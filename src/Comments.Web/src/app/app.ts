import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { CommentForm } from './comments/comment-form';
import { LiveState, LiveUpdates } from './core/live-updates';
import { Lightbox } from './shared/lightbox';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, CommentForm, Lightbox],
  template: `
    <header class="topbar">
      <div class="container topbar-inner">
        <a routerLink="/" class="brand">
          <span class="logo" aria-hidden="true">💬</span>
          <span>Comments</span>
        </a>
        <span class="live" [attr.data-state]="live.state()" [title]="liveTitle[live.state()]">
          <span class="dot"></span>{{ liveLabel[live.state()] }}
        </span>
      </div>
    </header>
    <main class="container">
      <router-outlet />
    </main>
    <footer class="container footer">
      ASP.NET Core · EF Core · SQL Server · Angular — a test task
    </footer>
    <app-comment-form />
    <app-lightbox />
  `,
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly live = inject(LiveUpdates);
  protected readonly liveLabel: Record<LiveState, string> = { live: 'Live', connecting: 'Connecting…', offline: 'Offline' };
  protected readonly liveTitle: Record<LiveState, string> = {
    live: 'New comments appear as they are posted',
    connecting: 'Connecting to live updates',
    offline: 'Live updates are unavailable; reload to see new comments',
  };
}
