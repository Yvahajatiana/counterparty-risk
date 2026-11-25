import { Pipe, PipeTransform } from '@angular/core';

// Pure formatters shared by templates, AG Grid and the chart adapter. The UI locale is en-GB.
const LOCALE = 'en-GB';

const oneDecimal = new Intl.NumberFormat(LOCALE, {
  maximumFractionDigits: 1,
  minimumFractionDigits: 1,
});
const integer = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 0 });
const percent = new Intl.NumberFormat(LOCALE, {
  style: 'percent',
  maximumFractionDigits: 1,
  minimumFractionDigits: 1,
});

/** Amount in millions of the reporting currency, e.g. "1,234.5 m". */
export function formatMillions(value: number | null | undefined): string {
  return value == null ? '-' : `${oneDecimal.format(value / 1_000_000)} m`;
}

/** Chart value in millions, rounded to one decimal (axes and tooltips show "m"). */
export function toMillions(value: number): number {
  return Math.round(value / 100_000) / 10;
}

const COMPACT_STEPS: readonly (readonly [number, string])[] = [
  [1_000_000_000, 'bn'],
  [1_000_000, 'm'],
  [1_000, 'k'],
];

/** Amount with a magnitude abbreviation and its currency, e.g. "1.2 bn EUR", "345.6 m EUR". */
export function formatMoney(value: number | null | undefined, currency = 'EUR'): string {
  if (value == null) {
    return '-';
  }
  const abs = Math.abs(value);
  for (const [step, suffix] of COMPACT_STEPS) {
    if (abs >= step) {
      return `${oneDecimal.format(value / step)} ${suffix} ${currency}`;
    }
  }
  return `${integer.format(value)} ${currency}`;
}

export function formatInteger(value: number | null | undefined): string {
  return value == null ? '-' : integer.format(value);
}

/** Ratio shown as a percentage: 0.853 -> "85.3%". */
export function formatPercent(value: number | null | undefined): string {
  return value == null ? '-' : percent.format(value);
}

/** Basis points: 12.34 -> "12 bps". */
export function formatBps(value: number | null | undefined): string {
  return value == null ? '-' : `${integer.format(value)} bps`;
}

export type DeltaDirection = 'up' | 'down' | 'flat';

export interface FormattedDelta {
  readonly text: string;
  readonly direction: DeltaDirection;
}

/** Signed variation, e.g. "+1.2 m EUR" / "-0.4%", with its direction for the colour code. */
export function formatDelta(
  delta: number | null | undefined,
  format: (value: number) => string,
): FormattedDelta | null {
  if (delta == null) {
    return null;
  }
  const direction: DeltaDirection = delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat';
  return { text: `${delta > 0 ? '+' : ''}${format(delta)}`, direction };
}

export function formatDate(value: string | null | undefined): string {
  if (!value) {
    return '-';
  }
  return new Date(`${value}T00:00:00Z`).toLocaleDateString(LOCALE, { timeZone: 'UTC' });
}

/** Template helper: `{{ amount | millions }}`. */
@Pipe({ name: 'millions' })
export class MillionsPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return formatMillions(value);
  }
}

/** Template helper: `{{ amount | money: 'EUR' }}`. */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(value: number | null | undefined, currency = 'EUR'): string {
    return formatMoney(value, currency);
  }
}

@Pipe({ name: 'pct' })
export class PercentPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return formatPercent(value);
  }
}

@Pipe({ name: 'bps' })
export class BpsPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return formatBps(value);
  }
}

@Pipe({ name: 'asOf' })
export class AsOfDatePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatDate(value);
  }
}
