// Pure builders: typed component inputs -> guarded, library-neutral ChartSpec.
import { DisplayLevel, MeasureFamily, titleWithLevel } from '../measures/measures';
import { formatBps, formatInteger, formatMoney, formatPercent } from '../format/amount';
import {
  assertExposureLevel,
  assertNoMixedFamilies,
  assertSingleUnit,
  ChartRuleError,
} from './chart-rules';
import {
  CartesianSpec,
  ChartUnit,
  CrBarItem,
  CrEventMarker,
  CrLimitLine,
  CrSeries,
  DataTable,
  SpecPoint,
  SpecSeries,
  thresholdStatus,
  Thresholds,
  WaterfallSpec,
  WaterfallStep,
  XAxisKind,
} from './chart.models';

export interface CommonChartInput {
  readonly title: string;
  readonly level: DisplayLevel;
  readonly description: string;
  readonly height: number;
  readonly currency: string;
}

const indexFormat = new Intl.NumberFormat('en-GB', {
  maximumFractionDigits: 1,
  minimumFractionDigits: 1,
});

/** Text of a raw value in its unit, used by tooltips and data tables. */
export function formatValue(unit: ChartUnit, value: number | null, currency: string): string {
  if (value == null) {
    return '-';
  }
  switch (unit) {
    case 'money':
      return formatMoney(value, currency);
    case 'percent':
      return formatPercent(value);
    case 'bps':
      return formatBps(value);
    case 'count':
      return formatInteger(value);
    case 'index':
      return indexFormat.format(value);
  }
}

/** Y axis title of a unit; money is displayed in millions of the reporting currency. */
export function axisTitle(unit: ChartUnit, currency: string): string {
  switch (unit) {
    case 'money':
      return `${currency} m`;
    case 'percent':
      return '%';
    case 'bps':
      return 'bps';
    case 'count':
      return 'Count';
    case 'index':
      return 'Index (base 100 = first point)';
  }
}

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

function toDateMs(value: string): number {
  const ms = ISO_DATE.test(value) ? Date.parse(`${value}T00:00:00Z`) : Number.NaN;
  if (Number.isNaN(ms)) {
    throw new ChartRuleError('chart.invalid_date', 'A date axis needs ISO dates (yyyy-MM-dd).');
  }
  return ms;
}

function dateLabel(ms: number): string {
  return new Date(ms).toISOString().slice(0, 10);
}

function base(input: CommonChartInput, unit: ChartUnit, table: DataTable, yTitle?: string) {
  return {
    title: titleWithLevel(input.title, input.level),
    description: input.description,
    unit,
    currency: input.currency,
    height: input.height,
    yTitle: yTitle ?? axisTitle(unit, input.currency),
    table,
  };
}

// Waterfall

export function waterfallSpec(
  input: CommonChartInput & { readonly steps: readonly WaterfallStep[] },
): WaterfallSpec {
  assertNoMixedFamilies(input.steps);
  assertExposureLevel(input.level, ['money']);

  let running = 0;
  const rows = input.steps.map((step) => {
    if (step.kind === 'start') {
      running = step.value;
    } else if (step.kind === 'delta') {
      running += step.value;
    }
    const amount = step.kind === 'subtotal' || step.kind === 'total' ? running : step.value;
    return [step.label, formatValue('money', amount, input.currency)];
  });

  return {
    kind: 'waterfall',
    ...base(input, 'money', { headers: ['Step', 'Amount'], rows }),
    steps: input.steps,
  };
}

// Cartesian charts (lines, bars, Pareto)

interface CartesianInput extends CommonChartInput {
  readonly series: readonly CrSeries[];
  readonly unit: ChartUnit;
  readonly xKind: XAxisKind;
  readonly xTitle: string;
  readonly mark: SpecSeries['mark'];
  readonly stacking: CartesianSpec['stacking'];
  readonly rebase100: boolean;
  readonly limits: readonly CrLimitLine[];
  readonly events: readonly CrEventMarker[];
}

function rebase(points: readonly (number | null)[]): (number | null)[] {
  const first = points.find((y) => y != null && y !== 0);
  return points.map((y) => (y == null || first == null ? null : (y / first) * 100));
}

function cartesianSpec(input: CartesianInput): CartesianSpec {
  assertNoMixedFamilies(input.series);
  const units = input.series.map((s) => s.unit ?? input.unit);
  assertSingleUnit(units, input.rebase100);
  assertExposureLevel(input.level, units.length ? units : [input.unit]);
  if (input.rebase100 && input.limits.length) {
    throw new ChartRuleError(
      'chart.limits_with_rebase',
      'Limit lines are expressed in a unit: they cannot be drawn on a rebased chart.',
    );
  }
  const unit: ChartUnit = input.rebase100 ? 'index' : (units[0] ?? input.unit);

  // X values in first-seen order across all series.
  const xs: (string | number)[] = [];
  const seen = new Set<string | number>();
  const keyOf = (x: string) => (input.xKind === 'date' ? toDateMs(x) : x);
  for (const s of input.series) {
    for (const [x] of s.points) {
      const key = keyOf(x);
      if (!seen.has(key)) {
        seen.add(key);
        xs.push(key);
      }
    }
  }
  if (input.xKind === 'date') {
    xs.sort((a, b) => (a as number) - (b as number));
  }

  const series: SpecSeries[] = input.series.map((s) => {
    const byX = new Map(s.points.map(([x, y]) => [keyOf(x), y] as const));
    const raw = xs.map((x) => byX.get(x) ?? null);
    const values = input.rebase100 ? rebase(raw) : raw;
    return {
      id: s.id,
      name: s.name,
      mark: input.mark,
      dashed: s.dashed ?? false,
      points: xs.map((x, i): SpecPoint => ({ x, y: values[i] ?? null })),
    };
  });

  const xHeader = input.xTitle || (input.xKind === 'date' ? 'Date' : 'Category');
  const table: DataTable = {
    headers: [xHeader, ...series.map((s) => s.name)],
    rows: xs.map((x, i) => [
      typeof x === 'number' ? dateLabel(x) : x,
      ...series.map((s) => formatValue(unit, s.points[i]?.y ?? null, input.currency)),
    ]),
  };

  const yTitle = input.stacking === 'percent' ? 'Share of total (%)' : undefined;
  return {
    kind: 'cartesian',
    ...base(input, unit, table, yTitle),
    xKind: input.xKind,
    xTitle: input.xTitle,
    categories: input.xKind === 'category' ? (xs as string[]) : [],
    stacking: input.stacking,
    series,
    limits: input.limits,
    events: input.events,
  };
}

