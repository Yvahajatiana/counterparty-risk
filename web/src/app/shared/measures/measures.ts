// Mirror of the closed vocabularies of src/CounterpartyRisk.Domain.Contracts/Measures (#5).
// The API serialises these enums as strings: change both sides together.

/** Stage of the mitigation chain an amount belongs to (ADR-002 §1). */
export type ExposureLevel =
  | 'Notional'
  | 'GrossBeforeCrm'
  | 'AfterNetting'
  | 'AfterCollateral'
  | 'Ead'
  | 'Drawn'
  | 'Undrawn'
  | 'TotalCommitment'
  | 'LargeExposureValue'
  | 'RiskWeighted'
  | 'ValuationAdjustment';

/** Family of a measure. Economic and regulatory measures are never mixed in one chart. */
export type MeasureFamily = 'CurrentExposure' | 'Economic' | 'Regulatory' | 'Accounting';

/** A published benchmark is never shown together with recomputed bank figures (W12 rule). */
export type DataOrigin = 'Recomputed' | 'PublishedBenchmark';

/** Display labels of the exposure levels, shown next to every exposure amount. */
export const EXPOSURE_LEVEL_LABELS: Readonly<Record<ExposureLevel, string>> = {
  Notional: 'Gross notional (volume, not an exposure)',
  GrossBeforeCrm: 'Gross exposure before CRM',
  AfterNetting: 'After netting',
  AfterCollateral: 'After collateral',
  Ead: 'EAD',
  Drawn: 'Drawn',
  Undrawn: 'Undrawn',
  TotalCommitment: 'Total commitment',
  LargeExposureValue: 'Large exposure value',
  RiskWeighted: 'Risk-weighted (RWA)',
  ValuationAdjustment: 'Valuation adjustment',
};

/** A chain of levels, e.g. a waterfall from gross exposure to the amount after collateral. */
export interface ExposureLevelRange {
  readonly from: ExposureLevel;
  readonly to: ExposureLevel;
}

/**
 * Level carried by a chart or a table. It is a required input: a figure that is not an exposure
 * (utilisation, spread, HHI) must say so explicitly with 'NotAnExposure'.
 */
export type DisplayLevel = ExposureLevel | ExposureLevelRange | 'NotAnExposure';

export function levelLabel(level: DisplayLevel): string | null {
  if (level === 'NotAnExposure') {
    return null;
  }
  if (typeof level === 'string') {
    return EXPOSURE_LEVEL_LABELS[level];
  }
  return `${EXPOSURE_LEVEL_LABELS[level.from]} to ${EXPOSURE_LEVEL_LABELS[level.to]}`;
}

/** Title shown on a chart or a table: "<title> - <exposure level>". */
export function titleWithLevel(title: string, level: DisplayLevel): string {
  const label = levelLabel(level);
  return label ? `${title} - ${label}` : title;
}
