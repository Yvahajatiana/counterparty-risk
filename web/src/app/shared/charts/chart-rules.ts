import { DataOrigin, DisplayLevel, MeasureFamily } from '../measures/measures';
import { ChartUnit } from './chart.models';

/**
 * Violation of a domain rule of the dashboard (CLAUDE.md, EDB): the chart is not drawn.
 * Any exception to these rules needs a Lead review first.
 */
export class ChartRuleError extends Error {
  constructor(
    readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'ChartRuleError';
  }
}

interface Classified {
  readonly family: MeasureFamily;
  readonly origin?: DataOrigin;
}

/**
 * Economic (EE, PFE) and regulatory (EAD, RWA) measures are never shown in one chart, and a
 * published benchmark is never shown with recomputed bank figures.
 */
export function assertNoMixedFamilies(items: readonly Classified[]): void {
  const families = new Set(items.map((item) => item.family));
  if (families.has('Economic') && families.has('Regulatory')) {
    throw new ChartRuleError(
      'chart.mixed_families',
      'Economic (EE/PFE) and regulatory (EAD/RWA) measures cannot be shown in the same chart.',
    );
  }
  const origins = new Set(items.map((item) => item.origin ?? 'Recomputed'));
  if (origins.size > 1) {
    throw new ChartRuleError(
      'chart.mixed_origins',
      'Published benchmarks cannot be shown in the same chart as recomputed figures.',
    );
  }
}

/** A chart has a single Y axis: series of different units must be rebased to 100. */
export function assertSingleUnit(units: readonly ChartUnit[], rebase100: boolean): void {
  if (!rebase100 && new Set(units).size > 1) {
    throw new ChartRuleError(
      'chart.mixed_units',
      'Series of different units need rebase100: a chart has a single Y axis.',
    );
  }
}

/** Every exposure amount carries its exposure level. */
export function assertExposureLevel(level: DisplayLevel, units: readonly ChartUnit[]): void {
  if (level === 'NotAnExposure' && units.includes('money')) {
    throw new ChartRuleError(
      'chart.missing_exposure_level',
      'An amount chart must state its exposure level.',
    );
  }
}
