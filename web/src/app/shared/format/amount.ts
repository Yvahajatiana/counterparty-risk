import { Pipe, PipeTransform } from '@angular/core';

const LOCALE = 'en-GB';

const millions = new Intl.NumberFormat(LOCALE, {
  maximumFractionDigits: 1,
  minimumFractionDigits: 1,
});
const integer = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 0 });
const percent = new Intl.NumberFormat(LOCALE, { style: 'percent', maximumFractionDigits: 1 });

/** Amount in millions of the reporting currency, e.g. "1,234.5 m". */
export function formatMillions(value: number | null | undefined): string {
  return value == null ? '-' : `${millions.format(value / 1_000_000)} m`;
}

/** Chart value in millions, rounded to one decimal (axes and tooltips show "m"). */
export function toMillions(value: number): number {
  return Math.round(value / 100_000) / 10;
}

/** AG Grid column defaults for an amount shown in millions. */
export const millionsCell = {
  valueFormatter: (p: { value: number }) => formatMillions(p.value),
  type: 'rightAligned',
};

export function formatInteger(value: number | null | undefined): string {
  return value == null ? '-' : integer.format(value);
}

export function formatPercent(value: number | null | undefined): string {
  return value == null ? '-' : percent.format(value);
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

@Pipe({ name: 'pct' })
export class PercentPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return formatPercent(value);
  }
}

@Pipe({ name: 'asOf' })
export class AsOfDatePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatDate(value);
  }
}
