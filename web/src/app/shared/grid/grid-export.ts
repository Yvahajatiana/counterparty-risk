import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { inject, Injectable, InjectionToken } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { safeFileName } from './csv-safety';

export type ExportFormat = 'csv' | 'xlsx';

/** Table identity sent to the export API (#31), which checks the export rights (ABAC, R5). */
export interface GridExportId {
  readonly widgetId: string;
  readonly tableId: string;
}

/** Body of POST /api/v0/exports (#31). */
export interface GridExportRequest extends GridExportId {
  readonly format: ExportFormat;
  readonly asOfDate: string | null;
  /** Current AG Grid filter and sort models; validated against allowlists by the API. */
  readonly filters: Readonly<Record<string, unknown>>;
  readonly sort: readonly { readonly colId: string; readonly sort: 'asc' | 'desc' }[];
}

export interface GridExportConfig {
  /**
   * 'server': exports are produced, audited and formula-neutralised by the API (#31).
   * 'mock': no export API yet; CSV is produced in the browser (neutralised), Excel is unavailable
   * (AG Grid Excel export is an Enterprise feature and is never used).
   */
  readonly mode: 'server' | 'mock';
  readonly endpoint: string;
}

// Mock until the export endpoint of #31 is merged; switch with provideGridExport({ mode: 'server' }).
export const GRID_EXPORT_CONFIG = new InjectionToken<GridExportConfig>('GRID_EXPORT_CONFIG', {
  factory: () => ({ mode: 'mock', endpoint: '/api/v0/exports' }),
});

export function provideGridExport(config: Partial<GridExportConfig>) {
  return {
    provide: GRID_EXPORT_CONFIG,
    useValue: { mode: 'mock', endpoint: '/api/v0/exports', ...config },
  };
}

@Injectable({ providedIn: 'root' })
export class GridExportService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly config = inject(GRID_EXPORT_CONFIG);

  supports(format: ExportFormat): boolean {
    return this.config.mode === 'server' || format === 'csv';
  }

  /** Server export, or the neutralised client CSV in mock mode. */
  async export(request: GridExportRequest, clientCsv: () => void): Promise<void> {
    if (this.config.mode === 'mock') {
      if (request.format === 'csv') {
        clientCsv();
      }
      return;
    }
    const response = await firstValueFrom(
      this.http.post(this.config.endpoint, request, { observe: 'response', responseType: 'blob' }),
    );
    // A 202 (asynchronous export with a download link) is handled with #31.
    if (response.status !== 200 || !response.body) {
      throw new Error('Export not available.');
    }
    // The file name is built locally, never taken from a response header.
    const name = safeFileName(
      `${request.tableId}-${request.asOfDate ?? 'latest'}.${request.format}`,
    );
    this.download(response.body, name);
  }

  private download(blob: Blob, name: string): void {
    const url = URL.createObjectURL(blob);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
  }
}
