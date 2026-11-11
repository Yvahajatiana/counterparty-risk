import { httpResource } from '@angular/common/http';
import { Signal } from '@angular/core';
import {
  CurrentExposureResponse,
  FutureExposureResponse,
  HistoryFrequency,
  HistoryIndicator,
  HistoryResponse,
  LimitsResponse,
  SummaryResponse,
} from './dashboard.models';

// Signal-based readers of the BFF dashboard endpoints. Same-origin relative URLs only:
// the BFF is the single entry point and holds the session (no token in the browser).
// Must be called in an injection context (component field initialiser).

const BASE = '/api/dashboard';

export const summaryResource = () => httpResource<SummaryResponse>(() => `${BASE}/summary`);

export const currentExposureResource = () =>
  httpResource<CurrentExposureResponse>(() => `${BASE}/current-exposure`);

export const futureExposureResource = (counterpartyId: Signal<string | null>) =>
  httpResource<FutureExposureResponse>(() => {
    const id = counterpartyId();
    const params: Record<string, string> = id ? { counterpartyId: id } : {};
    return { url: `${BASE}/future-exposure`, params };
  });

export const limitsResource = () => httpResource<LimitsResponse>(() => `${BASE}/limits`);

export const historyResource = (
  indicator: Signal<HistoryIndicator>,
  frequency: Signal<HistoryFrequency>,
) =>
  httpResource<HistoryResponse>(() => ({
    url: `${BASE}/history`,
    params: { indicator: indicator(), frequency: frequency() },
  }));
