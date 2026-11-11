import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CSP_NONCE,
  inject,
  input,
} from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import {
  AllCommunityModule,
  ColDef,
  GetRowIdFunc,
  ModuleRegistry,
  RowClassRules,
  themeQuartz,
} from 'ag-grid-community';

ModuleRegistry.registerModules([AllCommunityModule]);

/** AG Grid theme bound to the design tokens, so light and dark modes follow the app theme. */
const GRID_THEME = themeQuartz.withParams({
  fontFamily: 'var(--cr-font-family)',
  fontSize: 13,
  rowHeight: 28,
  headerHeight: 32,
  backgroundColor: 'var(--cr-color-surface)',
  foregroundColor: 'var(--cr-color-text)',
  headerBackgroundColor: 'var(--cr-color-surface-alt)',
  borderColor: 'var(--cr-color-border)',
  accentColor: 'var(--cr-color-primary)',
});

/**
 * AG Grid Community wrapper for risk tables. The theme stylesheets injected by AG Grid carry the
 * per-request CSP nonce (read from ngCspNonce on the root element).
 */
@Component({
  selector: 'app-data-grid',
  imports: [AgGridAngular],
  template: `
    <ag-grid-angular
      class="block h-full w-full"
      [theme]="theme"
      [styleNonce]="nonce"
      [rowData]="rowData()"
      [columnDefs]="columns()"
      [defaultColDef]="defaultColDef"
      [getRowId]="getRowId()"
      [rowClassRules]="rowClassRules()"
      [suppressCellFocus]="true"
      [attr.aria-label]="label()"
    />
  `,
  // Height set through the CSSOM (host binding), allowed by the CSP unlike a style attribute.
  host: { class: 'block', '[style.height.px]': 'height()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataGrid<TRow> {
  protected readonly nonce = inject(CSP_NONCE, { optional: true }) ?? undefined;
  protected readonly theme = GRID_THEME;
  protected readonly defaultColDef: ColDef = {
    sortable: true,
    resizable: true,
    flex: 1,
    minWidth: 110,
  };

  readonly rows = input.required<readonly TRow[] | undefined>();
  readonly columns = input.required<ColDef<TRow>[]>();
  readonly label = input.required<string>();
  readonly height = input(360);
  readonly getRowId = input<GetRowIdFunc<TRow> | undefined>(undefined);
  readonly rowClassRules = input<RowClassRules<TRow> | undefined>(undefined);

  protected readonly rowData = computed(() => {
    const rows = this.rows();
    return rows ? [...rows] : null;
  });
}