export interface LineChartInput extends CommonChartInput {
  readonly series: readonly CrSeries[];
  readonly unit: ChartUnit;
  readonly xKind: XAxisKind;
  readonly xTitle: string;
  readonly rebase100: boolean;
  readonly limits: readonly CrLimitLine[];
  readonly events: readonly CrEventMarker[];
}

export function lineChartSpec(input: LineChartInput): CartesianSpec {
  return cartesianSpec({ ...input, mark: 'line', stacking: 'none' });
}

export interface BarChartInput extends CommonChartInput {
  readonly series: readonly CrSeries[];
  readonly unit: ChartUnit;
  readonly orientation: 'column' | 'bar';
  readonly xTitle: string;
  readonly events: readonly CrEventMarker[];
}

export function stackedBarChartSpec(
  input: BarChartInput & { readonly stacking: 'normal' | 'percent' },
): CartesianSpec {
  return cartesianSpec({
    ...input,
    xKind: 'category',
    mark: input.orientation,
    rebase100: false,
    limits: [],
  });
}

export function groupedBarChartSpec(input: BarChartInput): CartesianSpec {
  return cartesianSpec({
    ...input,
    xKind: 'category',
    mark: input.orientation,
    stacking: 'none',
    rebase100: false,
    limits: [],
  });
}

export interface RankingChartInput extends CommonChartInput {
  readonly items: readonly CrBarItem[];
  readonly family: MeasureFamily;
  readonly seriesName: string;
}

/** Horizontal bars, coloured by threshold status (limit utilisation, CS01). */
export function horizontalBarChartSpec(
  input: RankingChartInput & {
    readonly unit: ChartUnit;
    readonly thresholds: Thresholds | null;
  },
): CartesianSpec {
  const spec = cartesianSpec({
    ...input,
    series: [
      {
        id: 'values',
        name: input.seriesName,
        family: input.family,
        unit: input.unit,
        points: input.items.map((item) => [item.label, item.value] as const),
      },
    ],
    xKind: 'category',
    xTitle: '',
    mark: 'bar',
    stacking: 'none',
    rebase100: false,
    limits: input.thresholds
      ? [
          { value: input.thresholds.warn, kind: 'warn' },
          { value: input.thresholds.breach, kind: 'breach' },
        ]
      : [],
    events: [],
  });
  const thresholds = input.thresholds;
  if (!thresholds) {
    return spec;
  }
  return {
    ...spec,
    series: spec.series.map((s) => ({
      ...s,
      points: s.points.map((p) =>
        p.y == null ? p : { ...p, className: `cr-point-${thresholdStatus(p.y, thresholds)}` },
      ),
    })),
  };
}

/**
 * Pareto on a single percentage axis: bars are the share of each item in the total of all
 * items, the line is the cumulative share of the top N.
 */
export function paretoChartSpec(
  input: RankingChartInput & { readonly top: number },
): CartesianSpec {
  if (input.items.some((item) => item.value < 0)) {
    throw new ChartRuleError('chart.negative_pareto', 'A Pareto chart needs non-negative values.');
  }
  const total = input.items.reduce((sum, item) => sum + item.value, 0);
  const ranked = [...input.items].sort((a, b) => b.value - a.value).slice(0, input.top);
  let cumulative = 0;
  const shares = ranked.map((item) => {
    const share = total === 0 ? 0 : item.value / total;
    cumulative += share;
    return { label: item.label, share, cumulative };
  });

  const spec = cartesianSpec({
    ...input,
    series: [
      {
        id: 'share',
        name: `${input.seriesName} - share of total`,
        family: input.family,
        points: shares.map((s) => [s.label, s.share] as const),
      },
      {
        id: 'cumulative',
        name: 'Cumulative share',
        family: input.family,
        points: shares.map((s) => [s.label, s.cumulative] as const),
      },
    ],
    unit: 'percent',
    xKind: 'category',
    xTitle: '',
    mark: 'column',
    stacking: 'none',
    rebase100: false,
    limits: [],
    events: [],
  });
  return {
    ...spec,
    yMax: 1,
    series: spec.series.map((s) => (s.id === 'cumulative' ? { ...s, mark: 'line' } : s)),
  };
}
