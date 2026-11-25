import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { HighchartsChartComponent } from 'highcharts-angular';
import { ChartSpec } from '../chart.models';
import { highchartsAdapter } from './highcharts-adapter';

/**
 * Highcharts implementation of the chart canvas (ChartAdapter, ADR-001 §6). styledMode keeps
 * colours in CSS (design tokens) and avoids inline style attributes under the strict CSP.
 */
@Component({
  selector: 'app-chart-canvas',
  imports: [HighchartsChartComponent],
  // The chart is recreated when the spec changes, so that removed series never linger.
  template: `
    @for (options of [options()]; track options) {
      <highcharts-chart class="block w-full" [options]="options" />
    }
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartCanvas {
  readonly spec = input.required<ChartSpec>();

  protected readonly options = computed(() => highchartsAdapter.toOptions(this.spec()));
}
