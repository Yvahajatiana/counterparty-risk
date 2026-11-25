import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { lineChartSpec } from './chart-specs';
import { ChartBase, ChartFrame, guardedBuild } from './chart-frame';
import { ChartUnit, CrEventMarker, CrLimitLine, CrSeries, XAxisKind } from './chart.models';

/**
 * Multi-series lines (W3 EE/PFE/ENE by horizon, W4, W7, W8, W10, W12). Single Y axis: series of
 * different units are only accepted with rebase100.
 */
@Component({
  selector: 'app-line-chart',
  imports: [ChartFrame],
  template: `<app-chart-frame [build]="build()" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LineChart extends ChartBase {
  readonly series = input.required<readonly CrSeries[]>();
  readonly unit = input<ChartUnit>('money');
  readonly xKind = input<XAxisKind>('category');
  readonly xTitle = input('');
  readonly rebase100 = input(false);
  readonly limits = input<readonly CrLimitLine[]>([]);
  readonly events = input<readonly CrEventMarker[]>([]);

  protected override readonly build = computed(() =>
    guardedBuild(this.fullTitle(), () =>
      lineChartSpec({
        ...this.common(),
        series: this.series(),
        unit: this.unit(),
        xKind: this.xKind(),
        xTitle: this.xTitle(),
        rebase100: this.rebase100(),
        limits: this.limits(),
        events: this.events(),
      }),
    ),
  );
}
