import { ChangeDetectionStrategy, Component, computed, linkedSignal, signal } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { historyResource } from '../../core/api/dashboard-api';
import {
  HistoryFrequency,
  HistoryIndicator,
  HistoryResponse,
} from '../../core/api/dashboard.models';
import { widgetNav } from '../../layout/navigation';
import { CrEventMarker, CrSeries } from '../../shared/charts/chart.models';
import { LineChart } from '../../shared/charts/line-chart';
import { formatMillions, formatPercent } from '../../shared/format/amount';
import { ExposureLevel, MeasureFamily } from '../../shared/measures/measures';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

interface IndicatorDefinition {
  readonly value: HistoryIndicator;
  readonly label: string;
  readonly family: MeasureFamily;
  readonly level: ExposureLevel;
}

const INDICATORS: readonly IndicatorDefinition[] = [
  { value: 'Ead', label: 'Total EAD (regulatory)', family: 'Regulatory', level: 'Ead' },
  {
    value: 'Rwa',
    label: 'RWA CCR + CVA (regulatory)',
    family: 'Regulatory',
    level: 'RiskWeighted',
  },
  { value: 'PeakPfe', label: 'Peak PFE (economic)', family: 'Economic', level: 'AfterCollateral' },
  { value: 'Cva', label: 'Accounting CVA', family: 'Accounting', level: 'ValuationAdjustment' },
  {
    value: 'ReplacementCost',
    label: 'Replacement cost (after collateral)',
    family: 'Regulatory',
    level: 'AfterCollateral',
  },
];

/**
 * History: one indicator at a time: X = as-of date, Y = the indicator. History is recalculated at
 * constant method; method changes stay annotated on the curve.
 */
@Component({
  selector: 'app-history',
  imports: [
    LineChart,
    LoadState,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatSelectModule,
    WidgetHeader,
  ],
  templateUrl: './history.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class History {
  protected readonly widget = widgetNav('history');
  protected readonly indicators = INDICATORS;
  protected readonly indicator = signal<HistoryIndicator>('Ead');
  protected readonly frequency = signal<HistoryFrequency>('Daily');
  protected readonly data = historyResource(this.indicator, this.frequency);
  protected readonly view = linkedSignal<HistoryResponse | undefined, HistoryResponse | undefined>({
    source: () => this.data.value(),
    computation: (value, previous) => value ?? previous?.value,
  });

  protected readonly variation = computed(() => {
    const points = this.view()?.points;
    if (!points || points.length < 2) {
      return null;
    }
    const first = points[0]!.value;
    const last = points[points.length - 1]!.value;
    return {
      amount: formatMillions(last - first),
      percent: formatPercent(first === 0 ? 0 : (last - first) / first),
    };
  });

  /**
   * Family and level of the indicator returned by the API. An unknown indicator has none: the
   * chart is not drawn rather than shown with a guessed exposure level.
   */
  protected readonly definition = computed(() => {
    const indicator = this.view()?.indicator;
    return INDICATORS.find((i) => i.value === indicator) ?? null;
  });

  protected readonly series = computed<CrSeries[]>(() => {
    const d = this.view();
    const definition = this.definition();
    if (!d || !definition) {
      return [];
    }
    return [
      {
        id: d.indicator,
        name: d.label,
        family: definition.family,
        points: d.points.map((p) => [p.date, p.value] as const),
      },
    ];
  });

  protected readonly events = computed<CrEventMarker[]>(
    () => this.view()?.events.map((e) => ({ at: e.date, label: e.label })) ?? [],
  );
  protected selectIndicator(value: HistoryIndicator): void {
    this.indicator.set(value);
  }

  protected selectFrequency(value: HistoryFrequency): void {
    this.frequency.set(value);
  }
}
