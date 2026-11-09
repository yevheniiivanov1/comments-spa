import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { CommentForm } from './comments/comment-form';
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
export class App {}
