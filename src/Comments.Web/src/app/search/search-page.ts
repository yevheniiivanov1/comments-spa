import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { GraphQLClient } from '../core/graphql';
import { Avatar } from '../shared/avatar';
import { Pagination } from '../shared/pagination';

/** Full-text search results (Elasticsearch over GraphQL); each hit links to its thread with the comment highlighted. */
@Component({
  selector: 'app-search-page',
  imports: [Avatar, Pagination, DatePipe, DecimalPipe, RouterLink],
  template: `
    <section class="search">
      <h1>Search</h1>
      @if (query()) {
        <p class="muted">
          @if (results.value(); as r) {
            {{ r.total | number }} {{ r.total === 1 ? 'comment' : 'comments' }} for “{{ query() }}”
          } @else if (results.isLoading()) {
            Searching for “{{ query() }}”…
          }
        </p>
      } @else {
        <p class="muted">Type a word in the search box above. Matches any word form in English and Russian.</p>
      }

      @if (results.error()) {
        <div class="error">Search is unavailable right now. Please try again later.</div>
      }

      <ol class="hits" [class.loading]="results.isLoading()">
        @for (hit of results.value()?.hits ?? []; track hit.id) {
          <li animate.enter="hit-in">
            <a class="hit" [routerLink]="['/thread', hit.rootId ?? hit.id]" [queryParams]="{ c: hit.id }">
              <app-avatar [name]="hit.userName" />
              <div class="body">
                <div class="meta">
                  <strong>{{ hit.userName }}</strong>
                  <span class="muted">{{ hit.createdAt | date: "dd.MM.yy 'at' HH:mm" }}</span>
                  @if (hit.rootId) {
                    <span class="tag">reply</span>
                  }
                </div>
                <p class="snippet" [innerHTML]="hit.snippet"></p>
              </div>
            </a>
          </li>
        }
      </ol>

      @if (results.value(); as r) {
        <app-pagination [page]="r.page" [totalPages]="totalPages()" (go)="goTo($event)" />
      }
    </section>
  `,
  styles: `
    .search { display: grid; gap: 14px; }
    h1 { margin: 0; font-size: 28px; }
    p { margin: 0; }
    .hits { display: grid; gap: 10px; margin: 0; padding: 0; list-style: none; transition: opacity 0.2s ease; }
    .hits.loading { opacity: 0.5; }
    .hit {
      display: flex; gap: 14px; padding: 14px 16px; border-radius: 12px;
      background: var(--surface); box-shadow: var(--shadow); color: inherit; text-decoration: none;
      transition: transform 0.15s ease, box-shadow 0.15s ease;
    }
    .hit:hover { transform: translateY(-2px); box-shadow: 0 10px 28px rgb(20 30 60 / 0.12); }
    app-avatar { --avatar-size: 40px; flex: none; }
    .body { min-width: 0; }
    .meta { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px; font-size: 14px; }
    .tag { padding: 1px 8px; border-radius: 999px; background: var(--accent-bg); color: var(--accent); font-size: 12px; font-weight: 600; }
    .snippet { margin-top: 4px; line-height: 1.55; overflow-wrap: anywhere; }
    .snippet ::ng-deep mark { padding: 0 2px; border-radius: 3px; background: #ffe58a; color: inherit; }
    .error { padding: 10px 12px; border-radius: 8px; background: var(--danger-bg); color: var(--danger); }
    .hit-in { animation: hit-in 0.3s ease-out; }
    @keyframes hit-in { from { opacity: 0; transform: translateY(6px); } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchPage {
  private readonly graphql = inject(GraphQLClient);
  private readonly router = inject(Router);
  private readonly params = toSignal(inject(ActivatedRoute).queryParamMap, { requireSync: true });

  protected readonly query = computed(() => (this.params().get('q') ?? '').trim());
  private readonly page = computed(() => Math.max(1, Number(this.params().get('page')) || 1));

  protected readonly results = rxResource({
    params: () => ({ query: this.query(), page: this.page() }),
    stream: ({ params }) =>
      params.query ? this.graphql.search(params.query, params.page) : of({ total: 0, page: 1, pageSize: 20, hits: [] }),
  });

  protected readonly totalPages = computed(() => {
    const r = this.results.value();
    return r ? Math.min(50, Math.max(1, Math.ceil(r.total / r.pageSize))) : 1;
  });

  protected goTo(page: number): void {
    void this.router.navigate([], { queryParams: { q: this.query(), page }, queryParamsHandling: 'merge' });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
