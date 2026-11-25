import { ChartSpec } from './chart.models';

/**
 * Translation of a library-neutral ChartSpec into the options of a chart library (ADR-001 §6).
 * V1 has a single Highcharts implementation (highcharts/highcharts-adapter.ts); switching to
 * Apache ECharts means a new adapter and canvas, never a change in the widgets.
 */
export interface ChartAdapter<TOptions> {
  toOptions(spec: ChartSpec): TOptions;
}
