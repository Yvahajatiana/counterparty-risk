import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DataQuality } from '../../core/api/dashboard.models';
import { WidgetNavItem } from '../../layout/navigation';
import { AsOfDatePipe } from '../format/amount';

/**
 * Widget title, business question and data quality banner (as-of date, source, status,
 * completeness), shown on every widget as required by BCBS 239.
 */
@Component({
  selector: 'app-widget-header',
  imports: [AsOfDatePipe],
  template: `
    <header class="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 class="cr-page-title">{{ widget().title }}</h1>
        <p class="cr-text-muted">{{ widget().question }}</p>
      </div>
      @if (quality(); as q) {
        <dl class="cr-quality" aria-label="Data quality">
          <div>
            <dt>As of</dt>
            <dd class="cr-numeric">{{ q.asOfDate | asOf }}</dd>
          </div>
          <div>
            <dt>Currency</dt>
            <dd>{{ q.reportingCurrency }}</dd>
          </div>
          <div>
            <dt>Status</dt>
            <dd [class.cr-badge-synthetic]="q.status === 'Synthetic'">{{ q.status }}</dd>
          </div>
          <div>
            <dt>Completeness</dt>
            <dd>{{ q.completeness }}</dd>
          </div>
        </dl>
      }
    </header>
  `,
  styles: `
    .cr-quality {
      display: flex;
      gap: 1rem;
      margin: 0;
      padding: 0.375rem 0.75rem;
      border: 1px solid var(--cr-color-border);
      border-radius: 6px;
      background: var(--cr-color-surface-alt);
      font-size: 0.8125rem;
    }
    dt {
      color: var(--cr-color-text-muted);
    }
    dd {
      margin: 0;
      font-weight: 600;
    }
    .cr-badge-synthetic {
      padding: 0 0.375rem;
      border: 1px solid var(--cr-status-warn);
      border-radius: 4px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WidgetHeader {
  readonly widget = input.required<WidgetNavItem>();
  readonly quality = input<DataQuality | undefined>();
}
