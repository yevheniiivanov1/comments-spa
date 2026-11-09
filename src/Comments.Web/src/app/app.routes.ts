import { Routes } from '@angular/router';
import { CommentsPage } from './comments/comments-page';

export const routes: Routes = [
  { path: '', component: CommentsPage, title: 'Comments' },
  { path: 'thread/:id', loadComponent: () => import('./comments/thread-page').then((m) => m.ThreadPage), title: 'Thread · Comments' },
  { path: 'search', loadComponent: () => import('./search/search-page').then((m) => m.SearchPage), title: 'Search · Comments' },
  { path: '**', redirectTo: '' },
];
