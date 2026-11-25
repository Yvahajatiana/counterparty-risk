import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import type { ICellRendererAngularComp } from 'ag-grid-angular';
import type { ICellRendererParams } from 'ag-grid-community';
import { thresholdStatus, Thresholds, ThresholdStatus } from '../measures/thresholds';

// Cell renderers are Angular components only: a renderer never returns an HTML string.

/** Non-chromatic marker of a status, for colour-blind users (shape + word). */
export const STATUS_TEXT: Readonly<Record<ThresholdStatus, string>> = {
  ok: '● OK',
  warn: '▲ Warning',
  breach: '■ Breach',
};

export interface ThresholdCellParams {
  readonly thresholds: Thresholds;
}

/** Formatted value followed by its threshold marker when warning or breach. */
@Component({
  selector: 'app-threshold-cell',
  template: `{{ text() }}
    @if (status(); as s) {
      @if (s !== 'ok') {
        <span class="cr-status-tag">{{ marker(s) }}</span>
      }
    }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThresholdCell implements ICellRendererAngularComp {
  protected readonly text = signal('');
  protected readonly status = signal<ThresholdStatus | null>(null);

  agInit(params: ICellRendererParams & ThresholdCellParams): void {
    this.refresh(params);
  }

  refresh(params: ICellRendererParams & ThresholdCellParams): boolean {
    this.text.set(params.valueFormatted ?? String(params.value ?? ''));
    this.status.set(
      typeof params.value === 'number' ? thresholdStatus(params.value, params.thresholds) : null,
    );
    return true;
  }

  protected marker(status: ThresholdStatus): string {
    return STATUS_TEXT[status];
  }
}

function isStatus(value: unknown): value is ThresholdStatus {
  return value === 'ok' || value === 'warn' || value === 'breach';
}

/** Status column ('ok' | 'warn' | 'breach'), shown as shape + word. */
@Component({
  selector: 'app-status-cell',
  template: `{{ text() }}`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusCell implements ICellRendererAngularComp {
  protected readonly text = signal('');

  agInit(params: ICellRendererParams): void {
    this.refresh(params);
  }

  refresh(params: ICellRendererParams): boolean {
    this.text.set(isStatus(params.value) ? STATUS_TEXT[params.value] : String(params.value ?? ''));
    return true;
  }
}
