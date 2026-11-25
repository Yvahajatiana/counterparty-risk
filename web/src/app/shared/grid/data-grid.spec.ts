import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { render, screen } from '@testing-library/angular';
import { AgGridAngular } from 'ag-grid-angular';
import { neutralizeCsvCell, safeFileName } from './csv-safety';
import { DataGrid, safeCsvParams } from './data-grid';
import { CrColumn } from './grid-columns';
import { provideGridExport } from './grid-export';

interface Row {
  readonly id: string;
  readonly counterparty: string;
  readonly exposure: number;
  readonly utilisation: number;
  readonly status: 'ok' | 'warn' | 'breach';
}

const ROWS: Row[] = [
  { id: '1', counterparty: 'Alpha', exposure: 12_300_000, utilisation: 0.5, status: 'ok' },
  {
    id: '2',
    counterparty: '=HYPERLINK("http://evil.invalid","x")',
    exposure: 1e6,
    utilisation: 0.85,
    status: 'warn',
  },
  {
    id: '3',
    counterparty: '<img src=x onerror=alert(1)>',
    exposure: 2e6,
    utilisation: 1.04,
    status: 'breach',
  },
];

const COLUMNS: CrColumn<Row>[] = [
  { field: 'counterparty', header: 'Counterparty', type: 'text', pinned: true },
  { field: 'exposure', header: 'Exposure', type: 'money' },
  { field: 'utilisation', header: 'Utilisation', type: 'percent', thresholds: true },
  { field: 'status', header: 'Status', type: 'status' },
];

async function renderGrid(mode: 'mock' | 'server' = 'mock') {
  const result = await render(DataGrid<Row>, {
    inputs: {
      title: 'Exposure',
      level: 'AfterCollateral',
      rows: ROWS,
      columns: COLUMNS,
      getRowId: (p: { data: Row }) => p.data.id,
      exportId: { widgetId: 'W4', tableId: 'limits' },
      asOfDate: '2026-11-24',
    },
    providers: [provideHttpClient(), provideHttpClientTesting(), provideGridExport({ mode })],
  });
  await screen.findByText('Alpha');
  const api = result.fixture.debugElement.query(By.directive(AgGridAngular))
    .componentInstance as AgGridAngular<Row>;
  return { ...result, api: api.api };
}

function cellOf(container: Element, rowId: string, colId: string): HTMLElement {
  const cell = container.querySelector<HTMLElement>(`[row-id="${rowId}"] [col-id="${colId}"]`);
  if (!cell) {
    throw new Error(`cell ${rowId}/${colId} not found`);
  }
  return cell;
}

describe('CSV formula neutralisation', () => {
  it('prefixes cells starting with a formula trigger', () => {
    expect(neutralizeCsvCell('=HYPERLINK("x")')).toBe(`'=HYPERLINK("x")`);
    for (const trigger of ['+', '-', '@', '\t', '\r']) {
      expect(neutralizeCsvCell(`${trigger}1`)).toBe(`'${trigger}1`);
    }
    expect(neutralizeCsvCell('Alpha')).toBe('Alpha');
    expect(neutralizeCsvCell(-12.5)).toBe('-12.5');
    expect(neutralizeCsvCell(null)).toBe('');
  });

  it('builds safe file names', () => {
    expect(safeFileName('../a b?.csv')).toBe('.._a_b_.csv');
  });
});

describe('DataGrid', () => {
  it('shows the exposure level in the title', async () => {
    await renderGrid();
    expect(screen.getByRole('heading', { name: 'Exposure - After collateral' })).toBeTruthy();
  });

  it('formats amounts in millions, right-aligned with tabular figures', async () => {
    const { container } = await renderGrid();
    const cell = cellOf(container, '1', 'exposure');
    expect(cell.textContent?.trim()).toBe('12.3 m');
    expect(cell.classList).toContain('cr-numeric');
  });

  it('marks 85 % as warning and 104 % as breach, with a non-chromatic marker', async () => {
    const { container } = await renderGrid();
    await vi.waitFor(() =>
      expect(cellOf(container, '1', 'utilisation').textContent).toContain('%'),
    );
    const ok = cellOf(container, '1', 'utilisation');
    const warn = cellOf(container, '2', 'utilisation');
    const breach = cellOf(container, '3', 'utilisation');

    expect(ok.classList).toContain('cr-status-ok');
    expect(ok.textContent?.trim()).toBe('50.0%');
    expect(warn.classList).toContain('cr-status-warn');
    expect(warn.textContent).toContain('▲ Warning');
    expect(breach.classList).toContain('cr-status-breach');
    expect(breach.textContent).toContain('■ Breach');
  });

  it('renders a status column as shape and word', async () => {
    const { container } = await renderGrid();
    await vi.waitFor(() =>
      expect(cellOf(container, '3', 'status').textContent).toContain('Breach'),
    );
    expect(cellOf(container, '3', 'status').classList).toContain('cr-status-breach');
    expect(cellOf(container, '1', 'status').textContent?.trim()).toBe('● OK');
  });

  it('renders counterparty names as text (Should_Render_Counterparty_Name_As_Text)', async () => {
    const { container } = await renderGrid();
    expect(container.querySelector('.ag-root img')).toBeNull();
    expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeTruthy();
  });

  it('neutralises formulas in the client CSV fallback', async () => {
    const { api } = await renderGrid();
    const csv = api.getDataAsCsv(safeCsvParams('limits.csv')) ?? '';
    expect(csv).toContain(`"'=HYPERLINK(""http://evil.invalid"",""x"")"`);
    expect(csv).toContain('"12300000"');
  });

  it('exports CSV in the browser in mock mode and disables Excel', async () => {
    const { api } = await renderGrid('mock');
    const exportCsv = vi.spyOn(api, 'exportDataAsCsv').mockImplementation(() => undefined);

    screen.getByRole('button', { name: 'Export CSV' }).click();
    await vi.waitFor(() => expect(exportCsv).toHaveBeenCalled());

    const params = exportCsv.mock.calls[0]?.[0];
    expect(params?.fileName).toBe('limits.csv');
    expect(params?.processCellCallback).toBeTypeOf('function');
    expect(
      (screen.getByRole('button', { name: 'Export Excel' }) as HTMLButtonElement).disabled,
    ).toBe(true);
    TestBed.inject(HttpTestingController).verify();
  });

  it('asks the export API for Excel with the current filters in server mode', async () => {
    const createUrl = vi.fn(() => 'blob:test');
    const revokeUrl = vi.fn();
    vi.stubGlobal(
      'URL',
      Object.assign(URL, { createObjectURL: createUrl, revokeObjectURL: revokeUrl }),
    );
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);
    const { api } = await renderGrid('server');
    api.setFilterModel({ counterparty: { filterType: 'text', type: 'contains', filter: 'Al' } });

    screen.getByRole('button', { name: 'Export Excel' }).click();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v0/exports');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toMatchObject({
      widgetId: 'W4',
      tableId: 'limits',
      format: 'xlsx',
      asOfDate: '2026-11-24',
      filters: { counterparty: { filter: 'Al' } },
    });
    request.flush(new Blob(['x']));
    await vi.waitFor(() => expect(click).toHaveBeenCalled());
    expect(createUrl).toHaveBeenCalled();
    expect(revokeUrl).toHaveBeenCalledWith('blob:test');
    vi.unstubAllGlobals();
    click.mockRestore();
  });
});
