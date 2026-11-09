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

function samePageQuery(a: PageQuery, b: PageQuery): boolean {
  return a.page === b.page && a.sort === b.sort && a.direction === b.direction;
}

/**
 * State of the comments board: the current page of top-level comments (from the URL query),
 * the threads the reader opened, and which comments just arrived (for the highlight).
 */
@Injectable()
export class CommentsStore {
  private readonly api = inject(CommentsApi);

  /** Compared by value: the page sets it from the URL on every navigation, and an equal query must not refetch. */
  readonly query = signal<PageQuery>(DEFAULT_QUERY, { equal: samePageQuery });
  private readonly pageEnabled = signal(false);
  readonly page = rxResource<CommentPage, PageQuery | undefined>({
    params: () => (this.pageEnabled() ? this.query() : undefined),
    stream: ({ params }) => this.api.getPage(params!.page, params!.sort, params!.direction),
  });
  readonly threads = signal<ReadonlyMap<number, ThreadState>>(new Map());
  readonly fresh = signal<ReadonlySet<number>>(new Set());
  /** Top-level comments posted by others since this page was loaded. */
  readonly newTopLevel = signal(0);
  /** Comments posted from this tab; their live echo is ignored. */
  private readonly own = new Set<number>();

  /** Loads pages of top-level comments; a store used only for one thread never does. */
  enablePage(): void {
    this.pageEnabled.set(true);
  }

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
    this.highlight(comment.id);
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
      this.highlight(comment.id);
    }
  }

  showNewTopLevel(): void {
    this.newTopLevel.set(0);
    this.page.reload();
  }

  /**
   * Adds a reply to its loaded thread and bumps the root's reply count; false if the reply was already there.
   * A reply is the newest comment of its thread, so it is safe to show it before the pages in between are loaded:
   * "Load more" continues from the thread's own cursor and duplicates are skipped.
   */
  private insertReply(comment: CommentDto): boolean {
    const rootId = comment.rootId!;
    const thread = this.threads().get(rootId);
    if (thread?.items.some((c) => c.id === comment.id)) {
      return false;
    }
    if (thread && thread.items.length > 0 && !thread.loading) {
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

  /** Highlights a comment for a moment (just posted, just arrived, or the target of a link). */
  highlight(id: number): void {
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
