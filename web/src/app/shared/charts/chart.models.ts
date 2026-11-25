// Library-neutral chart model (ChartAdapter, ADR-001 §6). Widgets only use these types and the
// chart components: no Highcharts type ever leaves shared/charts/highcharts.
import { DataOrigin, DisplayLevel, MeasureFamily } from '../measures/measures';

export type { DataOrigin, DisplayLevel, MeasureFamily };
export { DEFAULT_THRESHOLDS, thresholdStatus } from '../measures/thresholds';
export type { Thresholds, ThresholdStatus } from '../measures/thresholds';

/**
 * Unit of the values. Raw values are given in the base unit: amounts in the reporting currency,
 * percentages as ratios (0.85), basis points as numbers of bps, indices as index points.
 */
export type ChartUnit = 'money' | 'percent' | 'bps' | 'count' | 'index';

/** Kind of the X axis: categories (labels, horizons) or ISO dates (yyyy-MM-dd). */
export type XAxisKind = 'category' | 'date';

/** One point: [category label or ISO date, value]. A null value is a gap. */
export type CrPoint = readonly [x: string, y: number | null];

export interface CrSeries {
  readonly id: string;
  readonly name: string;
  readonly family: MeasureFamily;
  /** Defaults to 'Recomputed'. */
  readonly origin?: DataOrigin;
  /** Defaults to the unit of the chart. Series of different units need rebase100. */
  readonly unit?: ChartUnit;
  readonly points: readonly CrPoint[];
  readonly dashed?: boolean;
}

export interface WaterfallStep {
  readonly label: string;
  /** Ignored for 'subtotal' and 'total', which are computed from the previous steps. */
  readonly value: number;
  readonly kind: 'start' | 'delta' | 'subtotal' | 'total';
  readonly family: MeasureFamily;
}

/** A value of a ranking chart (horizontal bars, Pareto). */
export interface CrBarItem {
  readonly label: string;
  readonly value: number;
}

/** Horizontal reference line on the Y axis (limit, threshold), in the unit of the chart. */
export interface CrLimitLine {
  readonly value: number;
  readonly kind: 'warn' | 'breach' | 'limit';
  readonly label?: string;
}

/** Vertical event marker on the X axis (method change, regulation, default). */
export interface CrEventMarker {
  /** Category label or ISO date, matching the X axis kind. */
  readonly at: string;
  readonly label: string;
}

// Internal specification handed to the adapter. Built only by the chart components, after the
// domain guards have run.

export interface SpecPoint {
  /** Category label, or epoch milliseconds for a date axis. */
  readonly x: string | number;
  /** Raw value in the base unit (see ChartUnit); the adapter scales it for display. */
  readonly y: number | null;
  readonly className?: string;
}

export interface SpecSeries {
  readonly id: string;
  readonly name: string;
  readonly mark: 'line' | 'column' | 'bar';
  readonly dashed: boolean;
  readonly points: readonly SpecPoint[];
}

export interface DataTable {
  readonly headers: readonly string[];
  readonly rows: readonly (readonly string[])[];
}

interface ChartSpecBase {
  /** Full title, exposure level included. */
  readonly title: string;
  readonly description: string;
  readonly unit: ChartUnit;
  readonly currency: string;
  readonly height: number;
  readonly yTitle: string;
  readonly table: DataTable;
}

export interface WaterfallSpec extends ChartSpecBase {
  readonly kind: 'waterfall';
  readonly steps: readonly WaterfallStep[];
}

export interface CartesianSpec extends ChartSpecBase {
  readonly kind: 'cartesian';
  readonly xKind: XAxisKind;
  readonly xTitle: string;
  /** Category labels in display order (category axis only). */
  readonly categories: readonly string[];
  readonly stacking: 'none' | 'normal' | 'percent';
  /** Y axis maximum in base unit, e.g. 1 (100 %) for a Pareto. */
  readonly yMax?: number;
  readonly series: readonly SpecSeries[];
  readonly limits: readonly CrLimitLine[];
  readonly events: readonly CrEventMarker[];
}

export type ChartSpec = WaterfallSpec | CartesianSpec;
