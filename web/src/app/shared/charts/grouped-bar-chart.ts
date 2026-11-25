import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { groupedBarChartSpec } from './chart-specs';
import { ChartBase, ChartFrame, guardedBuild } from './chart-frame';
import { ChartUnit, CrEventMarker, CrSeries } from './chart.models';

/** Side-by-side bars (W5 SA-CCR vs IMM, W9 received vs posted, W11 base vs stress, W8). */
@Component({
  selector: 'app-grouped-bar-chart',
  imports: [ChartFrame],
  template: `<app-chart-frame [build]="build()" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupedBarChart extends ChartBase {
  readonly series = input.required<readonly CrSeries[]>();
  readonly orientation = input<'column' | 'bar'>('column');
  readonly unit = input<ChartUnit>('money');
  readonly xTitle = input('');
  readonly events = input<readonly CrEventMarker[]>([]);

  protected override readonly build = computed(() =>
    guardedBuild(this.fullTitle(), () =>
      groupedBarChartSpec({
        ...this.common(),
        series: this.series(),
        orientation: this.orientation(),
        unit: this.unit(),
        xTitle: this.xTitle(),
        events: this.events(),
      }),
    ),
  );
}
