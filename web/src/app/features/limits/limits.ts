import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import type { ColDef, GetRowIdFunc, RowClassRules } from 'ag-grid-community';
import type { Options as ChartOptions } from 'highcharts';
import { limitsResource } from '../../core/api/dashboard-api';
import { LimitRow } from '../../core/api/dashboard.models';
import { widgetNav } from '../../layout/navigation';
import { Chart } from '../../shared/charts/chart';
import { millionsCell, formatPercent, PercentPipe } from '../../shared/format/amount';
import { DataGrid } from '../../shared/grid/data-grid';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

/** Limits: counterparty limits on peak PFE, with the warning / breach colour code. */
@Component({
  selector: 'app-limits',
  imports: [Chart, DataGrid, LoadState, PercentPipe, WidgetHeader],
  templateUrl: './limits.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Limits {
  protected readonly widget = widgetNav('limits');
  protected readonly data = limitsResource();

  private readonly status = computed(() => {
    const thresholds = this.data.value()?.thresholds ?? { warning: 0.8, breach: 1 };
    return (utilisation: number) =>
      utilisation >= thresholds.breach
        ? 'breach'
        : utilisation >= thresholds.warning
          ? 'warn'
          : 'ok';
  });

  protected readonly utilisationChart = computed<ChartOptions | null>(() => {
    const d = this.data.value();
    if (!d) {
      return null;
    }
    const status = this.status();
    return {
      chart: { type: 'bar' },
      xAxis: { categories: d.rows.map((r) => r.counterparty) },
      yAxis: {
        title: { text: 'Utilisation of the PFE limit (%)' },
        plotLines: [
          // Thresholds are named in the widget text: no label over the bars.
          { value: d.thresholds.warning * 100, className: 'cr-plotline-warn' },
          { value: d.thresholds.breach * 100, className: 'cr-plotline-breach' },
        ],
      },
      legend: { enabled: false },
      tooltip: { valueSuffix: ' %', valueDecimals: 1 },
      series: [
        {
          type: 'bar',
          name: 'Utilisation',
          data: d.rows.map((r) => ({
            y: Math.round(r.utilisation * 1000) / 10,
            className: `cr-point-${status(r.utilisation)}`,
          })),
        },
      ],
    };
  });

  protected readonly historyChart = computed<ChartOptions | null>(() => {
    const d = this.data.value();
    if (!d) {
      return null;
    }
    return {
      chart: { type: 'line' },
      xAxis: { type: 'datetime' },
      yAxis: {
        title: { text: 'Utilisation (%)' },
        plotLines: [
          {
            value: d.thresholds.breach * 100,
            className: 'cr-plotline-breach',
            label: { text: 'Limit' },
          },
        ],
      },
      tooltip: { shared: true, valueSuffix: ' %', valueDecimals: 1 },
      series: d.history.map((s) => ({
        type: 'line' as const,
        name: s.counterparty,
        data: s.points.map((p) => [Date.parse(p.date), Math.round(p.value * 1000) / 10]),
      })),
    };
  });

  protected readonly breaches = computed(
    () =>
      this.data
        .value()
        ?.rows.filter((r) => r.utilisation >= (this.data.value()?.thresholds.breach ?? 1)).length ??
      0,
  );

  protected readonly columns: ColDef<LimitRow>[] = [
    { field: 'counterparty', headerName: 'Counterparty', minWidth: 190, pinned: 'left' },
    { field: 'group', headerName: 'Group', minWidth: 160 },
    { field: 'desk', headerName: 'Desk' },
    { field: 'measure', headerName: 'Limit measure', maxWidth: 140 },
    { field: 'limit', headerName: 'Limit', ...millionsCell },
    { field: 'exposure', headerName: 'Exposure (peak PFE)', minWidth: 160, ...millionsCell },
    {
      field: 'utilisation',
      headerName: 'Utilisation',
      sort: 'desc',
      type: 'rightAligned',
      valueFormatter: (p) => formatPercent(p.value),
    },
    { field: 'headroom', headerName: 'Headroom', ...millionsCell },
    {
      field: 'breachAgeDays',
      headerName: 'Breach age (days)',
      type: 'rightAligned',
      valueFormatter: (p) => (p.value == null ? '' : String(p.value)),
    },
  ];

  protected readonly rowClassRules = computed<RowClassRules<LimitRow>>(() => {
    const status = this.status();
    return {
      'cr-row-breach': (p) => !!p.data && status(p.data.utilisation) === 'breach',
      'cr-row-warn': (p) => !!p.data && status(p.data.utilisation) === 'warn',
    };
  });

  protected readonly rowId: GetRowIdFunc<LimitRow> = (p) => p.data.counterpartyId;
}
