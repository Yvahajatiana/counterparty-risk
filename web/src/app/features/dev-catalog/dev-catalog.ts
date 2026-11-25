import { ChangeDetectionStrategy, Component } from '@angular/core';
import {
  CrBarItem,
  CrEventMarker,
  CrSeries,
  WaterfallStep,
} from '../../shared/charts/chart.models';
import { GroupedBarChart } from '../../shared/charts/grouped-bar-chart';
import { HorizontalBarChart } from '../../shared/charts/horizontal-bar-chart';
import { LineChart } from '../../shared/charts/line-chart';
import { ParetoChart } from '../../shared/charts/pareto-chart';
import { StackedBarChart } from '../../shared/charts/stacked-bar-chart';
import { WaterfallChart } from '../../shared/charts/waterfall-chart';
import { DataGrid } from '../../shared/grid/data-grid';
import { CrColumn } from '../../shared/grid/grid-columns';
import { KpiTileComponent } from '../../shared/ui/kpi-tile/kpi-tile';

// Synthetic mock data only. Names include markup on purpose: it must render as plain text.

interface CatalogRow {
  readonly id: string;
  readonly counterparty: string;
  readonly lei: string;
  readonly exposure: number;
  readonly utilisation: number;
  readonly spread: number;
  readonly asOf: string;
  readonly status: 'ok' | 'warn' | 'breach';
}

const ROWS: readonly CatalogRow[] = [
  {
    id: '1',
    counterparty: 'Alpha Bank <b>SA</b>',
    lei: '529900T8BM49AURSDO55',
    exposure: 125_400_000,
    utilisation: 0.62,
    spread: 85,
    asOf: '2026-11-24',
    status: 'ok',
  },
  {
    id: '2',
    counterparty: '=HYPERLINK("http://example.invalid","Beta")',
    lei: '5493001KJTIIGC8Y1R12',
    exposure: 98_100_000,
    utilisation: 0.85,
    spread: 140,
    asOf: '2026-11-24',
    status: 'warn',
  },
  {
    id: '3',
    counterparty: 'Gamma Capital <img src=x>',
    lei: '213800D1EI4B9WTWWD28',
    exposure: 210_000_000,
    utilisation: 1.04,
    spread: 310,
    asOf: '2026-11-24',
    status: 'breach',
  },
];

const months = ['2026-06-30', '2026-07-31', '2026-08-31', '2026-09-30', '2026-10-31'];

