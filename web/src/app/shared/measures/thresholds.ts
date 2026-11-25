// Limit utilisation thresholds shared by tables and charts (EDB; configurable server-side in #27).

export interface Thresholds {
  readonly warn: number;
  readonly breach: number;
}

/** Default limit utilisation thresholds of the EDB: warning from 80 %, breach from 100 %. */
export const DEFAULT_THRESHOLDS: Thresholds = { warn: 0.8, breach: 1 };

export type ThresholdStatus = 'ok' | 'warn' | 'breach';

export function thresholdStatus(value: number, thresholds: Thresholds): ThresholdStatus {
  return value >= thresholds.breach ? 'breach' : value >= thresholds.warn ? 'warn' : 'ok';
}
