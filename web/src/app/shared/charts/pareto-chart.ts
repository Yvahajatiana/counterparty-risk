import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MeasureFamily } from '../measures/measures';
import { paretoChartSpec } from './chart-specs';
import { ChartBase, ChartFrame, guardedBuild } from './chart-frame';
import { CrBarItem } from './chart.models';

/**
 * Pareto of the top N (W10 concentration): share of each item in the total of all items and
 * cumulative share, on a single percentage axis.
 */
@Component({
  selector: 'app-pareto-chart',
  imports: [ChartFrame],
  template: `<app-chart-frame [build]="build()" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParetoChart extends ChartBase {
  readonly items = input.required<readonly CrBarItem[]>();
  readonly family = input.required<MeasureFamily>();
  readonly seriesName = input.required<string>();
  readonly top = input(10);

  protected override readonly build = computed(() =>
    guardedBuild(this.fullTitle(), () =>
      paretoChartSpec({
        ...this.common(),
        items: this.items(),
        family: this.family(),
        seriesName: this.seriesName(),
        top: this.top(),
      }),
    ),
  );
}
