import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, debounceTime, filter, switchMap } from 'rxjs';
import { CommentsApi } from '../core/comments-api';
import { MAX_TEXT_LENGTH } from '../core/markup';
import { Captcha, CommentDto, PreviewResult, ValidationProblem } from '../core/models';
import { checkFile, email, httpUrl, isImageFile, latinLettersAndDigits, markup } from '../core/validators';
import { FileSizePipe } from '../shared/file-size';
import { Icon } from '../shared/icons';
import { Composer } from './composer';

type Field = 'userName' | 'email' | 'homePage' | 'text' | 'captcha' | 'file' | 'parentId';
type Tag = 'i' | 'strong' | 'code' | 'a';

const AUTHOR_KEY = 'comments.author';

/**
 * The comment form in a modal dialog: author fields, text with a tag toolbar and live markup check,
 * server-side preview, an optional attachment and the CAPTCHA. Fields are validated in the browser as you type
 * and again by the server; server errors are shown under the field they belong to.
 */
@Component({
  selector: 'app-comment-form',
  imports: [ReactiveFormsModule, Icon, FileSizePipe],
  templateUrl: './comment-form.html',
  styleUrl: './comment-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentForm {
  protected readonly composer = inject(Composer);
  private readonly api = inject(CommentsApi);
  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly textarea = viewChild<ElementRef<HTMLTextAreaElement>>('textarea');

  protected readonly maxTextLength = MAX_TEXT_LENGTH;
  protected readonly form = new FormGroup({
    userName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(50), latinLettersAndDigits] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(254), email] }),
    homePage: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2048), httpUrl] }),
    text: new FormControl('', { nonNullable: true, validators: [markup] }),
    captcha: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9]{5}$/)] }),
  });

  protected readonly parent = computed(() => this.composer.target()?.parent ?? null);
  protected readonly captcha = signal<Captcha | null>(null);
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly filePreview = signal<string | null>(null);
  protected readonly preview = signal<PreviewResult | null>(null);
  protected readonly previewOpen = signal(false);
  protected readonly progress = signal<number | null>(null);
  protected readonly serverErrors = signal<Partial<Record<Field, string[]>>>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly submitted = signal(false);
  protected readonly textLength = signal(0);

  private posting: Subscription | null = null;

  constructor() {
    this.restoreAuthor();

    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.composer.target()) {
        if (!dialog.open) {
          this.reset();
          dialog.showModal();
          this.loadCaptcha();
          setTimeout(() => this.focusFirstEmpty());
        }
      } else if (dialog.open) {
        dialog.close();
      }
    });

    for (const [name, control] of Object.entries(this.form.controls)) {
      control.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.clearServerError(name as Field));
    }

    this.form.controls.text.valueChanges
      .pipe(
        takeUntilDestroyed(),
        debounceTime(350),
        filter(() => this.previewOpen()),
        switchMap((text) => this.api.preview(text)),
      )
      .subscribe((result) => this.preview.set(result));

    this.form.controls.text.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((text) => this.textLength.set(text.length));

    this.destroyRef.onDestroy(() => this.revokeFilePreview());
  }

  protected errorsOf(field: Exclude<Field, 'file' | 'parentId'>): string[] {
    const control = this.form.controls[field];
    const server = this.serverErrors()[field] ?? [];
    if (!(control.touched || this.submitted()) || !control.errors) {
      return server;
    }
    const e = control.errors;
    const client: string[] = [];
    if (e['required']) client.push(this.requiredMessage(field));
    if (e['maxlength']) client.push(`At most ${e['maxlength'].requiredLength} characters.`);
    if (e['latin']) client.push('Only Latin letters and digits.');
    if (e['email']) client.push('Enter an e-mail like name@example.com.');
    if (e['url']) client.push('Enter a full address starting with http:// or https://.');
    if (e['pattern']) client.push('Enter the 5 letters and digits from the picture.');
    if (e['markup']) client.push(...(e['markup'] as string[]));
    return [...client, ...server.filter((s) => !client.includes(s))];
  }

  protected wrap(tag: Tag): void {
    const element = this.textarea()?.nativeElement;
    if (!element) {
      return;
    }
    const { selectionStart: start, selectionEnd: end, value } = element;
    const selected = value.slice(start, end);
    const open = tag === 'a' ? '<a href="https://" title="">' : `<${tag}>`;
    const close = `</${tag}>`;
    this.form.controls.text.setValue(value.slice(0, start) + open + selected + close + value.slice(end));
    this.form.controls.text.markAsTouched();

    element.focus();
    if (tag === 'a') {
      const hrefStart = start + '<a href="'.length;
      element.setSelectionRange(hrefStart, hrefStart + 'https://'.length);
    } else {
      element.setSelectionRange(start + open.length, start + open.length + selected.length);
    }
  }

  protected onTextKeydown(event: KeyboardEvent): void {
    if (!(event.ctrlKey || event.metaKey)) {
      return;
    }
    const tag: Tag | null = event.key === 'i' ? 'i' : event.key === 'b' ? 'strong' : event.key === 'k' ? 'a' : null;
    if (tag) {
      event.preventDefault();
      this.wrap(tag);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      this.submit();
    }
  }

  protected togglePreview(): void {
    this.previewOpen.update((open) => !open);
    if (this.previewOpen()) {
      this.preview.set(null);
      this.api.preview(this.form.controls.text.value).subscribe((result) => this.preview.set(result));
    }
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    this.setFile(file);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.setFile(event.dataTransfer?.files?.[0] ?? null);
  }

  protected removeFile(): void {
    this.setFile(null);
  }

  protected loadCaptcha(): void {
    this.form.controls.captcha.reset('');
    this.api.getCaptcha().subscribe({
      next: (captcha) => this.captcha.set(captcha),
      error: (error: HttpErrorResponse) => this.formError.set(this.describe(error)),
    });
  }

  protected submit(): void {
    this.submitted.set(true);
    this.form.markAllAsTouched();
    this.formError.set(null);

    if (this.form.invalid || this.fileError() || !this.captcha() || this.posting) {
      this.focusFirstInvalid();
      return;
    }

    const value = this.form.getRawValue();
    this.saveAuthor();
    this.progress.set(0);
    this.posting = this.api
      .post({
        userName: value.userName,
        email: value.email.trim(),
        homePage: value.homePage.trim(),
        text: value.text,
        parentId: this.parent()?.id ?? null,
        captchaId: this.captcha()!.id,
        captchaAnswer: value.captcha,
        file: this.file(),
      })
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress && event.total) {
            this.progress.set(Math.round((100 * event.loaded) / event.total));
          } else if (event.type === HttpEventType.Response && event.body) {
            this.finish(event.body);
          }
        },
        error: (error: HttpErrorResponse) => this.fail(error),
      });
  }

  protected cancel(): void {
    this.posting?.unsubscribe();
    this.posting = null;
    this.composer.close();
  }

  private finish(comment: CommentDto): void {
    this.posting = null;
    this.progress.set(null);
    this.composer.posted.next(comment);
    this.composer.close();
  }

  private fail(error: HttpErrorResponse): void {
    this.posting = null;
    this.progress.set(null);
    const problem = error.error as ValidationProblem | null;
    if (error.status === 400 && problem?.errors) {
      const errors: Partial<Record<Field, string[]>> = {};
      for (const [key, messages] of Object.entries(problem.errors)) {
        errors[key as Field] = messages;
      }
      this.serverErrors.set(errors);
      if (errors.file) {
        this.fileError.set(errors.file[0]);
      }
      if (errors.parentId) {
        this.formError.set(errors.parentId[0]);
      }
    } else {
      this.formError.set(this.describe(error));
    }
    this.loadCaptcha();
    setTimeout(() => this.focusFirstInvalid());
  }

  private describe(error: HttpErrorResponse): string {
    if (error.status === 429) {
      return 'Too many requests from your address. Please wait a minute and try again.';
    }
    if (error.status === 413) {
      return 'The file is too large.';
    }
    if (error.status === 0) {
      return 'The server cannot be reached. Check your connection and try again.';
    }
    return 'Something went wrong on the server. Please try again.';
  }

  private setFile(file: File | null): void {
    this.revokeFilePreview();
    this.clearServerError('file');
    this.file.set(file);
    this.fileError.set(file ? checkFile(file) : null);
    if (file && !this.fileError() && isImageFile(file)) {
      this.filePreview.set(URL.createObjectURL(file));
    }
  }

  private revokeFilePreview(): void {
    const url = this.filePreview();
    if (url) {
      URL.revokeObjectURL(url);
      this.filePreview.set(null);
    }
  }

  private clearServerError(field: Field): void {
    if (this.serverErrors()[field]) {
      this.serverErrors.update((errors) => ({ ...errors, [field]: undefined }));
    }
  }

  private reset(): void {
    this.form.controls.text.reset('');
    this.form.controls.captcha.reset('');
    this.form.markAsUntouched();
    this.setFile(null);
    this.preview.set(null);
    this.previewOpen.set(false);
    this.serverErrors.set({});
    this.formError.set(null);
    this.submitted.set(false);
    this.progress.set(null);
  }

  private requiredMessage(field: Field): string {
    switch (field) {
      case 'userName':
        return 'User Name is required.';
      case 'email':
        return 'E-mail is required.';
      case 'captcha':
        return 'Enter the characters from the picture.';
      default:
        return 'Required.';
    }
  }

  private focusFirstEmpty(): void {
    const order: (keyof typeof this.form.controls)[] = ['userName', 'email', 'text'];
    const field = order.find((name) => !this.form.controls[name].value) ?? 'text';
    this.dialog().nativeElement.querySelector<HTMLElement>(`[formControlName="${field}"]`)?.focus();
  }

  private focusFirstInvalid(): void {
    this.dialog().nativeElement.querySelector<HTMLElement>('.invalid input, .invalid textarea')?.focus();
  }

  private saveAuthor(): void {
    const { userName, email, homePage } = this.form.getRawValue();
    try {
      localStorage.setItem(AUTHOR_KEY, JSON.stringify({ userName, email, homePage }));
    } catch {
      // Storage can be unavailable (private mode); remembering the author is only a convenience.
    }
  }

  private restoreAuthor(): void {
    try {
      const saved = JSON.parse(localStorage.getItem(AUTHOR_KEY) ?? 'null') as Record<string, string> | null;
      if (saved) {
        this.form.patchValue({ userName: saved['userName'] ?? '', email: saved['email'] ?? '', homePage: saved['homePage'] ?? '' });
      }
    } catch {
      // See saveAuthor.
    }
  }
}
