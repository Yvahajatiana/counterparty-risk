import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { currentExposureResource } from '../../core/api/dashboard-api';
import { CurrentExposureRow } from '../../core/api/dashboard.models';
import { widgetNav } from '../../layout/navigation';
import { WaterfallStep } from '../../shared/charts/chart.models';
import { WaterfallChart } from '../../shared/charts/waterfall-chart';
import { formatPercent, MillionsPipe } from '../../shared/format/amount';
import { DataGrid } from '../../shared/grid/data-grid';
import { CrColumn } from '../../shared/grid/grid-columns';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

/**
 * Current exposure, from gross to net. CCR (waterfall gross MtM -> netting -> collateral -> RC) and financing
 * (drawn -> undrawn x CCF -> EAD) are two natures: they are shown in two distinct charts, never stacked.
 */
@Component({
  selector: 'app-current-exposure',
  imports: [DataGrid, LoadState, MillionsPipe, WaterfallChart, WidgetHeader],
  templateUrl: './current-exposure.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CurrentExposure {
  protected readonly widget = widgetNav('current-exposure');
  protected readonly data = currentExposureResource();

  protected readonly ccrSteps = computed<WaterfallStep[]>(() => {
    const ccr = this.data.value()?.ccr;
    if (!ccr) {
      return [];
    }
    return [
      {
        label: 'Gross positive MtM (before netting)',
        value: ccr.grossPositiveMtm,
        kind: 'start',
        family: 'CurrentExposure',
      },
      {
        label: 'Netting benefit',
        value: -ccr.nettingBenefit,
        kind: 'delta',
        family: 'CurrentExposure',
      },
      {
        label: 'Current exposure (after netting)',
        value: 0,
        kind: 'subtotal',
        family: 'CurrentExposure',
      },
      {
        label: 'Collateral received (net of haircuts)',
        value: -ccr.collateralReceivedNet,
        kind: 'delta',
        family: 'CurrentExposure',
      },
      {
        label: 'Replacement cost (after collateral)',
        value: 0,
        kind: 'total',
        family: 'Regulatory',
      },
    ];
  });

  protected readonly financingSteps = computed<WaterfallStep[]>(() => {
    const f = this.data.value()?.financing;
    if (!f) {
      return [];
    }
    return [
      { label: 'Drawn', value: f.drawn, kind: 'start', family: 'Accounting' },
      {
        label: `Off-balance EAD (CCF ${formatPercent(f.weightedCcf)} x undrawn)`,
        value: f.offBalanceSheetEad,
        kind: 'delta',
        family: 'Regulatory',
      },
      { label: 'Financing EAD', value: 0, kind: 'total', family: 'Regulatory' },
    ];
  });

  protected readonly columns: CrColumn<CurrentExposureRow>[] = [
    { field: 'counterparty', header: 'Counterparty', type: 'text', minWidth: 190, pinned: true },
    { field: 'businessLine', header: 'Business line', type: 'text' },
    { field: 'collateralStatus', header: 'Collateral status', type: 'text', minWidth: 150 },
    { field: 'nettingSets', header: 'Netting sets', type: 'integer', maxWidth: 130 },
    { field: 'grossNotional', header: 'Gross notional (volume)', type: 'money', minWidth: 160 },
    {
      field: 'grossPositiveMtm',
      header: 'Gross MtM+ (before netting)',
      type: 'money',
      minWidth: 170,
    },
    {
      field: 'currentExposure',
      header: 'Current exposure (after netting)',
      type: 'money',
      minWidth: 190,
    },
    {
      field: 'collateralReceivedNet',
      header: 'Collateral received (net)',
      type: 'money',
      minWidth: 170,
    },
    {
      field: 'replacementCost',
      header: 'RC (after collateral)',
      type: 'money',
      minWidth: 160,
      sort: 'desc',
    },
  ];

  protected readonly rowId = (p: { data: CurrentExposureRow }) => p.data.counterpartyId;
}
