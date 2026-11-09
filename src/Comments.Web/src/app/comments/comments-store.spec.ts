import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CommentsApi } from '../core/comments-api';
import { CommentDto, CommentPage, ThreadPage } from '../core/models';
import { CommentsStore } from './comments-store';

function comment(id: number, parentId: number | null = null, rootId: number | null = null, replyCount = 0): CommentDto {
  return {
    id,
    parentId,
    rootId,
    depth: parentId === null ? 0 : 1,
    userName: `User${id}`,
    email: `user${id}@example.com`,
    homePage: null,
    html: `comment ${id}`,
    createdAt: '2026-11-01T12:00:00Z',
    replyCount,
    attachment: null,
  };
}

describe('CommentsStore', () => {
  let store: CommentsStore;
  let threadPages: Record<number, ThreadPage>;
  let threadRequests: { rootId: number; after: number }[];
  let pageRequests: number;

  beforeEach(async () => {
    threadRequests = [];
    pageRequests = 0;
    threadPages = {};
    const page: CommentPage = {
      items: [comment(1, null, null, 3), comment(2)],
      page: 1,
      pageSize: 25,
      totalCount: 2,
      totalPages: 1,
      sort: 'createdAt',
      direction: 'desc',
    };
    const api: Partial<CommentsApi> = {
      getPage: () => {
        pageRequests++;
        return of(page);
      },
      getThread: (rootId: number, after = 0) => {
        threadRequests.push({ rootId, after });
        return of(threadPages[rootId] ?? { rootId, items: [], nextAfter: null });
      },
    };

    TestBed.configureTestingModule({ providers: [CommentsStore, { provide: CommentsApi, useValue: api }] });
    store = TestBed.inject(CommentsStore);
    store.enablePage();
    await TestBed.inject(ApplicationRef).whenStable();
  });

  it('does not refetch the page when the query is set to an equal value', async () => {
    const appRef = TestBed.inject(ApplicationRef);
    store.query.set({ page: 1, sort: 'createdAt', direction: 'desc' });
    await appRef.whenStable();
    expect(pageRequests).toBe(1);

    store.query.set({ page: 2, sort: 'createdAt', direction: 'desc' });
    await appRef.whenStable();
    expect(pageRequests).toBe(2);
  });

  it('counts new threads posted by others instead of moving the list under the reader', () => {
    store.addLive(comment(10));

    expect(store.newTopLevel()).toBe(1);
    expect(store.page.value()?.items.map((c) => c.id)).toEqual([1, 2]);
  });

  it('ignores the live echo of a comment posted from this tab', () => {
    store.addOwn(comment(11));
    store.addLive(comment(11));

    expect(store.newTopLevel()).toBe(0);
  });

  it('adds a live reply to an open thread once and bumps the reply count', () => {
    threadPages[1] = { rootId: 1, items: [comment(3, 1, 1)], nextAfter: null };
    store.toggleThread(comment(1, null, null, 3));

    store.addLive(comment(4, 3, 1));
    store.addLive(comment(4, 3, 1));

    expect(store.threads().get(1)?.items.map((c) => c.id)).toEqual([3, 4]);
    expect(store.page.value()?.items[0].replyCount).toBe(4);
    expect(store.fresh().has(4)).toBe(true);
  });

  it('shows a new reply in a partly loaded thread and keeps the cursor for the pages in between', () => {
    threadPages[1] = { rootId: 1, items: [comment(3, 1, 1)], nextAfter: 3 };
    store.toggleThread(comment(1, null, null, 3));

    store.addOwn(comment(500, 1, 1));

    const thread = store.threads().get(1);
    expect(thread?.items.map((c) => c.id)).toEqual([3, 500]);
    expect(thread?.nextAfter).toBe(3);
  });

  it('opens and loads the thread when the reader replies in a thread that was not open', () => {
    store.addOwn(comment(20, 2, 2));

    expect(store.threads().get(2)?.open).toBe(true);
    expect(threadRequests).toEqual([{ rootId: 2, after: 0 }]);
  });
});
