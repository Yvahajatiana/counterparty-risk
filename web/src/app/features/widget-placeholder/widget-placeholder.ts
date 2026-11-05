import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Empty page for a widget route until its feature is implemented (#13). */
@Component({
  selector: 'app-widget-placeholder',
  template: `
    <h1 class="cr-page-title">{{ code() }}</h1>
    <p class="cr-text-muted">Not implemented yet.</p>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WidgetPlaceholder {
  /** Bound from the route `data.code` (withComponentInputBinding). */
  readonly code = input<string>('');
}
