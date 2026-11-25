import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { waterfallSpec } from './chart-specs';
import { ChartBase, ChartFrame, guardedBuild } from './chart-frame';
import { WaterfallStep } from './chart.models';

/** Waterfall (W2 gross to RC, W6 RWA variation, W7 CVA variation). Amounts only. */
@Component({
  selector: 'app-waterfall-chart',
  imports: [ChartFrame],
  template: `<app-chart-frame [build]="build()" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WaterfallChart extends ChartBase {
  readonly steps = input.required<readonly WaterfallStep[]>();

  protected override readonly build = computed(() =>
    guardedBuild(this.fullTitle(), () => waterfallSpec({ ...this.common(), steps: this.steps() })),
  );
}
