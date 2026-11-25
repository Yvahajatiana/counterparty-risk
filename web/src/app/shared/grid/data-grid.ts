import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CSP_NONCE,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { AgGridAngular } from 'ag-grid-angular';
import {
  CellStyleModule,
  ClientSideRowModelModule,
  ColDef,
  ColTypeDef,
  ColumnApiModule,
  CsvExportModule,
  CsvExportParams,
  DateFilterModule,
  GetRowIdFunc,
  ModuleRegistry,
  NumberFilterModule,
  PaginationModule,
  RowClassRules,
  RowStyleModule,
  TextFilterModule,
  themeQuartz,
  TooltipModule,
  ValidationModule,
} from 'ag-grid-community';
import { DisplayLevel, titleWithLevel } from '../measures/measures';
import { ThresholdStatus } from '../measures/thresholds';
import { neutralizeCsvCell, safeFileName } from './csv-safety';
import { COLUMN_TYPES, CrColumn, toColDefs } from './grid-columns';
import { ExportFormat, GridExportId, GridExportService } from './grid-export';

// AG Grid Community modules, registered explicitly (no AllCommunityModule, never Enterprise).
ModuleRegistry.registerModules([
  ClientSideRowModelModule,
  ColumnApiModule,
  TextFilterModule,
  NumberFilterModule,
  DateFilterModule,
  PaginationModule,
  TooltipModule,
  CellStyleModule,
  RowStyleModule,
  CsvExportModule,
  // Explicit error messages for misconfigured grids, development builds only.
  ...(typeof ngDevMode !== 'undefined' && ngDevMode ? [ValidationModule] : []),
]);

/** AG Grid theme bound to the design tokens, so light and dark modes follow the app theme. */
const GRID_THEME = themeQuartz.withParams({
  fontFamily: 'var(--cr-font-family)',
  fontSize: 13,
  rowHeight: 28, // --cr-grid-row-height
  headerHeight: 32,
  backgroundColor: 'var(--cr-color-surface)',
  foregroundColor: 'var(--cr-color-text)',
  headerBackgroundColor: 'var(--cr-color-surface-alt)',
  borderColor: 'var(--cr-color-border)',
  accentColor: 'var(--cr-color-primary)',
});

/** Client CSV fallback (mock mode only): raw values, formula triggers neutralised. */
export function safeCsvParams(fileName: string): CsvExportParams {
  return {
    fileName: safeFileName(fileName),
    processCellCallback: (p) => neutralizeCsvCell(p.value),
    processHeaderCallback: (p) =>
      neutralizeCsvCell(p.column.getColDef().headerName ?? p.column.getColId()),
  };
}

let nextId = 0;

/**
 * AG Grid Community wrapper for risk tables: title with exposure level, typed columns, sort,
 * filters, pinned counterparty column, client pagination, threshold colour code and export.
 * Risk tables have few columns: column virtualisation is off so assistive technologies see them all.
 * The theme stylesheets injected by AG Grid carry the per-request CSP nonce (ngCspNonce).
 */
