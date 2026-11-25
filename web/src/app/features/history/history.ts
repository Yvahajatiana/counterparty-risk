import { ChangeDetectionStrategy, Component, computed, linkedSignal, signal } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import type { Options as ChartOptions } from 'highcharts';
import { historyResource } from '../../core/api/dashboard-api';
import {
  HistoryFrequency,
  HistoryIndicator,
  HistoryResponse,
} from '../../core/api/dashboard.models';
import { widgetNav } from '../../layout/navigation';
import { Chart } from '../../shared/charts/chart';
import { toMillions, formatMillions, formatPercent } from '../../shared/format/amount';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

const INDICATORS: readonly { readonly value: HistoryIndicator; readonly label: string }[] = [
  { value: 'Ead', label: 'Total EAD (regulatory)' },
  { value: 'Rwa', label: 'RWA CCR + CVA (regulatory)' },
  { value: 'PeakPfe', label: 'Peak PFE (economic)' },
  { value: 'Cva', label: 'Accounting CVA' },
  { value: 'ReplacementCost', label: 'Replacement cost (after collateral)' },
];

/**
 * History: one indicator at a time: X = as-of date, Y = the indicator. History is recalculated at
 * constant method; method changes stay annotated on the curve.
 */
@Component({
  selector: 'app-history',
  imports: [
    Chart,
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

  protected readonly chart = computed<ChartOptions | null>(() => {
    const d = this.view();
    if (!d) {
      return null;
    }
    return {
      chart: { type: 'line', zooming: { type: 'x' } },
      xAxis: {
        type: 'datetime',
        title: { text: 'As-of date' },
        plotLines: d.events.map((e) => ({
          value: Date.parse(e.date),
          className: 'cr-plotline-event',
          label: { text: e.label },
        })),
      },
      yAxis: { title: { text: `${d.level} (EUR m)` } },
      legend: { enabled: false },
      tooltip: { valueSuffix: ' m EUR', valueDecimals: 1, xDateFormat: '%d/%m/%Y' },
      series: [
        {
          type: 'line',
          name: d.label,
          data: d.points.map((p) => [Date.parse(p.date), toMillions(p.value)]),
          marker: { enabled: false },
        },
      ],
    };
  });

  protected selectIndicator(value: HistoryIndicator): void {
    this.indicator.set(value);
  }

  protected selectFrequency(value: HistoryFrequency): void {
    this.frequency.set(value);
  }
}
