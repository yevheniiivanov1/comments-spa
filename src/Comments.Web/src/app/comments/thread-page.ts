import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { CommentsApi } from '../core/comments-api';
import { LiveUpdates } from '../core/live-updates';
import { AttachmentDto, CommentDto } from '../core/models';
import { Icon } from '../shared/icons';
import { LightboxItem, LightboxService, toLightboxItem } from '../shared/lightbox';
import { CommentCard } from './comment-card';
import { CommentNode } from './comment-node';
import { CommentsStore } from './comments-store';
import { Composer } from './composer';
import { buildThreadTree } from './thread-tree';

/** How far the page loads a long thread to reach a linked reply (25 pages of 200). */
const MAX_REPLIES_TO_REACH = 5_000;

/** One thread on its own page (linked from search results), with the target comment highlighted. */
@Component({
  selector: 'app-thread-page',
  imports: [CommentCard, CommentNode, Icon, RouterLink],
  template: `
    <a routerLink="/" class="back"><app-icon name="chevron-left" [size]="16" /> All comments</a>
    @if (root.value(); as r) {
      <section class="thread">
        <app-comment-card [comment]="r" [showEmail]="true" [fresh]="store.fresh().has(r.id)" (reply)="composer.open($event)" (openAttachment)="open($event, r)" />
        @let thread = store.threads().get(r.id);
        @if (tree().length) {
          <div class="replies-tree">
            @for (node of tree(); track node.comment.id) {
              <app-comment-node
                [node]="node"
                [freshIds]="store.fresh()"
                (reply)="composer.open($event)"
                (openAttachment)="open($event.attachment, $event.comment)"
              />
            }
          </div>
        }
        <div class="foot">
          @if (thread?.loading) {
            <span class="muted">Loading replies…</span>
          } @else if (thread?.nextAfter) {
            <button type="button" class="link" (click)="store.loadThread(r.id, thread!.nextAfter!)">Load more replies</button>
          }
          <button type="button" class="button small" (click)="composer.open(r)"><app-icon name="reply" [size]="15" /> Reply</button>
        </div>
      </section>
    } @else if (root.error()) {
      <p class="muted">This comment does not exist.</p>
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    :host { display: grid; gap: 14px; }
    .back { display: inline-flex; align-items: center; gap: 4px; color: var(--accent); font-weight: 600; text-decoration: none; }
    .thread { display: grid; gap: 12px; padding: 16px; border-radius: 12px; background: var(--surface); box-shadow: var(--shadow); }
    .replies-tree { display: grid; gap: 12px; margin-left: 32px; padding-left: 14px; border-left: 2px solid var(--border); }
    .foot { display: flex; align-items: center; gap: 12px; }
    .foot .button { margin-left: auto; }
    @media (max-width: 600px) { .replies-tree { margin-left: 6px; padding-left: 8px; } }
  `,
  providers: [CommentsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThreadPage {
  readonly id = input.required<string>();
  readonly c = input<string>();

  protected readonly store = inject(CommentsStore);
  protected readonly composer = inject(Composer);
  private readonly api = inject(CommentsApi);
  private readonly lightbox = inject(LightboxService);

  protected readonly root = rxResource({
    params: () => Number(this.id()),
    stream: ({ params }) => this.api.getComment(params),
  });

  protected readonly tree = computed(() => {
    const id = Number(this.id());
    return buildThreadTree(id, this.store.threads().get(id)?.items ?? []);
  });

  constructor() {
    effect(() => {
      const id = Number(this.id());
      if (id > 0 && !this.store.threads().has(id)) {
        this.store.loadThread(id, 0);
      }
    });

    let highlighted = 0;
    effect(() => {
      const target = Number(this.c());
      const id = Number(this.id());
      const loaded = this.store.threads().get(id);
      if (!target || target === highlighted || !this.root.value() || !loaded || loaded.loading) {
        return;
      }
      // The linked reply may be beyond the first page of a long thread: keep loading until it is there.
      const present = target === id || loaded.items.some((c) => c.id === target);
      if (!present && loaded.nextAfter !== null && loaded.items.length < MAX_REPLIES_TO_REACH) {
        this.store.loadThread(id, loaded.nextAfter);
        return;
      }
      highlighted = target;
      this.store.highlight(target);
      setTimeout(() => document.getElementById(`comment-${target}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' }));
    });

    this.composer.posted.pipe(takeUntilDestroyed()).subscribe((comment) => this.store.addOwn(comment));
    inject(LiveUpdates).commentPosted.pipe(takeUntilDestroyed()).subscribe((comment) => this.store.addLive(comment));
  }

  protected open(attachment: AttachmentDto, owner: CommentDto): void {
    const items: LightboxItem[] = [];
    const root = this.root.value();
    if (root?.attachment) {
      items.push(toLightboxItem(root.attachment, root.userName));
    }
    for (const reply of this.store.threads().get(Number(this.id()))?.items ?? []) {
      if (reply.attachment) {
        items.push(toLightboxItem(reply.attachment, reply.userName));
      }
    }
    this.lightbox.open(items, toLightboxItem(attachment, owner.userName));
  }
}
