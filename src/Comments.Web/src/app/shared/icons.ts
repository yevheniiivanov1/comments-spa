import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type IconName =
  | 'reply'
  | 'link'
  | 'image'
  | 'file'
  | 'sort'
  | 'sort-asc'
  | 'sort-desc'
  | 'close'
  | 'chevron-left'
  | 'chevron-right'
  | 'refresh'
  | 'plus'
  | 'thread'
  | 'search'
  | 'eye'
  | 'paperclip';

/** Inline SVG icons (stroke style), so the page needs no icon font or external requests. */
@Component({
  selector: 'app-icon',
  template: `
    <svg
      viewBox="0 0 24 24"
      [attr.width]="size()"
      [attr.height]="size()"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      @switch (name()) {
        @case ('reply') { <svg:path d="M9 14 4 9l5-5" /><svg:path d="M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5V20" /> }
        @case ('link') { <svg:path d="M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7" /><svg:path d="M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7" /> }
        @case ('image') { <svg:rect x="3" y="3" width="18" height="18" rx="2" /><svg:circle cx="9" cy="9" r="2" /><svg:path d="m21 15-3-3a2 2 0 0 0-3 0l-9 9" /> }
        @case ('file') { <svg:path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" /><svg:path d="M14 3v5h5M9 13h6M9 17h6" /> }
        @case ('sort') { <svg:path d="m7 15 5 5 5-5M7 9l5-5 5 5" /> }
        @case ('sort-asc') { <svg:path d="m7 14 5-5 5 5" /> }
        @case ('sort-desc') { <svg:path d="m7 10 5 5 5-5" /> }
        @case ('close') { <svg:path d="M18 6 6 18M6 6l12 12" /> }
        @case ('chevron-left') { <svg:path d="m15 18-6-6 6-6" /> }
        @case ('chevron-right') { <svg:path d="m9 18 6-6-6-6" /> }
        @case ('refresh') { <svg:path d="M21 12a9 9 0 1 1-3-6.7L21 8" /><svg:path d="M21 3v5h-5" /> }
        @case ('plus') { <svg:path d="M12 5v14M5 12h14" /> }
        @case ('thread') { <svg:path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" /> }
        @case ('search') { <svg:circle cx="11" cy="11" r="7" /><svg:path d="m21 21-4.3-4.3" /> }
        @case ('eye') { <svg:path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" /><svg:circle cx="12" cy="12" r="3" /> }
        @case ('paperclip') { <svg:path d="m21 11-8.5 8.5a5 5 0 0 1-7-7L14 4a3.5 3.5 0 0 1 5 5l-8.5 8.5a2 2 0 0 1-3-3L15 7" /> }
      }
    </svg>
  `,
  styles: `
    :host { display: inline-flex; line-height: 0; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly size = input(18);
}
