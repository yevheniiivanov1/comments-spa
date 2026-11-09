// Load test for the comments service: the traffic mix of 100k users a day at their busiest, plus headroom.
// Profiles (PROFILE env): smoke (sanity), peak (the busiest hour with spikes, the target), stress (3x peak),
// write (1M new comments a day at peak: ~60 posts a second). See docs/architecture.md for how the rates were derived.
// k6 calls the API directly (no Caddy, no TLS) with the load-test overlay, which lifts rate limits and exposes CAPTCHA answers.

import http from 'k6/http';
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { WebSocket } from 'k6/websockets';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const WS_BASE = BASE.replace(/^http/, 'ws');

const PROFILES = {
  smoke: { browse: 5, threads: 3, search: 1, post: 1, sockets: 10, hold: '30s' },
  peak: { browse: 120, threads: 50, search: 10, post: 4, sockets: 500, hold: '3m' },
  stress: { browse: 360, threads: 150, search: 30, post: 12, sockets: 1500, hold: '3m' },
  write: { browse: 60, threads: 30, search: 5, post: 60, sockets: 300, hold: '3m' },
};
const profile = PROFILES[__ENV.PROFILE || 'peak'];
const RAMP = '30s';
const totalSeconds = 30 + parseDuration(profile.hold) + 15;

const liveDelivery = new Trend('live_delivery_ms', true);
const liveEvents = new Counter('live_events');
const posted = new Counter('comments_posted');

const SORTS = ['createdAt', 'userName', 'email'];
const PHOTO = open('./photo.jpg', 'b');
const POPULAR = loadPopularThreads();
const WORDS = [
  'lorem', 'ipsum', 'dolor', 'amet', 'consectetur', 'voluptas', 'quia', 'neque', 'porro', 'aut',
  'research', 'competitors', 'political', 'culture', 'opportunities', 'semantic', 'analysis',
];

function arrival(rate) {
  return {
    executor: 'ramping-arrival-rate',
    startRate: 0,
    timeUnit: '1s',
    preAllocatedVUs: Math.max(5, Math.ceil(rate / 2)),
    maxVUs: Math.max(20, rate * 4),
    stages: [
      { target: rate, duration: RAMP },
      { target: rate, duration: profile.hold },
      { target: 0, duration: '15s' },
    ],
  };
}

