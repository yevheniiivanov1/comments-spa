import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { AttachmentDto, CommentDto } from '../core/models';
import { Avatar } from '../shared/avatar';
import { Icon } from '../shared/icons';
import { FileSizePipe } from '../shared/file-size';

/** One comment as in the design: header strip with avatar, name, date and actions, then the text and the attachment. */
@Component({
  selector: 'app-comment-card',
  imports: [Avatar, Icon, DatePipe, FileSizePipe],
  template: `
    @let c = comment();
    <article class="card" [class.fresh]="fresh()" [attr.id]="'comment-' + c.id">
      <header>
        <app-avatar [name]="c.userName" />
        <div class="who">
          @if (c.homePage) {
            <a class="name" [href]="c.homePage" target="_blank" rel="nofollow ugc noopener" [title]="c.homePage">
              {{ c.userName }} <app-icon name="link" [size]="13" />
            </a>
          } @else {
            <span class="name">{{ c.userName }}</span>
          }
          <time [attr.datetime]="c.createdAt">{{ c.createdAt | date: "dd.MM.yy 'at' HH:mm" }}</time>
          @if (showEmail()) {
            <span class="email">{{ c.email }}</span>
          }
        </div>
        <div class="actions">
          <button type="button" class="action" (click)="reply.emit(c)" title="Reply">
            <app-icon name="reply" [size]="16" />
            <span>Reply</span>
          </button>
        </div>
      </header>

      <div class="body comment-html" [innerHTML]="c.html"></div>

      @if (c.attachment; as file) {
        <div class="attachment">
          @if (file.kind === 'image') {
            <button type="button" class="thumb" (click)="openAttachment.emit(file)" [title]="'View ' + file.fileName">
              <img [src]="file.url" [alt]="file.fileName" loading="lazy" [attr.width]="thumbWidth(file)" [attr.height]="thumbHeight(file)" />
              <span class="zoom"><app-icon name="eye" [size]="20" /></span>
            </button>
          } @else {
            <button type="button" class="file-chip" (click)="openAttachment.emit(file)">
              <app-icon name="file" [size]="18" />
              <span class="file-name">{{ file.fileName }}</span>
              <span class="file-size">{{ file.sizeBytes | fileSize }}</span>
            </button>
          }
        </div>
      }
    </article>
  `,
  styleUrl: './comment-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentCard {
  readonly comment = input.required<CommentDto>();
  readonly showEmail = input(false);
  readonly fresh = input(false);
  readonly reply = output<CommentDto>();
  readonly openAttachment = output<AttachmentDto>();

  protected thumbWidth(file: AttachmentDto): number | null {
    return file.width && file.height ? Math.round(file.width * this.thumbScale(file)) : null;
  }

  protected thumbHeight(file: AttachmentDto): number | null {
    return file.width && file.height ? Math.round(file.height * this.thumbScale(file)) : null;
  }

  private thumbScale(file: AttachmentDto): number {
    return Math.min(1, 160 / (file.width ?? 160), 120 / (file.height ?? 120));
  }
}
