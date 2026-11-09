import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Icon } from './icons';

/** Page links with the first, last and neighbouring pages; gaps become an ellipsis. */
@Component({
  selector: 'app-pagination',
  imports: [Icon],
  template: `
    @if (totalPages() > 1) {
      <nav class="pager" aria-label="Pages">
        <button type="button" [disabled]="page() <= 1" (click)="go.emit(page() - 1)" aria-label="Previous page">
          <app-icon name="chevron-left" [size]="16" />
        </button>
        @for (item of items(); track $index) {
          @if (item === null) {
            <span class="gap">…</span>
          } @else {
            <button type="button" [class.current]="item === page()" [attr.aria-current]="item === page() ? 'page' : null" (click)="go.emit(item)">
              {{ item }}
            </button>
          }
        }
        <button type="button" [disabled]="page() >= totalPages()" (click)="go.emit(page() + 1)" aria-label="Next page">
          <app-icon name="chevron-right" [size]="16" />
        </button>
      </nav>
    }
  `,
  styles: `
    .pager {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: 6px;
    }
    button {
      display: inline-grid;
      place-items: center;
      min-width: 36px;
      height: 36px;
      padding: 0 10px;
      border: 1px solid var(--border);
      border-radius: 8px;
      background: var(--surface);
      color: var(--text);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s ease, border-color 0.15s ease, transform 0.1s ease;
    }
    button:hover:not(:disabled) {
      border-color: var(--accent);
      color: var(--accent);
    }
    button:active:not(:disabled) {
      transform: scale(0.94);
    }
    button:disabled {
      opacity: 0.4;
      cursor: default;
    }
    button.current {
      background: var(--accent);
      border-color: var(--accent);
      color: #fff;
    }
    .gap {
      align-self: center;
      color: var(--text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Pagination {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly go = output<number>();

  protected readonly items = computed(() => {
    const page = this.page();
    const total = this.totalPages();
    const shown = new Set([1, total, page - 2, page - 1, page, page + 1, page + 2].filter((p) => p >= 1 && p <= total));
    const sorted = [...shown].sort((a, b) => a - b);
    const result: (number | null)[] = [];
    for (const [i, p] of sorted.entries()) {
      if (i > 0 && p - sorted[i - 1] > 1) {
        result.push(null);
      }
      result.push(p);
    }
    return result;
  });
}
