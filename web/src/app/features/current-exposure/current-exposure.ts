import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import type { ColDef, GetRowIdFunc } from 'ag-grid-community';
import type { Options as ChartOptions } from 'highcharts';
import { currentExposureResource } from '../../core/api/dashboard-api';
import { CurrentExposureRow } from '../../core/api/dashboard.models';
import { widgetNav } from '../../layout/navigation';
import { Chart } from '../../shared/charts/chart';
import { toMillions, millionsCell, formatPercent, MillionsPipe } from '../../shared/format/amount';
import { DataGrid } from '../../shared/grid/data-grid';
import { LoadState } from '../../shared/ui/load-state';
import { WidgetHeader } from '../../shared/ui/widget-header';

/**
 * Current exposure, from gross to net. CCR (waterfall gross MtM -> netting -> collateral -> RC) and financing
 * (drawn -> undrawn x CCF -> EAD) are two natures: they are shown in two distinct charts, never stacked.
 */
@Component({
  selector: 'app-current-exposure',
  imports: [Chart, DataGrid, LoadState, MillionsPipe, WidgetHeader],
  templateUrl: './current-exposure.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CurrentExposure {
  protected readonly widget = widgetNav('current-exposure');
  protected readonly data = currentExposureResource();

  protected readonly ccrChart = computed<ChartOptions | null>(() => {
    const ccr = this.data.value()?.ccr;
    if (!ccr) {
      return null;
    }
    return {
      chart: { type: 'waterfall' },
      xAxis: { type: 'category' },
      yAxis: { title: { text: 'EUR m' } },
      legend: { enabled: false },
      tooltip: { valueSuffix: ' m EUR', valueDecimals: 1 },
      series: [
        {
          type: 'waterfall',
          name: 'CCR exposure',
          data: [
            { name: 'Gross positive MtM (before netting)', y: toMillions(ccr.grossPositiveMtm) },
            {
              name: 'Netting benefit',
              y: -toMillions(ccr.nettingBenefit),
              className: 'cr-point-reduction',
            },
            {
              name: 'Current exposure (after netting)',
              isIntermediateSum: true,
              className: 'cr-point-sum',
            },
            {
              name: 'Collateral received (net of haircuts)',
              y: -toMillions(ccr.collateralReceivedNet),
              className: 'cr-point-reduction',
            },
            { name: 'Replacement cost (after collateral)', isSum: true, className: 'cr-point-sum' },
          ],
        },
      ],
    };
  });

  protected readonly financingChart = computed<ChartOptions | null>(() => {
    const f = this.data.value()?.financing;
    if (!f) {
      return null;
    }
    return {
      chart: { type: 'waterfall' },
      xAxis: { type: 'category' },
      yAxis: { title: { text: 'EUR m' } },
      legend: { enabled: false },
      tooltip: { valueSuffix: ' m EUR', valueDecimals: 1 },
      series: [
        {
          type: 'waterfall',
          name: 'Financing exposure',
          data: [
            { name: 'Drawn', y: toMillions(f.drawn) },
            {
              name: `Off-balance EAD (CCF ${formatPercent(f.weightedCcf)} x undrawn)`,
              y: toMillions(f.offBalanceSheetEad),
            },
            { name: 'Financing EAD', isSum: true, className: 'cr-point-sum' },
          ],
        },
      ],
    };
  });

  protected readonly columns: ColDef<CurrentExposureRow>[] = [
    { field: 'counterparty', headerName: 'Counterparty', minWidth: 190, pinned: 'left' },
    { field: 'businessLine', headerName: 'Business line' },
    { field: 'collateralStatus', headerName: 'Collateral status', minWidth: 150 },
    { field: 'nettingSets', headerName: 'Netting sets', type: 'rightAligned', maxWidth: 120 },
    { field: 'grossNotional', headerName: 'Gross notional', ...millionsCell },
    {
      field: 'grossPositiveMtm',
      headerName: 'Gross MtM+ (before netting)',
      minWidth: 170,
      ...millionsCell,
    },
    {
      field: 'currentExposure',
      headerName: 'Current exposure (after netting)',
      minWidth: 190,
      ...millionsCell,
    },
    {
      field: 'collateralReceivedNet',
      headerName: 'Collateral received (net)',
      minWidth: 170,
      ...millionsCell,
    },
    {
      field: 'replacementCost',
      headerName: 'RC (after collateral)',
      minWidth: 160,
      sort: 'desc',
      ...millionsCell,
    },
  ];

  protected readonly rowId: GetRowIdFunc<CurrentExposureRow> = (p) => p.data.counterpartyId;
}
