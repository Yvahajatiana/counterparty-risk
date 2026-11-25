// ISO calendar dates (yyyy-MM-dd) as used by the API, always read and written in UTC.

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

/** Epoch milliseconds of an ISO date at 00:00 UTC, or NaN when the value is not an ISO date. */
export function parseIsoDate(value: string | null | undefined): number {
  return value && ISO_DATE.test(value) ? Date.parse(`${value}T00:00:00Z`) : Number.NaN;
}

/** ISO date (yyyy-MM-dd) of an epoch milliseconds value, in UTC. */
export function isoDateLabel(ms: number): string {
  return new Date(ms).toISOString().slice(0, 10);
}
