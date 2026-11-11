import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { Options } from 'highcharts';
import { HighchartsChartComponent } from 'highcharts-angular';

/**
 * Highcharts wrapper. Always in styledMode: colours come from the design tokens (_charts.scss),
 * and no inline style attribute is needed, which keeps the strict CSP (style-src nonce) intact.
 * Labels never use HTML (useHTML stays false).
 */
@Component({
  selector: 'app-chart',
  imports: [HighchartsChartComponent],
  template: `<highcharts-chart
    class="block w-full"
    [options]="merged()"
    [attr.aria-label]="label()"
  />`,
  styles: `
    :host {
      display: block;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Chart {
  readonly options = input.required<Options>();
  /** Accessible name of the chart. */
  readonly label = input.required<string>();
  readonly height = input(320);

  protected readonly merged = computed<Options>(() => {
    const options = this.options();
    return {
      ...options,
      chart: { styledMode: true, height: this.height(), ...options.chart },
      title: { text: undefined, ...options.title },
      credits: { enabled: false },
      tooltip: { ...options.tooltip, useHTML: false },
    };
  });
}
