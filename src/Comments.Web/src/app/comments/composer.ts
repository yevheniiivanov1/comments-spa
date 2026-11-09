import { Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { CommentDto } from '../core/models';

export interface ComposerTarget {
  parent: CommentDto | null;
}

/** Opens the comment form for a new top-level comment or a reply and announces what was posted. */
@Injectable({ providedIn: 'root' })
export class Composer {
  readonly target = signal<ComposerTarget | null>(null);
  readonly posted = new Subject<CommentDto>();

  open(parent: CommentDto | null = null): void {
    this.target.set({ parent });
  }

  close(): void {
    this.target.set(null);
  }
}
