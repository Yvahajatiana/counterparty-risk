import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  formatDelta,
  formatInteger,
  formatMillions,
  formatPercent,
  FormattedDelta,
} from '../../format/amount';
import { ThresholdStatus } from '../../measures/thresholds';

export type KpiUnit = 'money' | 'count' | 'percent' | 'index';

interface Change extends FormattedDelta {
  readonly period: string;
  /** Whether the move is adverse for the risk reading (e.g. exposure up). */
  readonly assessment: 'adverse' | 'favourable' | 'neutral';
}

/**
 * KPI tile (W1 summary, W10 HHI): value with its level, variations D-1 / M-1, status and link
 * to the detail widget. Variations of amounts are relative (%), of counts and indices absolute.
 */
@Component({
  selector: 'app-kpi-tile',
  imports: [RouterLink],
  template: `
    <a class="cr-tile" [routerLink]="route()" [attr.data-status]="status()">
      <span class="cr-tile-label">{{ label() }}</span>
      <span class="cr-tile-value cr-numeric">{{ text() }}</span>
      <span class="cr-tile-level">{{ levelText() }}</span>
      @if (breakdown()) {
        <span class="cr-tile-breakdown cr-numeric">{{ breakdown() }}</span>
      }
      <span class="cr-tile-changes">
        @for (change of changes(); track change.period) {
          <span
            class="cr-numeric"
            [attr.data-direction]="change.direction"
            [attr.data-assessment]="change.assessment"
          >
            {{ change.period }} {{ change.text }}
            @if (change.assessment !== 'neutral') {
              <span class="cr-visually-hidden">({{ change.assessment }})</span>
            }
          </span>
        }
      </span>
    </a>
  `,
  styleUrl: './kpi-tile.scss',
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KpiTileComponent {
  readonly label = input.required<string>();
  readonly value = input.required<number>();
  readonly unit = input.required<KpiUnit>();
  /** Exposure or measure level, always displayed with the amount. */
  readonly level = input.required<string>();
  readonly vision = input<string | null>(null);
  readonly currency = input('EUR');
  readonly route = input.required<string>();
  readonly breakdown = input<string | null>(null);
  readonly status = input<ThresholdStatus | null>(null);
  readonly previousDay = input<number | null>(null);
  readonly previousMonth = input<number | null>(null);
  /** Reading direction: for exposures a higher value is adverse. */
  readonly higherIsWorse = input(true);

  protected readonly text = computed(() => {
    const value = this.value();
    switch (this.unit()) {
      case 'money':
        return `${formatMillions(value)} ${this.currency()}`;
      case 'percent':
        return formatPercent(value);
      default:
        return formatInteger(value);
    }
  });

  protected readonly levelText = computed(() =>
    this.vision() ? `${this.vision()} · ${this.level()}` : this.level(),
  );

  protected readonly changes = computed<Change[]>(() => {
    const value = this.value();
    const unit = this.unit();
    const result: Change[] = [];
    for (const [period, previous] of [
      ['D-1', this.previousDay()],
      ['M-1', this.previousMonth()],
    ] as const) {
      if (previous == null) {
        continue;
      }
      const delta =
        unit === 'money'
          ? formatDelta(previous === 0 ? 0 : (value - previous) / previous, formatPercent)
          : formatDelta(value - previous, unit === 'percent' ? formatPercent : formatInteger);
      if (!delta) {
        continue;
      }
      const adverse = this.higherIsWorse() ? delta.direction === 'up' : delta.direction === 'down';
      const assessment =
        delta.direction === 'flat' ? 'neutral' : adverse ? 'adverse' : 'favourable';
      result.push({ ...delta, period, assessment });
    }
    return result;
  });
}
