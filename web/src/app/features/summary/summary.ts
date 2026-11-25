import { ChangeDetectionStrategy, Component } from '@angular/core';
import { summaryResource } from '../../core/api/dashboard-api';
import { widgetNav } from '../../layout/navigation';
import { KpiTileComponent } from '../../shared/ui/kpi-tile/kpi-tile';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

/** Summary: five KPI tiles at most, each one links to its detail widget. */
@Component({
  selector: 'app-summary',
  imports: [KpiTileComponent, LoadState, WidgetHeader],
  templateUrl: './summary.html',
  styleUrl: './summary.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Summary {
  protected readonly widget = widgetNav('summary');
  protected readonly summary = summaryResource();
}
