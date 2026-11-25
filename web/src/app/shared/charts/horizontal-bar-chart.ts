import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MeasureFamily } from '../measures/measures';
import { horizontalBarChartSpec } from './chart-specs';
import { ChartBase, ChartFrame, guardedBuild } from './chart-frame';
import { ChartUnit, CrBarItem, DEFAULT_THRESHOLDS, Thresholds } from './chart.models';

/**
 * Horizontal bars coloured by threshold (W4 limit utilisation, W7 CS01). Thresholds are in the
 * unit of the values (ratios for percent); null disables the colour code.
 */
@Component({
  selector: 'app-horizontal-bar-chart',
  imports: [ChartFrame],
  template: `<app-chart-frame [build]="build()" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HorizontalBarChart extends ChartBase {
  readonly items = input.required<readonly CrBarItem[]>();
  readonly family = input.required<MeasureFamily>();
  readonly seriesName = input.required<string>();
  readonly unit = input<ChartUnit>('percent');
  readonly thresholds = input<Thresholds | null>(DEFAULT_THRESHOLDS);

  protected override readonly build = computed(() =>
    guardedBuild(this.fullTitle(), () =>
      horizontalBarChartSpec({
        ...this.common(),
        items: this.items(),
        family: this.family(),
        seriesName: this.seriesName(),
        unit: this.unit(),
        thresholds: this.thresholds(),
      }),
    ),
  );
}
