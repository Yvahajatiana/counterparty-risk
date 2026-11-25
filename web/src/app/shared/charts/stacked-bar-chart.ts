import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { stackedBarChartSpec } from './chart-specs';
import { ChartBase, ChartFrame, guardedBuild } from './chart-frame';
import { ChartUnit, CrEventMarker, CrSeries } from './chart.models';

/** Stacked bars, in value or 100 % (W5, W6, W9, W10, W12). */
@Component({
  selector: 'app-stacked-bar-chart',
  imports: [ChartFrame],
  template: `<app-chart-frame [build]="build()" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StackedBarChart extends ChartBase {
  readonly series = input.required<readonly CrSeries[]>();
  readonly stacking = input<'normal' | 'percent'>('normal');
  readonly orientation = input<'column' | 'bar'>('column');
  readonly unit = input<ChartUnit>('money');
  readonly xTitle = input('');
  readonly events = input<readonly CrEventMarker[]>([]);

  protected override readonly build = computed(() =>
    guardedBuild(this.fullTitle(), () =>
      stackedBarChartSpec({
        ...this.common(),
        series: this.series(),
        stacking: this.stacking(),
        orientation: this.orientation(),
        unit: this.unit(),
        xTitle: this.xTitle(),
        events: this.events(),
      }),
    ),
  );
}
