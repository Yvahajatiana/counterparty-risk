import type { ColDef, ColTypeDef, ValueFormatterParams } from 'ag-grid-community';
import {
  formatBps,
  formatDate,
  formatInteger,
  formatMillions,
  formatPercent,
} from '../format/amount';
import { parseIsoDate } from '../format/dates';
import { DEFAULT_THRESHOLDS, thresholdStatus, Thresholds } from '../measures/thresholds';
import { StatusCell, ThresholdCell, ThresholdCellParams } from './grid-cells';

/**
 * Column types of the risk tables. Amounts ('money') are in millions of the reporting currency,
 * 'percent' values are ratios (0.85), 'bps' values are basis points.
 */
export type CrColumnType =
  'money' | 'percent' | 'bps' | 'integer' | 'date' | 'lei' | 'text' | 'status';

export interface CrColumn<T> {
  readonly field: keyof T & string;
  readonly header: string;
  readonly type: CrColumnType;
  /** Frozen on the left (counterparty column). */
  readonly pinned?: boolean;
  /** Warning / breach colour code with a text marker; true uses the EDB defaults (80 % / 100 %). */
  readonly thresholds?: Thresholds | true;
  readonly sort?: 'asc' | 'desc';
  readonly minWidth?: number;
  readonly maxWidth?: number;
}

type Formatter = (value: number | null | undefined) => string;

const NUMERIC_FORMATTERS: Partial<Record<CrColumnType, Formatter>> = {
  money: formatMillions,
  percent: formatPercent,
  bps: formatBps,
  integer: formatInteger,
};

function numericType(format: Formatter): ColTypeDef {
  return {
    cellClass: ['ag-right-aligned-cell', 'cr-numeric'],
    headerClass: 'ag-right-aligned-header',
    filter: 'agNumberColumnFilter',
    valueFormatter: (p: ValueFormatterParams) =>
      typeof p.value === 'number' ? format(p.value) : '',
  };
}

/** Compares ISO dates (yyyy-MM-dd) for the date filter. */
function compareIsoDate(filter: Date, cell: string | null): number {
  const value = parseIsoDate(cell);
  if (Number.isNaN(value)) {
    return -1;
  }
  const target = Date.UTC(filter.getFullYear(), filter.getMonth(), filter.getDate());
  return value === target ? 0 : value < target ? -1 : 1;
}

/** AG Grid column types (gridOptions.columnTypes), keyed by CrColumnType. */
export const COLUMN_TYPES: Readonly<Record<CrColumnType, ColTypeDef>> = {
  money: numericType(formatMillions),
  percent: numericType(formatPercent),
  bps: numericType(formatBps),
  integer: numericType(formatInteger),
  date: {
    cellClass: 'cr-numeric',
    filter: 'agDateColumnFilter',
    filterParams: { comparator: compareIsoDate },
    valueFormatter: (p: ValueFormatterParams) => formatDate(p.value as string | null),
  },
  lei: {
    cellClass: 'cr-cell-lei',
    filter: 'agTextColumnFilter',
    tooltip: (p) => p.value,
  },
  text: { filter: 'agTextColumnFilter', tooltip: (p) => p.value },
  status: { filter: 'agTextColumnFilter', cellRenderer: StatusCell },
};

function statusClassRules(thresholds: Thresholds): ColDef['cellClassRules'] {
  const is = (status: string) => (p: { value: unknown }) =>
    typeof p.value === 'number' && thresholdStatus(p.value, thresholds) === status;
  return {
    'cr-status-ok': is('ok'),
    'cr-status-warn': is('warn'),
    'cr-status-breach': is('breach'),
  };
}

/** Typed widget columns -> AG Grid column definitions (no HTML string renderer, no inline style). */
export function toColDefs<T>(columns: readonly CrColumn<T>[]): ColDef<T>[] {
  return columns.map((column) => {
    const def: ColDef<T> = {
      field: column.field as unknown as ColDef<T>['field'],
      headerName: column.header,
      headerTooltip: column.header,
      type: column.type,
      pinned: column.pinned ? 'left' : undefined,
      sort: column.sort,
      minWidth: column.minWidth,
      maxWidth: column.maxWidth,
    };
    if (column.type === 'status') {
      def.cellClassRules = {
        'cr-status-ok': (p) => p.value === 'ok',
        'cr-status-warn': (p) => p.value === 'warn',
        'cr-status-breach': (p) => p.value === 'breach',
      };
    } else if (column.thresholds && NUMERIC_FORMATTERS[column.type]) {
      const thresholds = column.thresholds === true ? DEFAULT_THRESHOLDS : column.thresholds;
      const params: ThresholdCellParams = { thresholds };
      def.cellClassRules = statusClassRules(thresholds);
      def.cellRenderer = ThresholdCell;
      def.cellRendererParams = params;
    }
    return def;
  });
}
