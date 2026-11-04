import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Landing page. Intentionally empty: the dashboard content is delivered by #13. */
@Component({
  selector: 'app-home',
  template: `
    <h1 class="cr-page-title">Counterparty Risk Dashboard</h1>
    <p class="cr-text-muted">Select a widget in the side navigation.</p>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Home {}
