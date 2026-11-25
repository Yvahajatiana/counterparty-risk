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

/** Magnitudes in ascending order; units are shown with one decimal, plain amounts with none. */
const MAGNITUDES: readonly (readonly [step: number, suffix: string])[] = [
  [1, ''],
  [1_000, 'k'],
  [1_000_000, 'm'],
  [1_000_000_000, 'bn'],
];

function roundAt(value: number, index: number): number {
  return index === 0 ? Math.round(value) : Math.round(value * 10) / 10;
}

/** Amount with a magnitude abbreviation and its currency, e.g. "1.2 bn EUR", "345.6 m EUR". */
export function formatMoney(value: number | null | undefined, currency = 'EUR'): string {
  if (value == null) {
    return '-';
  }
  const abs = Math.abs(value);
  let index = 0;
  while (index < MAGNITUDES.length - 1 && abs >= MAGNITUDES[index + 1]![0]) {
    index++;
  }
  // The magnitude is checked after rounding: 999,960 is "1.0 m EUR", never "1,000.0 k EUR".
  if (index < MAGNITUDES.length - 1 && roundAt(abs / MAGNITUDES[index]![0], index) >= 1000) {
    index++;
  }
  const [step, suffix] = MAGNITUDES[index]!;
  const text = index === 0 ? integer.format(value) : oneDecimal.format(value / step);
  return suffix ? `${text} ${suffix} ${currency}` : `${text} ${currency}`;
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
