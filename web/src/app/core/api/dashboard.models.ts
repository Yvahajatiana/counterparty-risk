// Contracts of the BFF dashboard endpoints (/api/dashboard/*).
// Mirror of src/CounterpartyRisk.Bff/Mock/DashboardContracts.cs: change both together.

/** ISO date (yyyy-MM-dd). */
export type IsoDate = string;

/** BCBS 239 data quality header carried by every widget payload. */
export interface DataQuality {
  readonly asOfDate: IsoDate;
  readonly reportingCurrency: string;
  readonly source: string;
  readonly status: 'Real' | 'Transformed' | 'Synthetic';
  readonly completeness: 'Complete' | 'Partial';
}

export interface CounterpartyRef {
  readonly id: string;
  readonly name: string;
  readonly group: string;
  readonly businessLine: string;
}

export interface DatedValue {
  readonly date: IsoDate;
  readonly value: number;
}

// Summary

export interface KpiTile {
  readonly code: string;
  readonly label: string;
  /** Exposure or measure level, always displayed with the amount. */
  readonly level: string;
  readonly vision: string;
  readonly value: number;
  readonly previousDay: number | null;
  readonly previousMonth: number | null;
  readonly unit: 'EUR' | 'count';
  readonly route: string;
  readonly breakdown: string | null;
  readonly status: 'ok' | 'warn' | 'breach' | null;
}

export interface SummaryResponse {
  readonly quality: DataQuality;
  readonly tiles: readonly KpiTile[];
}

// Current exposure

export interface CcrWaterfall {
  readonly grossNotional: number;
  readonly grossPositiveMtm: number;
  readonly nettingBenefit: number;
  readonly currentExposure: number;
  readonly collateralReceivedNet: number;
  readonly replacementCost: number;
}

export interface CurrentExposureRow {
  readonly counterpartyId: string;
  readonly counterparty: string;
  readonly businessLine: string;
  readonly collateralStatus: string;
  readonly nettingSets: number;
  readonly grossNotional: number;
  readonly grossPositiveMtm: number;
  readonly currentExposure: number;
  readonly collateralReceivedNet: number;
  readonly replacementCost: number;
}

export interface FinancingCommitment {
  readonly drawn: number;
  readonly undrawn: number;
  readonly totalCommitment: number;
  readonly weightedCcf: number;
  readonly offBalanceSheetEad: number;
  readonly financingEad: number;
}

export interface CurrentExposureResponse {
  readonly quality: DataQuality;
  readonly ccr: CcrWaterfall;
  readonly rows: readonly CurrentExposureRow[];
  readonly financing: FinancingCommitment;
}

// Future exposure

export interface ExposureProfilePoint {
  readonly horizon: string;
  readonly months: number;
  readonly ee: number;
  readonly pfe: number;
  readonly ene: number;
}

export interface PfePeakRow {
  readonly counterpartyId: string;
  readonly counterparty: string;
  readonly businessLine: string;
  readonly peakPfe: number;
  readonly peakHorizon: string;
  readonly epe: number;
  readonly effectiveEpe: number;
}

export interface FutureExposureResponse {
  readonly quality: DataQuality;
  readonly pfeQuantile: number;
  readonly counterparties: readonly CounterpartyRef[];
  readonly counterpartyId: string;
  readonly profile: readonly ExposureProfilePoint[];
  readonly epe: number;
  readonly effectiveEpe: number;
  readonly peaks: readonly PfePeakRow[];
}

// Limits

export interface LimitRow {
  readonly counterpartyId: string;
  readonly counterparty: string;
  readonly group: string;
  readonly desk: string;
  readonly measure: string;
  readonly limit: number;
  readonly exposure: number;
  readonly utilisation: number;
  readonly headroom: number;
  readonly breachAgeDays: number | null;
}

export interface UtilisationSeries {
  readonly counterpartyId: string;
  readonly counterparty: string;
  readonly points: readonly DatedValue[];
}

export interface LimitsResponse {
  readonly quality: DataQuality;
  readonly thresholds: { readonly warning: number; readonly breach: number };
  readonly rows: readonly LimitRow[];
  readonly history: readonly UtilisationSeries[];
}

// History

export type HistoryIndicator = 'Ead' | 'Rwa' | 'PeakPfe' | 'Cva' | 'ReplacementCost';
export type HistoryFrequency = 'Daily' | 'Monthly';

export interface HistoryResponse {
  readonly quality: DataQuality;
  readonly indicator: HistoryIndicator;
  readonly label: string;
  readonly level: string;
  readonly vision: string;
  readonly frequency: HistoryFrequency;
  readonly points: readonly DatedValue[];
  readonly events: readonly { readonly date: IsoDate; readonly label: string }[];
}
