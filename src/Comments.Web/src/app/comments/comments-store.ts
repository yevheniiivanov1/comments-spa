import { Injectable, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { CommentsApi } from '../core/comments-api';
import { CommentDto, CommentPage, CommentSort, SortDirection } from '../core/models';

export interface PageQuery {
  page: number;
  sort: CommentSort;
  direction: SortDirection;
}

export interface ThreadState {
  open: boolean;
  loading: boolean;
  items: CommentDto[];
  nextAfter: number | null;
  error: string | null;
}

export const DEFAULT_QUERY: PageQuery = { page: 1, sort: 'createdAt', direction: 'desc' };
const FRESH_MS = 2500;

/**
 * State of the comments board: the current page of top-level comments (from the URL query),
 * the threads the reader opened, and which comments just arrived (for the highlight).
 */
@Injectable()
export class CommentsStore {
  private readonly api = inject(CommentsApi);

  readonly query = signal<PageQuery>(DEFAULT_QUERY);
  readonly page = rxResource<CommentPage, PageQuery>({
    params: () => this.query(),
    stream: ({ params }) => this.api.getPage(params.page, params.sort, params.direction),
  });
  readonly threads = signal<ReadonlyMap<number, ThreadState>>(new Map());
  readonly fresh = signal<ReadonlySet<number>>(new Set());
  /** Top-level comments posted by others since this page was loaded. */
  readonly newTopLevel = signal(0);
  /** Comments posted from this tab; their live echo is ignored. */
  private readonly own = new Set<number>();

  isDefaultView(): boolean {
    const q = this.query();
    return q.page === 1 && q.sort === 'createdAt' && q.direction === 'desc';
  }

  toggleThread(root: CommentDto): void {
    const state = this.threads().get(root.id);
    if (state?.open) {
      this.patchThread(root.id, { open: false });
      return;
    }
    this.patchThread(root.id, { open: true });
    if (!state || state.error) {
      this.loadThread(root.id, 0);
    }
  }

  loadThread(rootId: number, after: number): void {
    this.patchThread(rootId, { loading: true, error: null });
    this.api.getThread(rootId, after).subscribe({
      next: (page) => {
        const current = this.threads().get(rootId)?.items ?? [];
        const known = new Set(current.map((c) => c.id));
        this.patchThread(rootId, {
          loading: false,
          items: [...current, ...page.items.filter((c) => !known.has(c.id))],
          nextAfter: page.nextAfter,
        });
      },
      error: () => this.patchThread(rootId, { loading: false, error: 'Replies could not be loaded.' }),
    });
  }

  /** A comment posted from this tab: replies go into their open thread; top-level ones need a page reload. */
  addOwn(comment: CommentDto): void {
    this.own.add(comment.id);
    this.markFresh(comment.id);
    if (comment.rootId === null) {
      this.page.reload();
      return;
    }
    const loaded = this.threads().get(comment.rootId)?.items.length ?? 0;
    this.insertReply(comment);
    this.patchThread(comment.rootId, { open: true });
    if (loaded === 0) {
      this.loadThread(comment.rootId, 0);
    }
  }

  /** A comment that arrived over the live connection. */
  addLive(comment: CommentDto): void {
    if (this.own.has(comment.id)) {
      return;
    }
    if (comment.rootId === null) {
      if (!this.page.value()?.items.some((c) => c.id === comment.id)) {
        this.newTopLevel.update((n) => n + 1);
      }
      return;
    }
    if (this.insertReply(comment)) {
      this.markFresh(comment.id);
    }
  }

  showNewTopLevel(): void {
    this.newTopLevel.set(0);
    this.page.reload();
  }

  /**
   * Adds a reply to its thread when every reply of that thread is already loaded (otherwise it shows up with the next
   * page) and bumps the root's reply count; false if the reply was already there.
   */
  private insertReply(comment: CommentDto): boolean {
    const rootId = comment.rootId!;
    const thread = this.threads().get(rootId);
    if (thread?.items.some((c) => c.id === comment.id)) {
      return false;
    }
    if (thread && thread.items.length > 0 && !thread.loading && thread.nextAfter === null) {
      this.patchThread(rootId, { items: [...thread.items, comment] });
    }
    this.page.value.update((page) =>
      page && {
        ...page,
        items: page.items.map((c) => (c.id === rootId ? { ...c, replyCount: c.replyCount + 1 } : c)),
      },
    );
    return true;
  }

  private markFresh(id: number): void {
    this.fresh.update((ids) => new Set(ids).add(id));
    setTimeout(() => {
      this.fresh.update((ids) => {
        const next = new Set(ids);
        next.delete(id);
        return next;
      });
    }, FRESH_MS);
  }

  private patchThread(rootId: number, patch: Partial<ThreadState>): void {
    this.threads.update((threads) => {
      const next = new Map(threads);
      const current: ThreadState = threads.get(rootId) ?? {
        open: false,
        loading: false,
        items: [],
        nextAfter: null,
        error: null,
      };
      next.set(rootId, { ...current, ...patch });
      return next;
    });
  }
}