export const options = {
  scenarios: {
    browse: { ...arrival(profile.browse), exec: 'browse' },
    threads: { ...arrival(profile.threads), exec: 'threads' },
    search: { ...arrival(profile.search), exec: 'search' },
    post: { ...arrival(profile.post), exec: 'post' },
    sockets: { executor: 'constant-vus', vus: profile.sockets, duration: `${totalSeconds}s`, exec: 'listen', gracefulStop: '10s' },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:browse}': ['p(95)<300'],
    'http_req_duration{scenario:threads}': ['p(95)<300'],
    'http_req_duration{scenario:search}': ['p(95)<500'],
    'http_req_duration{scenario:post}': ['p(95)<800'],
    live_delivery_ms: ['p(95)<2000'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

/** The seed tool writes the ids of its long threads (about a thousand replies each) next to the results. */
function loadPopularThreads() {
  try {
    return JSON.parse(open('/results/popular-threads.json'));
  } catch {
    return [];
  }
}

/**
 * Collects thread ids once, before the load starts: recent ones from the first pages (likely cached) and a sample from
 * random deep pages (cold), so thread reads are not only cheap cache hits.
 */
export function setup() {
  const first = http.get(`${BASE}/api/comments`).json();
  const recent = [];
  for (let page = 1; page <= 8; page++) {
    for (const c of http.get(`${BASE}/api/comments?page=${page}`).json().items) {
      recent.push(c.id);
    }
  }
  const sampled = [];
  for (let i = 0; i < 20; i++) {
    for (const c of http.get(`${BASE}/api/comments?page=${randomInt(9, first.totalPages)}`).json().items) {
      sampled.push(c.id);
    }
  }
  return { recent, sampled, popular: POPULAR, totalPages: first.totalPages };
}

export function browse(data) {
  const roll = Math.random();
  let url = `${BASE}/api/comments`;
  if (roll >= 0.6 && roll < 0.85) {
    url += `?page=${randomInt(2, 20)}&sort=${pick(SORTS)}&direction=${pick(['asc', 'desc'])}`;
  } else if (roll >= 0.85) {
    url += `?page=${randomInt(1, data.totalPages)}&sort=${pick(SORTS)}&direction=${pick(['asc', 'desc'])}`;
  }
  const response = http.get(url, { tags: { name: 'GET /api/comments' } });
  check(response, { 'page 200': (r) => r.status === 200 });
}

/** 50% recent threads, 25% random older ones, 25% long threads read page by page (up to 3 pages of 200). */
export function threads(data) {
  const roll = Math.random();
  const pool = roll < 0.5 || (!data.sampled.length && !data.popular.length) ? data.recent : roll < 0.75 || !data.popular.length ? data.sampled : data.popular;
  const id = pick(pool.length ? pool : data.recent);
  let after = 0;
  for (let page = 0; page < 3; page++) {
    const response = http.get(`${BASE}/api/comments/${id}/thread?after=${after}`, { tags: { name: 'GET /api/comments/{id}/thread' } });
    if (!check(response, { 'thread 200': (r) => r.status === 200 })) {
      return;
    }
    const next = response.json().nextAfter;
    if (next === null || pool !== data.popular) {
      return;
    }
    after = next;
  }
}

export function search() {
  const response = http.get(`${BASE}/api/search?q=${encodeURIComponent(pick(WORDS))}`, { tags: { name: 'GET /api/search' } });
  check(response, { 'search 200': (r) => r.status === 200 });
}

export function post(data) {
  const captcha = http.get(`${BASE}/api/captcha`, { tags: { name: 'GET /api/captcha' } }).json();
  const fields = {
    userName: `Load${__VU}`,
    email: `load${__VU}@loadtest.example.com`,
    text: `${pick(WORDS)} <strong>${pick(WORDS)}</strong> ${pick(WORDS)} — load test ${Date.now()}`,
    captchaId: captcha.id,
    captchaAnswer: captcha.answer,
  };
  if (Math.random() < 0.3 && data.recent.length) {
    fields.parentId = String(pick(Math.random() < 0.2 && data.popular.length ? data.popular : data.recent));
  }
  const tags = { tags: { name: 'POST /api/comments' } };
  const attachment = Math.random();
  let response;
  if (attachment < 0.1) {
    fields.file = http.file(PHOTO, 'photo.jpg', 'image/jpeg');
    response = http.post(`${BASE}/api/comments`, fields, tags);
  } else if (attachment < 0.2) {
    fields.file = http.file(`notes from VU ${__VU}\n`.repeat(50), 'notes.txt', 'text/plain');
    response = http.post(`${BASE}/api/comments`, fields, tags);
  } else {
    const form = multipart(fields);
    response = http.post(`${BASE}/api/comments`, form.body, { ...tags, headers: { 'Content-Type': form.contentType } });
  }
  if (check(response, { 'posted 201': (r) => r.status === 201 })) {
    posted.add(1);
  }
}

/** A reader with the page open: holds a SignalR WebSocket and measures how fast new comments arrive. */
export function listen() {
  const negotiate = http.post(`${BASE}/hubs/comments/negotiate?negotiateVersion=1`, null, { tags: { name: 'POST /hubs/comments/negotiate' } });
  if (!check(negotiate, { 'negotiate 200': (r) => r.status === 200 })) {
    return;
  }
  const token = negotiate.json().connectionToken;
  const socket = new WebSocket(`${WS_BASE}/hubs/comments?id=${token}`);

  socket.onopen = () => socket.send('{"protocol":"json","version":1}\u001e');
  socket.onmessage = (event) => {
    for (const frame of String(event.data).split('\u001e')) {
      if (!frame) {
        continue;
      }
      const message = JSON.parse(frame);
      if (message.type === 1 && message.target === 'commentPosted') {
        liveEvents.add(1);
        liveDelivery.add(Date.now() - Date.parse(message.arguments[0].createdAt));
      } else if (message.type === 6) {
        socket.send('{"type":6}\u001e');
      }
    }
  };
  socket.onerror = (e) => console.warn(`socket error: ${e.error}`);
  setTimeout(() => socket.close(), totalSeconds * 1000);
}

/** The API takes multipart/form-data, as the browser sends it; k6 only builds multipart itself when a file is attached. */
function multipart(fields) {
  const boundary = `----k6${Math.random().toString(16).slice(2)}`;
  let body = '';
  for (const [name, value] of Object.entries(fields)) {
    body += `--${boundary}\r\nContent-Disposition: form-data; name="${name}"\r\n\r\n${value}\r\n`;
  }
  return { body: `${body}--${boundary}--\r\n`, contentType: `multipart/form-data; boundary=${boundary}` };
}

function pick(items) {
  return items[Math.floor(Math.random() * items.length)];
}

function randomInt(min, max) {
  return min + Math.floor(Math.random() * (Math.max(min, max) - min + 1));
}

function parseDuration(value) {
  const match = /^(\d+)(s|m)$/.exec(value);
  return match[2] === 'm' ? Number(match[1]) * 60 : Number(match[1]);
}

export function handleSummary(data) {
  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  return {
    [`/results/${__ENV.PROFILE || 'peak'}-${stamp}.json`]: JSON.stringify(data, null, 2),
    stdout: summarize(data),
  };
}

/** A compact text summary: per-endpoint latency, throughput, errors and threshold results. */
function summarize(data) {
  const m = data.metrics;
  const lines = [`\nProfile: ${__ENV.PROFILE || 'peak'}  duration: ${(data.state.testRunDurationMs / 1000).toFixed(0)}s`];
  const row = (label, metric) => {
    if (!metric) return;
    const v = metric.values;
    lines.push(
      `${label.padEnd(34)} avg ${fmt(v.avg)}  p95 ${fmt(v['p(95)'])}  p99 ${fmt(v['p(99)'])}  max ${fmt(v.max)}`,
    );
  };
  for (const scenario of ['browse', 'threads', 'search', 'post']) {
    row(`http_req_duration{scenario:${scenario}}`, m[`http_req_duration{scenario:${scenario}}`]);
  }
  row('live_delivery_ms', m.live_delivery_ms);
  lines.push(`http requests: ${m.http_reqs.values.count} (${m.http_reqs.values.rate.toFixed(1)}/s)`);
  lines.push(`failed requests: ${(m.http_req_failed.values.rate * 100).toFixed(2)}%`);
  lines.push(`comments posted: ${m.comments_posted ? m.comments_posted.values.count : 0}`);
  lines.push(`live events received: ${m.live_events ? m.live_events.values.count : 0}`);
  lines.push('thresholds:');
  for (const [name, metric] of Object.entries(m)) {
    for (const [expr, result] of Object.entries(metric.thresholds || {})) {
      lines.push(`  ${result.ok ? 'PASS' : 'FAIL'}  ${name}: ${expr}`);
    }
  }
  return lines.join('\n') + '\n';
}

function fmt(ms) {
  return `${(ms ?? 0).toFixed(1).padStart(7)}ms`;
}
