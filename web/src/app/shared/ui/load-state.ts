import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Loading and error placeholder for a widget resource. Never displays the raw error. */
@Component({
  selector: 'app-load-state',
  template: `
    @if (error()) {
      <p role="alert" class="cr-text-muted">Data could not be loaded. Please retry later.</p>
    } @else if (loading()) {
      <p aria-live="polite" class="cr-text-muted">Loading...</p>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoadState {
  readonly loading = input(false);
  readonly error = input<unknown>(undefined);
}