@Component({
  selector: 'app-data-grid',
  imports: [AgGridAngular, MatButtonModule],
  template: `
    <div class="cr-grid-toolbar">
      <h2 class="cr-section-title" [id]="titleId">{{ fullTitle() }}</h2>
      @if (exportId()) {
        <div class="cr-grid-actions">
          <button mat-stroked-button type="button" [disabled]="busy()" (click)="export('csv')">
            Export CSV
          </button>
          <button
            mat-stroked-button
            type="button"
            [disabled]="busy() || !xlsxAvailable"
            [attr.title]="xlsxAvailable ? null : 'Excel export is produced by the export API'"
            (click)="export('xlsx')"
          >
            Export Excel
          </button>
        </div>
      }
    </div>
    @if (exportFailed()) {
      <p role="alert" class="cr-text-muted">The export failed. Please retry later.</p>
    }
    <ag-grid-angular
      class="block min-h-0 flex-1"
      [theme]="theme"
      [styleNonce]="nonce"
      [rowData]="rowData()"
      [columnDefs]="columnDefs()"
      [columnTypes]="columnTypes"
      [defaultColDef]="defaultColDef"
      [getRowId]="getRowId()"
      [rowClassRules]="rowClassRules()"
      [pagination]="true"
      [paginationPageSize]="pageSize()"
      [paginationPageSizeSelector]="pageSizes"
      [tooltipShowDelay]="500"
      [enableCellTextSelection]="true"
      [suppressColumnVirtualisation]="true"
      [attr.aria-labelledby]="titleId"
    />
  `,
  // Height set through the CSSOM (host binding), allowed by the CSP unlike a style attribute.
  host: { class: 'flex flex-col', '[style.height.px]': 'height()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataGrid<TRow> {
  private readonly exports = inject(GridExportService);
  protected readonly nonce = inject(CSP_NONCE, { optional: true }) ?? undefined;
  protected readonly theme = GRID_THEME;
  protected readonly columnTypes: Record<string, ColTypeDef> = { ...COLUMN_TYPES };
  protected readonly pageSizes = [20, 50, 100];
  protected readonly titleId = `cr-grid-${nextId++}`;
  protected readonly xlsxAvailable = this.exports.supports('xlsx');
  protected readonly defaultColDef: ColDef = {
    sortable: true,
    resizable: true,
    filter: true,
    flex: 1,
    minWidth: 110,
  };

  readonly rows = input.required<readonly TRow[] | undefined>();
  readonly columns = input.required<readonly CrColumn<TRow>[]>();
  /** Business title; the exposure level is appended: "<title> - <level>". */
  readonly title = input.required<string>();
  /** Exposure level of the amounts, or 'NotAnExposure'. */
  readonly level = input.required<DisplayLevel>();
  /** Enables the export buttons (API #31). */
  readonly exportId = input<GridExportId | undefined>(undefined);
  readonly asOfDate = input<string | null>(null);
  readonly height = input(420);
  readonly pageSize = input(20);
  readonly getRowId = input<GetRowIdFunc<TRow> | undefined>(undefined);
  /** Row marker (left border) for warning / breach rows. */
  readonly rowStatus = input<((row: TRow) => ThresholdStatus | null) | undefined>(undefined);

  protected readonly fullTitle = computed(() => titleWithLevel(this.title(), this.level()));
  protected readonly columnDefs = computed(() => toColDefs(this.columns()));
  protected readonly rowData = computed(() => {
    const rows = this.rows();
    return rows ? [...rows] : null;
  });
  protected readonly rowClassRules = computed<RowClassRules<TRow> | undefined>(() => {
    const status = this.rowStatus();
    if (!status) {
      return undefined;
    }
    return {
      'cr-row-breach': (p) => !!p.data && status(p.data) === 'breach',
      'cr-row-warn': (p) => !!p.data && status(p.data) === 'warn',
    };
  });

  protected readonly busy = signal(false);
  protected readonly exportFailed = signal(false);
  private readonly grid = viewChild.required(AgGridAngular<TRow>);

  protected async export(format: ExportFormat): Promise<void> {
    const api = this.grid().api;
    const id = this.exportId();
    if (!api || !id) {
      return;
    }
    this.busy.set(true);
    this.exportFailed.set(false);
    try {
      await this.exports.export(
        {
          ...id,
          format,
          asOfDate: this.asOfDate(),
          filters: api.getFilterModel(),
          sort: api
            .getColumnState()
            .flatMap((c) => (c.sort ? [{ colId: c.colId, sort: c.sort }] : [])),
        },
        () => api.exportDataAsCsv(safeCsvParams(`${id.tableId}.csv`)),
      );
    } catch {
      this.exportFailed.set(true);
    } finally {
      this.busy.set(false);
    }
  }
}
