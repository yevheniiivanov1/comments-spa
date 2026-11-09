import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { LiveUpdates } from '../core/live-updates';
import { AttachmentDto, CommentDto, CommentSort, SortDirection } from '../core/models';
import { Avatar } from '../shared/avatar';
import { Icon } from '../shared/icons';
import { LightboxItem, LightboxService, toLightboxItem } from '../shared/lightbox';
import { Pagination } from '../shared/pagination';
import { CommentCard } from './comment-card';
import { CommentNode } from './comment-node';
import { CommentsStore, DEFAULT_QUERY, PageQuery } from './comments-store';
import { Composer } from './composer';
import { buildThreadTree } from './thread-tree';

const SORTS: readonly CommentSort[] = ['createdAt', 'userName', 'email'];

/**
 * The board: top-level comments as a sortable table (User Name, E-mail, date; newest first by default),
 * 25 per page, each row opening its thread of replies as a cascade. Page and sort live in the URL.
 */
@Component({
  selector: 'app-comments-page',
  imports: [Avatar, Icon, Pagination, CommentCard, CommentNode, DatePipe, DecimalPipe],
  templateUrl: './comments-page.html',
  styleUrl: './comments-page.scss',
  providers: [CommentsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentsPage {
  protected readonly store = inject(CommentsStore);
  protected readonly composer = inject(Composer);
  private readonly lightbox = inject(LightboxService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });

  protected readonly page = computed(() => this.store.page.value());
  protected readonly trees = computed(() => {
    const trees = new Map<number, ReturnType<typeof buildThreadTree>>();
    for (const [rootId, thread] of this.store.threads()) {
      trees.set(rootId, buildThreadTree(rootId, thread.items));
    }
    return trees;
  });

  constructor() {
    this.store.query.set(this.parseQuery());
    this.store.enablePage();
    effect(() => this.store.query.set(this.parseQuery()));
    this.composer.posted.pipe(takeUntilDestroyed()).subscribe((comment) => this.onPosted(comment));
    inject(LiveUpdates).commentPosted.pipe(takeUntilDestroyed()).subscribe((comment) => this.store.addLive(comment));
  }

  protected sortBy(sort: CommentSort): void {
    const query = this.store.query();
    const direction: SortDirection =
      query.sort === sort ? (query.direction === 'desc' ? 'asc' : 'desc') : sort === 'createdAt' ? 'desc' : 'asc';
    this.navigate({ page: 1, sort, direction });
  }

  protected goTo(page: number): void {
    this.navigate({ ...this.store.query(), page });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected ariaSort(sort: CommentSort): 'ascending' | 'descending' | 'none' {
    const query = this.store.query();
    return query.sort !== sort ? 'none' : query.direction === 'asc' ? 'ascending' : 'descending';
  }

  /** Clicking the text opens the thread, except when the click is on a link inside the comment. */
  protected onTextClick(event: MouseEvent, comment: CommentDto): void {
    if (!(event.target as Element | null)?.closest('a')) {
      this.store.toggleThread(comment);
    }
  }

  protected openAttachment(attachment: AttachmentDto, owner: CommentDto): void {
    this.lightbox.open(this.visibleAttachments(), toLightboxItem(attachment, owner.userName));
  }

  protected showNew(): void {
    if (this.store.isDefaultView()) {
      this.store.showNewTopLevel();
    } else {
      this.store.newTopLevel.set(0);
      this.navigate(DEFAULT_QUERY);
    }
  }

  private onPosted(comment: CommentDto): void {
    if (comment.rootId === null && !this.store.isDefaultView()) {
      this.navigate(DEFAULT_QUERY);
    }
    this.store.addOwn(comment);
  }

  /** Every attachment on screen, in reading order, so the lightbox can step through them. */
  private visibleAttachments(): LightboxItem[] {
    const items: LightboxItem[] = [];
    for (const root of this.page()?.items ?? []) {
      if (root.attachment) {
        items.push(toLightboxItem(root.attachment, root.userName));
      }
      const thread = this.store.threads().get(root.id);
      if (thread?.open) {
        for (const reply of thread.items) {
          if (reply.attachment) {
            items.push(toLightboxItem(reply.attachment, reply.userName));
          }
        }
      }
    }
    return items;
  }

  private parseQuery(): PageQuery {
    const params = this.params();
    const page = Number(params.get('page'));
    const sort = params.get('sort') as CommentSort;
    const direction = params.get('dir');
    return {
      page: Number.isInteger(page) && page > 0 ? page : 1,
      sort: SORTS.includes(sort) ? sort : DEFAULT_QUERY.sort,
      direction: direction === 'asc' || direction === 'desc' ? direction : DEFAULT_QUERY.direction,
    };
  }

  private navigate(query: PageQuery): void {
    const isDefault = (key: keyof PageQuery) => query[key] === DEFAULT_QUERY[key];
    void this.router.navigate([], {
      queryParams: {
        page: isDefault('page') ? null : query.page,
        sort: isDefault('sort') ? null : query.sort,
        dir: isDefault('direction') ? null : query.direction,
      },
    });
  }
}
