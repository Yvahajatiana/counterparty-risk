import { ChangeDetectionStrategy, Component, computed, linkedSignal, signal } from '@angular/core';
import { FutureExposureResponse } from '../../core/api/dashboard.models';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import type { ColDef, GetRowIdFunc } from 'ag-grid-community';
import type { Options as ChartOptions } from 'highcharts';
import { futureExposureResource } from '../../core/api/dashboard-api';
import { PfePeakRow } from '../../core/api/dashboard.models';
import { widgetNav } from '../../layout/navigation';
import { Chart } from '../../shared/charts/chart';
import { toMillions, millionsCell, formatPercent, MillionsPipe } from '../../shared/format/amount';
import { DataGrid } from '../../shared/grid/data-grid';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

/**
 * Future exposure, economic view only (EE, PFE, ENE, EPE). The x axis is the projection horizon, not the
 * calendar date: the risk history lives in the History widget. A single PFE quantile for the whole dashboard.
 */
@Component({
  selector: 'app-future-exposure',
  imports: [
    Chart,
    DataGrid,
    LoadState,
    MatFormFieldModule,
    MatSelectModule,
    MillionsPipe,
    WidgetHeader,
  ],
  templateUrl: './future-exposure.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FutureExposure {
  protected readonly widget = widgetNav('future-exposure');
  protected readonly counterpartyId = signal<string | null>(null);
  protected readonly data = futureExposureResource(this.counterpartyId);
  /** Keeps the previous counterparty on screen while the next one loads. */
  protected readonly view = linkedSignal<
    FutureExposureResponse | undefined,
    FutureExposureResponse | undefined
  >({
    source: () => this.data.value(),
    computation: (value, previous) => value ?? previous?.value,
  });

  protected readonly quantile = computed(() => formatPercent(this.view()?.pfeQuantile));
  protected readonly selectedName = computed(() => {
    const d = this.view();
    return d?.counterparties.find((c) => c.id === d.counterpartyId)?.name ?? '';
  });

  protected readonly profileChart = computed<ChartOptions | null>(() => {
    const d = this.view();
    if (!d) {
      return null;
    }
    return {
      chart: { type: 'line' },
      xAxis: { categories: d.profile.map((p) => p.horizon), title: { text: 'Projection horizon' } },
      yAxis: { title: { text: 'EUR m' } },
      tooltip: { shared: true, valueSuffix: ' m EUR', valueDecimals: 1 },
      series: [
        { type: 'line', name: 'EE', data: d.profile.map((p) => toMillions(p.ee)) },
        {
          type: 'line',
          name: `PFE ${this.quantile()}`,
          data: d.profile.map((p) => toMillions(p.pfe)),
          dashStyle: 'Dash',
        },
        {
          type: 'line',
          name: 'ENE',
          data: d.profile.map((p) => toMillions(p.ene)),
          dashStyle: 'Dot',
        },
      ],
    };
  });

  protected readonly columns = computed<ColDef<PfePeakRow>[]>(() => [
    { field: 'counterparty', headerName: 'Counterparty', minWidth: 190 },
    { field: 'businessLine', headerName: 'Business line' },
    { field: 'peakPfe', headerName: `Peak PFE ${this.quantile()}`, sort: 'desc', ...millionsCell },
    { field: 'peakHorizon', headerName: 'Peak horizon', maxWidth: 130 },
    { field: 'epe', headerName: 'EPE (1Y)', ...millionsCell },
    { field: 'effectiveEpe', headerName: 'Effective EPE (1Y)', ...millionsCell },
  ]);

  protected readonly rowId: GetRowIdFunc<PfePeakRow> = (p) => p.data.counterpartyId;

  protected select(id: string): void {
    this.counterpartyId.set(id);
  }
}
