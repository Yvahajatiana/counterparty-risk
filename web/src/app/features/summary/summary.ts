import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { summaryResource } from '../../core/api/dashboard-api';
import { KpiTile } from '../../core/api/dashboard.models';
import { widgetNav } from '../../layout/navigation';
import { formatInteger, formatMillions, formatPercent } from '../../shared/format/amount';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

interface Change {
  readonly period: string;
  readonly text: string;
  readonly direction: 'up' | 'down' | 'flat';
}

/** Summary: five KPI tiles at most, each one links to its detail widget. */
@Component({
  selector: 'app-summary',
  imports: [LoadState, RouterLink, WidgetHeader],
  templateUrl: './summary.html',
  styleUrl: './summary.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Summary {
  protected readonly widget = widgetNav('summary');
  protected readonly summary = summaryResource();

  protected value(tile: KpiTile): string {
    return tile.unit === 'count' ? formatInteger(tile.value) : `${formatMillions(tile.value)} EUR`;
  }

  protected changes(tile: KpiTile): Change[] {
    const result: Change[] = [];
    for (const [period, previous] of [
      ['D-1', tile.previousDay],
      ['M-1', tile.previousMonth],
    ] as const) {
      if (previous == null) {
        continue;
      }
      const delta = tile.value - previous;
      const direction = delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat';
      const sign = delta > 0 ? '+' : '';
      const text =
        tile.unit === 'count'
          ? `${sign}${formatInteger(delta)}`
          : `${sign}${formatPercent(previous === 0 ? 0 : delta / previous)}`;
      result.push({ period, text, direction });
    }
    return result;
  }
}
