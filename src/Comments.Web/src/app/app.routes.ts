import { Routes } from '@angular/router';
import { CommentsPage } from './comments/comments-page';

export const routes: Routes = [
  { path: '', component: CommentsPage, title: 'Comments' },
  { path: '**', redirectTo: '' },
];
