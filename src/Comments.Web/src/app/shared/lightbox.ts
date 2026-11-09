import {
  ChangeDetectionStrategy,
  Component,
  Injectable,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CommentsApi } from '../core/comments-api';
import { AttachmentDto } from '../core/models';
import { Icon } from './icons';

export interface LightboxItem {
  kind: 'image' | 'text';
  url: string;
  title: string;
  width?: number | null;
  height?: number | null;
}

export function toLightboxItem(attachment: AttachmentDto, author: string): LightboxItem {
  return {
    kind: attachment.kind,
    url: attachment.url,
    title: `${attachment.fileName} · ${author}`,
    width: attachment.width,
    height: attachment.height,
  };
}

/** Lightbox state: a gallery of the attachments currently on screen and the one shown. */
@Injectable({ providedIn: 'root' })
export class LightboxService {
  readonly items = signal<LightboxItem[]>([]);
  readonly index = signal(0);
  readonly current = computed(() => this.items()[this.index()] ?? null);

  open(items: LightboxItem[], start: LightboxItem): void {
    const index = items.findIndex((i) => i.url === start.url);
    this.items.set(index >= 0 ? items : [start]);
    this.index.set(Math.max(0, index));
  }

  close(): void {
    this.items.set([]);
  }

  step(delta: number): void {
    const count = this.items().length;
    if (count > 1) {
      this.index.update((i) => (i + delta + count) % count);
    }
  }
}

/** Overlay that shows images and text files with fade and zoom transitions; Esc closes, arrows navigate. */
@Component({
  selector: 'app-lightbox',
  imports: [Icon],
  template: `
    @if (lightbox.current(); as item) {
      <div
        class="backdrop"
        animate.enter="backdrop-in"
        animate.leave="backdrop-out"
        (click)="lightbox.close()"
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="item.title"
      >
        <figure class="frame" (click)="$event.stopPropagation()">
          @for (shown of [item]; track shown.url) {
            <div class="content" animate.enter="zoom-in">
              @if (shown.kind === 'image') {
                <img [src]="shown.url" [alt]="shown.title" [attr.width]="shown.width" [attr.height]="shown.height" />
              } @else {
                @if (text(); as body) {
                  <pre class="text">{{ body }}</pre>
                } @else {
                  <div class="loading">Loading…</div>
                }
              }
            </div>
          }
          <figcaption>
            <span class="title">{{ item.title }}</span>
            @if (lightbox.items().length > 1) {
              <span class="counter">{{ lightbox.index() + 1 }} / {{ lightbox.items().length }}</span>
            }
            <a [href]="item.url" target="_blank" rel="noopener" class="open">Open original</a>
          </figcaption>
          <button type="button" class="close" (click)="lightbox.close()" aria-label="Close"><app-icon name="close" [size]="22" /></button>
          @if (lightbox.items().length > 1) {
            <button type="button" class="nav prev" (click)="lightbox.step(-1)" aria-label="Previous"><app-icon name="chevron-left" [size]="28" /></button>
            <button type="button" class="nav next" (click)="lightbox.step(1)" aria-label="Next"><app-icon name="chevron-right" [size]="28" /></button>
          }
        </figure>
      </div>
    }
  `,
  styleUrl: './lightbox.scss',
  host: { '(document:keydown)': 'onKey($event)' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Lightbox {
  protected readonly lightbox = inject(LightboxService);
  private readonly api = inject(CommentsApi);
  protected readonly text = signal<string | null>(null);

  constructor() {
    effect((onCleanup) => {
      const item = this.lightbox.current();
      this.text.set(null);
      if (item?.kind !== 'text') {
        return;
      }
      const subscription = this.api.getText(item.url).subscribe({
        next: (body) => this.text.set(body),
        error: () => this.text.set('The file could not be loaded.'),
      });
      onCleanup(() => subscription.unsubscribe());
    });
  }

  protected onKey(event: KeyboardEvent): void {
    if (!this.lightbox.current()) {
      return;
    }
    if (event.key === 'Escape') {
      this.lightbox.close();
    } else if (event.key === 'ArrowLeft') {
      this.lightbox.step(-1);
    } else if (event.key === 'ArrowRight') {
      this.lightbox.step(1);
    }
  }
}
