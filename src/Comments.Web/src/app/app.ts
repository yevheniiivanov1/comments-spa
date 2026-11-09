import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { CommentForm } from './comments/comment-form';
import { LiveState, LiveUpdates } from './core/live-updates';
import { Icon } from './shared/icons';
import { Lightbox } from './shared/lightbox';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, CommentForm, Lightbox, Icon],
  template: `
    <header class="topbar">
      <div class="container topbar-inner">
        <a routerLink="/" class="brand">
          <span class="logo" aria-hidden="true">💬</span>
          <span>Comments</span>
        </a>
        <form class="search" role="search" (submit)="$event.preventDefault(); search(box.value)">
          <app-icon name="search" [size]="16" />
          <input #box type="search" placeholder="Search comments" aria-label="Search comments" maxlength="200" />
        </form>
        <span class="live" [attr.data-state]="live.state()" [title]="liveTitle[live.state()]">
          <span class="dot"></span><span class="live-label">{{ liveLabel[live.state()] }}</span>
        </span>
      </div>
    </header>
    <main class="container">
      <router-outlet />
    </main>
    <footer class="container footer">
      ASP.NET Core · EF Core · SQL Server · Redis · RabbitMQ · Elasticsearch · Angular
    </footer>
    <app-comment-form />
    <app-lightbox />
  `,
  styleUrl: './app.scss',
  host: { '(document:click)': 'openCommentLinksInNewTab($event)' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly live = inject(LiveUpdates);
  private readonly router = inject(Router);

  protected readonly liveLabel: Record<LiveState, string> = { live: 'Live', connecting: 'Connecting…', offline: 'Offline' };
  protected readonly liveTitle: Record<LiveState, string> = {
    live: 'New comments appear as they are posted',
    connecting: 'Connecting to live updates',
    offline: 'Live updates are unavailable; reload to see new comments',
  };

  /** Links written in comments lead to other sites, so they open in a new tab instead of replacing the app. */
  protected openCommentLinksInNewTab(event: MouseEvent): void {
    const link = (event.target as Element | null)?.closest<HTMLAnchorElement>('.comment-html a[href]');
    if (link && event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
      event.preventDefault();
      window.open(link.href, '_blank', 'noopener,noreferrer');
    }
  }

  protected search(query: string): void {
    const q = query.trim();
    if (q) {
      void this.router.navigate(['/search'], { queryParams: { q } });
    }
  }
}
