import { HttpResourceRef, httpResource } from '@angular/common/http';
import { Signal, computed } from '@angular/core';
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

/** Read-only view of a widget resource whose value() never throws. */
export interface WidgetData<T> {
  readonly value: Signal<T | undefined>;
  readonly isLoading: Signal<boolean>;
  readonly error: Signal<unknown>;
}

// HttpResourceRef.value() throws while the resource is in error: expose undefined instead,
// so templates and computeds fall back to the LoadState error message.
function widgetData<T>(ref: HttpResourceRef<T | undefined>): WidgetData<T> {
  return {
    value: computed(() => (ref.hasValue() ? ref.value() : undefined)),
    isLoading: ref.isLoading,
    error: ref.error,
  };
}

export const summaryResource = () =>
  widgetData(httpResource<SummaryResponse>(() => `${BASE}/summary`));

export const currentExposureResource = () =>
  widgetData(httpResource<CurrentExposureResponse>(() => `${BASE}/current-exposure`));

export const futureExposureResource = (counterpartyId: Signal<string | null>) =>
  widgetData(
    httpResource<FutureExposureResponse>(() => {
      const id = counterpartyId();
      const params: Record<string, string> = id ? { counterpartyId: id } : {};
      return { url: `${BASE}/future-exposure`, params };
    }),
  );

export const limitsResource = () =>
  widgetData(httpResource<LimitsResponse>(() => `${BASE}/limits`));

export const historyResource = (
  indicator: Signal<HistoryIndicator>,
  frequency: Signal<HistoryFrequency>,
) =>
  widgetData(
    httpResource<HistoryResponse>(() => ({
      url: `${BASE}/history`,
      params: { indicator: indicator(), frequency: frequency() },
    })),
  );
