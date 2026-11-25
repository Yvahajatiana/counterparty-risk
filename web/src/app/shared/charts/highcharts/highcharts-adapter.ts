import type {
  Options,
  Point,
  SeriesOptionsType,
  TooltipFormatterCallbackFunction,
  XAxisPlotLinesOptions,
  YAxisPlotLinesOptions,
} from 'highcharts';
import { ChartAdapter } from '../chart-adapter';
import { formatValue } from '../chart-specs';
import { CartesianSpec, ChartSpec, ChartUnit, WaterfallSpec } from '../chart.models';

/** Display factor of each unit: amounts in millions, ratios in percent. */
const SCALE: Readonly<Record<ChartUnit, number>> = {
  money: 1e-6,
  percent: 100,
  bps: 1,
  count: 1,
  index: 1,
};

const ENTITIES: Readonly<Record<string, string>> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
};

/**
 * Highcharts parses every label (titles, categories, series and point names, tooltips) as a
 * markup subset, even with useHTML: false. Data labels come from untrusted open data (GLEIF
 * names): they are escaped so that they always render as plain text.
 */
export function escapeText(value: string): string {
  return value.replace(/[&<>"']/g, (c) => ENTITIES[c] ?? c);
}

function dateLabel(ms: number): string {
  return new Date(ms).toISOString().slice(0, 10);
}

/** Tooltip as a single line of text: "<series> - <x>: <value>". */
function tooltipFormatter(spec: ChartSpec): TooltipFormatterCallbackFunction {
  const scale = SCALE[spec.unit];
  const dateAxis = spec.kind === 'cartesian' && spec.xKind === 'date';
  const showSeries = spec.kind === 'cartesian' && spec.series.length > 1;
  const percentStack = spec.kind === 'cartesian' && spec.stacking === 'percent';
  return function (this: Point) {
    // Category and series names were escaped when the options were built.
    const x = dateAxis ? dateLabel(Number(this.x)) : String(this.category ?? this.name ?? '');
    const raw = this.y == null ? null : this.y / scale;
    let value = escapeText(formatValue(spec.unit, raw, spec.currency));
    if (percentStack && this.percentage != null) {
      value += ` (${this.percentage.toFixed(1)}% of total)`;
    }
    return showSeries ? `${this.series.name} - ${x}: ${value}` : `${x}: ${value}`;
  };
}

function baseOptions(spec: ChartSpec): Options {
  return {
    chart: { styledMode: true, height: spec.height },
    // The visible title is the Angular figcaption; this one feeds the accessibility module.
    title: { text: escapeText(spec.title), useHTML: false },
    subtitle: { text: undefined, useHTML: false },
    // Licence attribution (non-commercial Highcharts licence, ADR-001 §6): always on.
    credits: { enabled: true },
    accessibility: {
      enabled: true,
      description: escapeText(spec.description || spec.title),
    },
    legend: { useHTML: false },
    tooltip: { useHTML: false, formatter: tooltipFormatter(spec) },
    plotOptions: { series: { dataLabels: { useHTML: false } } },
  };
}

function waterfallOptions(spec: WaterfallSpec): Options {
  const scale = SCALE[spec.unit];
  return {
    ...baseOptions(spec),
    legend: { enabled: false, useHTML: false },
    xAxis: { type: 'category', labels: { useHTML: false } },
    yAxis: {
      title: { text: escapeText(spec.yTitle), useHTML: false },
      labels: { useHTML: false },
    },
    series: [
      {
        type: 'waterfall',
        name: escapeText(spec.title),
        data: spec.steps.map((step) => {
          const name = escapeText(step.label);
          switch (step.kind) {
            case 'subtotal':
              return { name, isIntermediateSum: true, className: 'cr-point-sum' };
            case 'total':
              return { name, isSum: true, className: 'cr-point-sum' };
            default:
              return {
                name,
                y: step.value * scale,
                className: step.value < 0 ? 'cr-point-reduction' : undefined,
              };
          }
        }),
      },
    ],
  };
}

function cartesianOptions(spec: CartesianSpec): Options {
  const scale = SCALE[spec.unit];
  const dateAxis = spec.xKind === 'date';
  const percentAxis = spec.unit === 'percent' || spec.stacking === 'percent';

  const eventLines: XAxisPlotLinesOptions[] = spec.events.flatMap((event) => {
    const value = dateAxis
      ? Date.parse(`${event.at}T00:00:00Z`)
      : spec.categories.indexOf(event.at);
    if (Number.isNaN(value) || value < 0) {
      return [];
    }
    return [
      {
        value,
        className: 'cr-plotline-event',
        label: { text: escapeText(event.label), useHTML: false },
      },
    ];
  });

  const limitLines: YAxisPlotLinesOptions[] = spec.limits.map((limit) => ({
    value: limit.value * scale,
    className: limit.kind === 'warn' ? 'cr-plotline-warn' : 'cr-plotline-breach',
    label: limit.label ? { text: escapeText(limit.label), useHTML: false } : undefined,
  }));

  const series = spec.series.map(
    (s) =>
      ({
        type: s.mark,
        id: s.id,
        name: escapeText(s.name),
        className: s.dashed ? 'cr-series-dashed' : undefined,
        marker: { enabled: s.mark === 'line' && s.points.length <= 24 },
        data: s.points.map((p) => ({
          x: dateAxis ? (p.x as number) : undefined,
          y: p.y == null ? null : p.y * scale,
          className: p.className,
        })),
      }) as SeriesOptionsType,
  );

  return {
    ...baseOptions(spec),
    legend: { enabled: spec.series.length > 1, useHTML: false },
    xAxis: {
      type: dateAxis ? 'datetime' : 'category',
      categories: dateAxis ? undefined : spec.categories.map(escapeText),
      title: { text: spec.xTitle ? escapeText(spec.xTitle) : undefined, useHTML: false },
      labels: { useHTML: false },
      plotLines: eventLines,
    },
    // A single Y axis, by construction (no dual axis, EDB).
    yAxis: {
      title: { text: escapeText(spec.yTitle), useHTML: false },
      labels: { useHTML: false, format: percentAxis ? '{value}%' : undefined },
      max: spec.yMax == null ? undefined : spec.yMax * scale,
      plotLines: limitLines,
    },
    plotOptions: {
      series: {
        stacking: spec.stacking === 'none' ? undefined : spec.stacking,
        dataLabels: { useHTML: false },
      },
    },
    series,
  };
}

export const highchartsAdapter: ChartAdapter<Options> = {
  toOptions(spec: ChartSpec): Options {
    return spec.kind === 'waterfall' ? waterfallOptions(spec) : cartesianOptions(spec);
  },
};
