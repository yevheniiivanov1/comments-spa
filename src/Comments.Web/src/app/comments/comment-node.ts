import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { AttachmentDto, CommentDto } from '../core/models';
import { CommentCard } from './comment-card';
import { ThreadNode } from './thread-tree';

/** A reply with its own replies below it, indented (cascade); renders itself recursively. */
@Component({
  selector: 'app-comment-node',
  imports: [CommentCard],
  template: `
    <app-comment-card
      [comment]="node().comment"
      [fresh]="freshIds().has(node().comment.id)"
      (reply)="reply.emit($event)"
      (openAttachment)="openAttachment.emit({ attachment: $event, comment: node().comment })"
    />
    @if (node().children.length) {
      <div class="children" [class.flat]="level() >= maxIndent">
        @for (child of node().children; track child.comment.id) {
          <app-comment-node
            animate.enter="node-in"
            [node]="child"
            [level]="level() + 1"
            [freshIds]="freshIds()"
            (reply)="reply.emit($event)"
            (openAttachment)="openAttachment.emit($event)"
          />
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .children {
      margin: 10px 0 0 32px;
      padding-left: 14px;
      border-left: 2px solid var(--border);
      display: grid;
      gap: 10px;
    }
    .children.flat {
      margin-left: 0;
    }
    .node-in {
      animation: node-in 0.35s ease-out;
    }
    @keyframes node-in {
      from {
        opacity: 0;
        transform: translateY(-6px);
      }
    }
    @media (max-width: 600px) {
      .children {
        margin-left: 10px;
        padding-left: 8px;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentNode {
  protected readonly maxIndent = 6;

  readonly node = input.required<ThreadNode>();
  readonly level = input(1);
  readonly freshIds = input<ReadonlySet<number>>(new Set());
  readonly reply = output<CommentDto>();
  readonly openAttachment = output<{ attachment: AttachmentDto; comment: CommentDto }>();
}
