import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** A round avatar with the user's initials on a colour derived from the name. */
@Component({
  selector: 'app-avatar',
  template: `<span class="avatar" [style.--hue]="hue()" [attr.aria-hidden]="true">{{ initials() }}</span>`,
  styles: `
    .avatar {
      display: inline-grid;
      place-items: center;
      width: var(--avatar-size, 44px);
      height: var(--avatar-size, 44px);
      border-radius: 50%;
      font-weight: 700;
      font-size: calc(var(--avatar-size, 44px) * 0.38);
      letter-spacing: 0.02em;
      color: hsl(var(--hue) 45% 28%);
      background: hsl(var(--hue) 70% 88%);
      box-shadow: inset 0 0 0 3px #fff, 0 0 0 1px hsl(var(--hue) 40% 80%);
      user-select: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Avatar {
  readonly name = input.required<string>();

  protected readonly initials = computed(() => {
    const name = this.name();
    const capitals = name.replace(/[^A-Z]/g, '');
    return (capitals.length >= 2 ? capitals.slice(0, 2) : name.slice(0, 2)).toUpperCase();
  });

  protected readonly hue = computed(() => {
    let hash = 0;
    for (const char of this.name()) {
      hash = (hash * 31 + char.charCodeAt(0)) | 0;
    }
    return Math.abs(hash) % 360;
  });
}