/** Component catalogue (/dev/catalog), development builds only: review and #40 support. */
@Component({
  selector: 'app-dev-catalog',
  imports: [
    DataGrid,
    GroupedBarChart,
    HorizontalBarChart,
    KpiTileComponent,
    LineChart,
    ParetoChart,
    StackedBarChart,
    WaterfallChart,
  ],
  template: `
    <h1 class="cr-page-title">Component catalogue (synthetic data)</h1>
    <div class="grid gap-4 xl:grid-cols-2">
      <section class="cr-card" aria-label="KPI tile">
        <app-kpi-tile
          label="Total EAD"
          [value]="1254000000"
          unit="money"
          level="EAD"
          vision="Regulatory"
          route="/regulatory-ead"
          status="warn"
          [previousDay]="1240000000"
          [previousMonth]="1300000000"
        />
      </section>
      <section class="cr-card" aria-label="Waterfall">
        <app-waterfall-chart
          heading="CCR exposure (EUR m)"
          [level]="{ from: 'GrossBeforeCrm', to: 'AfterCollateral' }"
          [steps]="steps"
        />
      </section>
      <section class="cr-card" aria-label="Lines">
        <app-line-chart
          heading="EE and PFE by horizon (EUR m)"
          level="AfterCollateral"
          [series]="profile"
          xTitle="Projection horizon"
        />
      </section>
      <section class="cr-card" aria-label="Rebased lines">
        <app-line-chart
          heading="EAD vs limit utilisation, rebased"
          level="Ead"
          xKind="date"
          [rebase100]="true"
          [series]="rebased"
          [events]="events"
        />
      </section>
      <section class="cr-card" aria-label="Stacked bars">
        <app-stacked-bar-chart
          heading="SA-CCR EAD by asset class (EUR m)"
          level="Ead"
          [series]="stacked"
        />
      </section>
      <section class="cr-card" aria-label="Stacked bars 100 %">
        <app-stacked-bar-chart
          heading="Share of exposure under CSA"
          level="AfterCollateral"
          stacking="percent"
          [series]="stacked"
        />
      </section>
      <section class="cr-card" aria-label="Grouped bars">
        <app-grouped-bar-chart
          heading="Collateral received vs posted (EUR m)"
          level="AfterCollateral"
          [series]="grouped"
        />
      </section>
      <section class="cr-card" aria-label="Horizontal bars">
        <app-horizontal-bar-chart
          heading="Limit utilisation"
          level="AfterCollateral"
          family="Economic"
          seriesName="Utilisation"
          [items]="utilisation"
        />
      </section>
      <section class="cr-card" aria-label="Pareto">
        <app-pareto-chart
          heading="Top 10 counterparties by EAD"
          level="Ead"
          family="Regulatory"
          seriesName="EAD"
          [items]="concentration"
        />
      </section>
      <section class="cr-card" aria-label="Rule violation">
        <app-line-chart heading="Rejected: PFE with EAD" level="Ead" [series]="mixed" />
      </section>
    </div>
    <section class="cr-card mt-4" aria-label="Grid">
      <app-data-grid
        heading="Counterparties (EUR m)"
        level="AfterCollateral"
        [rows]="rows"
        [columns]="columns"
        [getRowId]="rowId"
        [exportId]="{ widgetId: 'CATALOG', tableId: 'catalog-grid' }"
        asOfDate="2026-11-24"
      />
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevCatalog {
  protected readonly rows = ROWS;
  protected readonly rowId = (p: { data: CatalogRow }) => p.data.id;
  protected readonly columns: CrColumn<CatalogRow>[] = [
    { field: 'counterparty', header: 'Counterparty', type: 'text', pinned: true, minWidth: 220 },
    { field: 'lei', header: 'LEI', type: 'lei', minWidth: 200 },
    { field: 'exposure', header: 'Exposure (after collateral)', type: 'money', sort: 'desc' },
    { field: 'utilisation', header: 'Utilisation', type: 'percent', thresholds: true },
    { field: 'spread', header: 'CDS spread', type: 'bps' },
    { field: 'asOf', header: 'As of', type: 'date' },
    { field: 'status', header: 'Status', type: 'status' },
  ];

  protected readonly steps: WaterfallStep[] = [
    { label: 'Gross positive MtM', value: 500e6, kind: 'start', family: 'CurrentExposure' },
    { label: 'Netting benefit', value: -180e6, kind: 'delta', family: 'CurrentExposure' },
    { label: 'After netting', value: 0, kind: 'subtotal', family: 'CurrentExposure' },
    { label: 'Collateral received', value: -120e6, kind: 'delta', family: 'CurrentExposure' },
    { label: 'Replacement cost', value: 0, kind: 'total', family: 'Regulatory' },
  ];

  protected readonly profile: CrSeries[] = [
    {
      id: 'ee',
      name: 'EE',
      family: 'Economic',
      points: [
        ['1M', 40e6],
        ['1Y', 55e6],
        ['5Y', 48e6],
      ],
    },
    {
      id: 'pfe',
      name: 'PFE 97.5%',
      family: 'Economic',
      dash: 'dash',
      points: [
        ['1M', 70e6],
        ['1Y', 110e6],
        ['5Y', 95e6],
      ],
    },
  ];

  protected readonly rebased: CrSeries[] = [
    {
      id: 'ead',
      name: 'EAD',
      family: 'Regulatory',
      unit: 'money',
      points: months.map((m, i) => [m, 1e9 + i * 3e7] as const),
    },
    {
      id: 'util',
      name: 'Limit utilisation',
      family: 'Regulatory',
      unit: 'percent',
      points: months.map((m, i) => [m, 0.7 + i * 0.04] as const),
    },
  ];

  protected readonly events: CrEventMarker[] = [{ at: '2026-08-31', label: 'Method v2 <i>' }];

  protected readonly stacked: CrSeries[] = ['IR', 'FX', 'Credit'].map((name, i) => ({
    id: name,
    name,
    family: 'Regulatory',
    points: [
      ['Bank A', (i + 1) * 20e6],
      ['Bank B', (3 - i) * 15e6],
    ],
  }));

  protected readonly grouped: CrSeries[] = [
    {
      id: 'received',
      name: 'Received',
      family: 'CurrentExposure',
      points: [
        ['Bank A', 50e6],
        ['Bank B', 30e6],
      ],
    },
    {
      id: 'posted',
      name: 'Posted',
      family: 'CurrentExposure',
      points: [
        ['Bank A', 20e6],
        ['Bank B', 45e6],
      ],
    },
  ];

  protected readonly utilisation: CrBarItem[] = ROWS.map((r) => ({
    label: r.counterparty,
    value: r.utilisation,
  }));

  protected readonly concentration: CrBarItem[] = Array.from({ length: 25 }, (_, i) => ({
    label: `Counterparty ${i + 1}`,
    value: 1e9 / (i + 1),
  }));

  protected readonly mixed: CrSeries[] = [
    { id: 'pfe', name: 'PFE', family: 'Economic', points: [['1Y', 1e6]] },
    { id: 'ead', name: 'EAD', family: 'Regulatory', points: [['1Y', 2e6]] },
  ];
}
