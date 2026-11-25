import { ChangeDetectionStrategy, Component, computed, linkedSignal, signal } from '@angular/core';
import { FutureExposureResponse } from '../../core/api/dashboard.models';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { futureExposureResource } from '../../core/api/dashboard-api';
import { ExposureProfilePoint, PfePeakRow } from '../../core/api/dashboard.models';
import { widgetNav } from '../../layout/navigation';
import { CrSeries } from '../../shared/charts/chart.models';
import { LineChart } from '../../shared/charts/line-chart';
import { formatPercent, MillionsPipe } from '../../shared/format/amount';
import { DataGrid } from '../../shared/grid/data-grid';
import { CrColumn } from '../../shared/grid/grid-columns';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

/**
 * Future exposure, economic view only (EE, PFE, ENE, EPE). The x axis is the projection horizon, not the
 * calendar date: the risk history lives in the History widget. A single PFE quantile for the whole dashboard.
 */
@Component({
  selector: 'app-future-exposure',
  imports: [
    DataGrid,
    LineChart,
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

  protected readonly profile = computed<CrSeries[]>(() => {
    const d = this.view();
    if (!d) {
      return [];
    }
    const profile = d.profile;
    const points = (pick: (p: ExposureProfilePoint) => number) =>
      profile.map((p) => [p.horizon, pick(p)] as const);
    return [
      { id: 'ee', name: 'EE', family: 'Economic', points: points((p) => p.ee) },
      {
        id: 'pfe',
        name: `PFE ${this.quantile()}`,
        family: 'Economic',
        points: points((p) => p.pfe),
        dashed: true,
      },
      { id: 'ene', name: 'ENE', family: 'Economic', points: points((p) => p.ene), dashed: true },
    ];
  });

  protected readonly columns = computed<CrColumn<PfePeakRow>[]>(() => [
    { field: 'counterparty', header: 'Counterparty', type: 'text', minWidth: 190, pinned: true },
    { field: 'businessLine', header: 'Business line', type: 'text' },
    { field: 'peakPfe', header: `Peak PFE ${this.quantile()}`, type: 'money', sort: 'desc' },
    { field: 'peakHorizon', header: 'Peak horizon', type: 'text', maxWidth: 140 },
    { field: 'epe', header: 'EPE (1Y)', type: 'money' },
    { field: 'effectiveEpe', header: 'Effective EPE (1Y)', type: 'money' },
  ]);

  protected readonly rowId = (p: { data: PfePeakRow }) => p.data.counterpartyId;
  protected select(id: string): void {
    this.counterpartyId.set(id);
  }
}
