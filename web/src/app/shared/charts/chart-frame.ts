import {
  ChangeDetectionStrategy,
  Component,
  computed,
  Directive,
  input,
  isDevMode,
  Signal,
} from '@angular/core';
import { DisplayLevel, titleWithLevel } from '../measures/measures';
import { ChartRuleError } from './chart-rules';
import { CommonChartInput } from './chart-specs';
import { ChartSpec } from './chart.models';
import { ChartCanvas } from './highcharts/chart-canvas';

export type ChartBuild =
  | { readonly title: string; readonly spec: ChartSpec; readonly error?: undefined }
  | { readonly title: string; readonly spec?: undefined; readonly error: string };

/**
 * Runs a spec builder. A domain rule violation never draws the chart: in development the
 * explicit reason is logged and displayed, in production a generic message is shown.
 */
export function guardedBuild(title: string, build: () => ChartSpec): ChartBuild {
  try {
    return { title, spec: build() };
  } catch (error) {
    if (!(error instanceof ChartRuleError)) {
      throw error;
    }
    if (isDevMode()) {
      console.error(`[chart] ${error.code}: ${error.message}`);
      return { title, error: `Chart not displayed: ${error.message}` };
    }
    return { title, error: 'This chart cannot be displayed.' };
  }
}

/** Inputs shared by every chart component. */
@Directive()
export abstract class ChartBase {
  /** Business title; the exposure level is appended: "<title> - <level>". */
  readonly title = input.required<string>();
  /** Exposure level of the amounts, or 'NotAnExposure' for ratios, spreads and indices. */
  readonly level = input.required<DisplayLevel>();
  /** Text read by screen readers (Highcharts accessibility module). */
  readonly description = input('');
  readonly height = input(320);
  readonly currency = input('EUR');

  protected readonly fullTitle = computed(() => titleWithLevel(this.title(), this.level()));

  protected common(): CommonChartInput {
    return {
      title: this.title(),
      level: this.level(),
      description: this.description(),
      height: this.height(),
      currency: this.currency(),
    };
  }

  protected abstract readonly build: Signal<ChartBuild>;
}

/** Figure shared by the chart components: title, chart or rule error, and the data table. */
@Component({
  selector: 'app-chart-frame',
  imports: [ChartCanvas],
  template: `
    <figure class="cr-chart">
      <figcaption class="cr-chart-title">{{ build().title }}</figcaption>
      @if (build().spec; as spec) {
        <app-chart-canvas [spec]="spec" />
        <details class="cr-chart-data">
          <summary>Data table</summary>
          <table>
            <thead>
              <tr>
                @for (header of spec.table.headers; track $index) {
                  <th scope="col">{{ header }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of spec.table.rows; track $index) {
                <tr>
                  @for (cell of row; track $index) {
                    @if ($first) {
                      <th scope="row">{{ cell }}</th>
                    } @else {
                      <td class="cr-numeric">{{ cell }}</td>
                    }
                  }
                </tr>
              }
            </tbody>
          </table>
        </details>
      } @else {
        <p role="alert" class="cr-chart-error">{{ build().error }}</p>
      }
    </figure>
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartFrame {
  readonly build = input.required<ChartBuild>();
}
