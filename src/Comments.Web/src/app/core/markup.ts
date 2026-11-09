/**
 * Client-side mirror of the server's CommentMarkup rules, for instant feedback while typing.
 * The server re-validates and rebuilds the markup; its result is the one that gets published.
 */

export const MAX_TEXT_LENGTH = 10_000;
const MAX_TITLE_LENGTH = 200;
const ALLOWED_TAGS = new Set(['a', 'code', 'i', 'strong']);
const ALLOWED_LIST = '<a>, <code>, <i>, <strong>';

const TAG =
  /<(?<close>\/)?(?<name>[A-Za-z][A-Za-z0-9]*)(?<attrs>(?:\s+[^\s=<>/"']+\s*=\s*(?:"[^"<]*"|'[^'<]*'))*)\s*(?<self>\/)?>/y;
const ATTRIBUTE = /\s+(?<name>[^\s=<>/"']+)\s*=\s*(?:"(?<dq>[^"<]*)"|'(?<sq>[^'<]*)')/g;

export function validateMarkup(input: string): string[] {
  if (!input || input.trim() === '') {
    return ['Text is required.'];
  }
  if (input.length > MAX_TEXT_LENGTH) {
    return [`Text must be at most ${MAX_TEXT_LENGTH} characters.`];
  }

  const errors: string[] = [];
  const open: string[] = [];
  let plain = '';
  let textStart = 0;
  let position = 0;

  while ((position = input.indexOf('<', position)) >= 0) {
    if (!looksLikeTag(input, position)) {
      position++;
      continue;
    }

    plain += decodeEntities(input.slice(textStart, position));
    TAG.lastIndex = position;
    const match = TAG.exec(input);

    if (match?.groups) {
      handleTag(match, errors, open);
      position += match[0].length;
    } else {
      const close = input.indexOf('>', position);
      const end = close < 0 ? input.length : close + 1;
      errors.push(describeMalformed(input.slice(position, end)));
      position = end;
    }
    textStart = position;
  }

  plain += decodeEntities(input.slice(textStart));
  for (const name of [...open].reverse()) {
    errors.push(`Tag <${name}> is not closed.`);
  }

  if (errors.length > 0) {
    return [...new Set(errors)];
  }
  return plain.trim() === '' ? ['Text is required.'] : [];
}

export function isHttpUrl(value: string): boolean {
  try {
    const url = new URL(value.trim());
    return (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname !== '';
  } catch {
    return false;
  }
}

function looksLikeTag(input: string, position: number): boolean {
  const next = input[position + 1];
  return next !== undefined && /[A-Za-z/!?]/.test(next);
}

function handleTag(match: RegExpExecArray, errors: string[], open: string[]): void {
  const groups = match.groups!;
  const name = groups['name'];
  const lower = name.toLowerCase();
  const source = match[0];

  if (!ALLOWED_TAGS.has(lower)) {
    errors.push(`Tag <${name}> is not allowed. Allowed tags: ${ALLOWED_LIST}.`);
    return;
  }
  if (name !== lower) {
    errors.push(`Tag names must be lowercase in XHTML: use <${lower}> instead of <${name}>.`);
    return;
  }

  if (groups['close']) {
    if (groups['attrs'] || groups['self']) {
      errors.push(`Closing tag "${source}" cannot have attributes.`);
    } else if (!open.includes(name)) {
      errors.push(`Closing tag </${name}> has no matching opening tag.`);
    } else if (open[open.length - 1] !== name) {
      errors.push(
        `Tags are not nested correctly: <${open[open.length - 1]}> must be closed before </${name}>.`,
      );
    } else {
      open.pop();
    }
    return;
  }

  if (groups['self']) {
    errors.push(`Tag <${name}> must have content and a closing </${name}>, not "${source}".`);
    return;
  }
  if (name === 'a' && open.includes('a')) {
    errors.push('Links cannot be nested inside other links.');
    return;
  }
  if (checkAttributes(name, groups['attrs'] ?? '', errors)) {
    open.push(name);
  }
}

function checkAttributes(tag: string, source: string, errors: string[]): boolean {
  const values = new Map<string, string>();
  let valid = true;

  for (const attribute of source.matchAll(ATTRIBUTE)) {
    const name = attribute.groups!['name'];
    const value = decodeEntities(attribute.groups!['dq'] ?? attribute.groups!['sq'] ?? '').trim();
    if (tag !== 'a' || (name !== 'href' && name !== 'title')) {
      errors.push(
        tag === 'a'
          ? `Attribute "${name}" is not allowed on <a>; only href and title are.`
          : `Tag <${tag}> cannot have attributes.`,
      );
      valid = false;
    } else if (values.has(name)) {
      errors.push(`Attribute "${name}" is repeated on <a>.`);
      valid = false;
    } else {
      values.set(name, value);
    }
  }

  if (tag !== 'a') {
    return valid;
  }

  const href = values.get('href');
  if (!href) {
    errors.push('Link <a> needs an href with the address.');
    valid = false;
  } else if (!isHttpUrl(href)) {
    errors.push(`Link address "${href}" must be a full http:// or https:// URL.`);
    valid = false;
  }
  if ((values.get('title')?.length ?? 0) > MAX_TITLE_LENGTH) {
    errors.push(`Link title must be at most ${MAX_TITLE_LENGTH} characters.`);
    valid = false;
  }
  return valid;
}

function describeMalformed(fragment: string): string {
  const shown = fragment.length > 40 ? fragment.slice(0, 40) + '…' : fragment;
  if (fragment.startsWith('<!--')) {
    return 'HTML comments are not allowed.';
  }
  if (fragment.startsWith('<!') || fragment.startsWith('<?')) {
    return `"${shown}" is not allowed.`;
  }
  return fragment.endsWith('>')
    ? `Tag "${shown}" is malformed: attribute values must be in quotes, e.g. <a href="https://example.com">.`
    : `Tag "${shown}" is not closed with '>'. To show a literal '<', write &lt;.`;
}

const NAMED_ENTITIES: Record<string, string> = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ' };

function decodeEntities(text: string): string {
  return text.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (entity, body: string) => {
    if (body[0] === '#') {
      const code = body[1] === 'x' || body[1] === 'X' ? parseInt(body.slice(2), 16) : parseInt(body.slice(1), 10);
      return Number.isFinite(code) && code <= 0x10ffff ? String.fromCodePoint(code) : entity;
    }
    return NAMED_ENTITIES[body.toLowerCase()] ?? entity;
  });
}
