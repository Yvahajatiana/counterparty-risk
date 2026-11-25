import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { limitsResource } from '../../core/api/dashboard-api';
import { LimitRow } from '../../core/api/dashboard.models';
import { widgetNav } from '../../layout/navigation';
import { CrBarItem, CrSeries, Thresholds } from '../../shared/charts/chart.models';
import { HorizontalBarChart } from '../../shared/charts/horizontal-bar-chart';
import { LineChart } from '../../shared/charts/line-chart';
import { PercentPipe } from '../../shared/format/amount';
import { DataGrid } from '../../shared/grid/data-grid';
import { CrColumn } from '../../shared/grid/grid-columns';
import { DEFAULT_THRESHOLDS, thresholdStatus } from '../../shared/measures/thresholds';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

/** Limits: counterparty limits on peak PFE, with the warning / breach colour code. */
@Component({
  selector: 'app-limits',
  imports: [DataGrid, HorizontalBarChart, LineChart, LoadState, PercentPipe, WidgetHeader],
  templateUrl: './limits.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Limits {
  protected readonly widget = widgetNav('limits');
  protected readonly data = limitsResource();

  protected readonly thresholds = computed<Thresholds>(() => {
    const t = this.data.value()?.thresholds;
    return t ? { warn: t.warning, breach: t.breach } : DEFAULT_THRESHOLDS;
  });

  protected readonly utilisation = computed<CrBarItem[]>(
    () =>
      this.data.value()?.rows.map((r) => ({ label: r.counterparty, value: r.utilisation })) ?? [],
  );

  protected readonly history = computed<CrSeries[]>(
    () =>
      this.data.value()?.history.map((s) => ({
        id: s.counterpartyId,
        name: s.counterparty,
        family: 'Economic',
        points: s.points.map((p) => [p.date, p.value] as const),
      })) ?? [],
  );

  protected readonly breaches = computed(
    () =>
      this.data.value()?.rows.filter((r) => r.utilisation >= this.thresholds().breach).length ?? 0,
  );

  protected readonly columns = computed<CrColumn<LimitRow>[]>(() => [
    { field: 'counterparty', header: 'Counterparty', type: 'text', minWidth: 190, pinned: true },
    { field: 'group', header: 'Group', type: 'text', minWidth: 160 },
    { field: 'desk', header: 'Desk', type: 'text' },
    { field: 'measure', header: 'Limit measure', type: 'text', maxWidth: 150 },
    { field: 'limit', header: 'Limit', type: 'money' },
    { field: 'exposure', header: 'Exposure (peak PFE)', type: 'money', minWidth: 160 },
    {
      field: 'utilisation',
      header: 'Utilisation',
      type: 'percent',
      sort: 'desc',
      minWidth: 150,
      thresholds: this.thresholds(),
    },
    { field: 'headroom', header: 'Headroom', type: 'money' },
    { field: 'breachAgeDays', header: 'Breach age (days)', type: 'integer' },
  ]);

  protected readonly rowStatus = computed(() => {
    const thresholds = this.thresholds();
    return (row: LimitRow) => thresholdStatus(row.utilisation, thresholds);
  });

  protected readonly rowId = (p: { data: LimitRow }) => p.data.counterpartyId;
}
