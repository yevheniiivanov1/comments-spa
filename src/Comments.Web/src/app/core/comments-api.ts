import { HttpClient, HttpEvent, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Captcha,
  CommentDto,
  CommentPage,
  CommentSort,
  NewComment,
  PreviewResult,
  SortDirection,
  ThreadPage,
} from './models';

/** REST client for the comments API. */
@Injectable({ providedIn: 'root' })
export class CommentsApi {
  private readonly http = inject(HttpClient);

  getPage(page: number, sort: CommentSort, direction: SortDirection): Observable<CommentPage> {
    const params = new HttpParams().set('page', page).set('sort', sort).set('direction', direction);
    return this.http.get<CommentPage>('/api/comments', { params });
  }

  getThread(rootId: number, after = 0): Observable<ThreadPage> {
    return this.http.get<ThreadPage>(`/api/comments/${rootId}/thread`, {
      params: new HttpParams().set('after', after),
    });
  }

  getComment(id: number): Observable<CommentDto> {
    return this.http.get<CommentDto>(`/api/comments/${id}`);
  }

  getCaptcha(): Observable<Captcha> {
    return this.http.get<Captcha>('/api/captcha');
  }

  preview(text: string): Observable<PreviewResult> {
    return this.http.post<PreviewResult>('/api/comments/preview', { text });
  }

  /** Multipart post with upload progress events. */
  post(comment: NewComment): Observable<HttpEvent<CommentDto>> {
    const form = new FormData();
    form.set('userName', comment.userName);
    form.set('email', comment.email);
    form.set('homePage', comment.homePage);
    form.set('text', comment.text);
    form.set('captchaId', comment.captchaId);
    form.set('captchaAnswer', comment.captchaAnswer);
    if (comment.parentId !== null) {
      form.set('parentId', String(comment.parentId));
    }
    if (comment.file) {
      form.set('file', comment.file, comment.file.name);
    }
    return this.http.post<CommentDto>('/api/comments', form, {
      observe: 'events',
      reportProgress: true,
    });
  }

  /** Text attachments are read as text by the browser, which honours the charset the server declared. */
  getText(url: string): Observable<string> {
    return this.http.get(url, { responseType: 'text' });
  }
}
