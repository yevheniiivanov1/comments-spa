import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { isHttpUrl, validateMarkup } from './markup';

/** The same e-mail shape the server accepts. */
const EMAIL =
  /^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\.[A-Za-z]{2,}$/;

export const IMAGE_EXTENSIONS = ['jpg', 'jpeg', 'png', 'gif'];
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
export const MAX_TEXT_FILE_BYTES = 100 * 1024;

export const latinLettersAndDigits: ValidatorFn = (control) =>
  !control.value || /^[A-Za-z0-9]+$/.test(control.value) ? null : { latin: true };

export const email: ValidatorFn = (control) =>
  !control.value || EMAIL.test(control.value.trim()) ? null : { email: true };

export const httpUrl: ValidatorFn = (control) =>
  !control.value || isHttpUrl(control.value) ? null : { url: true };

export const markup: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const errors = validateMarkup(control.value ?? '');
  return errors.length === 0 ? null : { markup: errors };
};

/** Mirrors the server's checks by name and size; the server also checks the content. */
export function checkFile(file: File): string | null {
  const extension = file.name.split('.').pop()?.toLowerCase() ?? '';
  if (IMAGE_EXTENSIONS.includes(extension)) {
    return file.size > MAX_IMAGE_BYTES ? 'Image must be at most 5 MB.' : null;
  }
  if (extension === 'txt') {
    return file.size > MAX_TEXT_FILE_BYTES ? 'Text file must be at most 100 KB.' : null;
  }
  return 'Only JPG, GIF and PNG images or TXT files can be attached.';
}

export function isImageFile(file: File): boolean {
  return IMAGE_EXTENSIONS.includes(file.name.split('.').pop()?.toLowerCase() ?? '');
}
