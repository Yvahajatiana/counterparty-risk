import { titleWithLevel } from '../measures/measures';
import { ChartRuleError } from './chart-rules';
import {
  CommonChartInput,
  horizontalBarChartSpec,
  lineChartSpec,
  LineChartInput,
  paretoChartSpec,
  stackedBarChartSpec,
  waterfallSpec,
} from './chart-specs';
import { CrSeries } from './chart.models';

const common: CommonChartInput = {
  heading: 'Exposure',
  level: 'AfterCollateral',
  description: '',
  height: 320,
  currency: 'EUR',
};

function line(series: CrSeries[], overrides: Partial<LineChartInput> = {}): LineChartInput {
  return {
    ...common,
    series,
    unit: 'money',
    xKind: 'category',
    xTitle: '',
    rebase100: false,
    limits: [],
    events: [],
    ...overrides,
  };
}

const pfe: CrSeries = {
  id: 'pfe',
  name: 'PFE',
  family: 'Economic',
  points: [
    ['1Y', 10e6],
    ['2Y', 12e6],
  ],
};
const ead: CrSeries = {
  id: 'ead',
  name: 'EAD',
  family: 'Regulatory',
  points: [
    ['1Y', 8e6],
    ['2Y', 9e6],
  ],
};

function ruleCode(build: () => unknown): string | undefined {
  try {
    build();
  } catch (error) {
    if (error instanceof ChartRuleError) {
      return error.code;
    }
    throw error;
  }
  return undefined;
}

describe('exposure level in titles', () => {
  it('appends the exposure level to the title', () => {
    expect(titleWithLevel('Exposure', 'AfterCollateral')).toBe('Exposure - After collateral');
    expect(titleWithLevel('CCR', { from: 'GrossBeforeCrm', to: 'AfterCollateral' })).toBe(
      'CCR - Gross exposure before CRM to After collateral',
    );
    expect(titleWithLevel('Utilisation', 'NotAnExposure')).toBe('Utilisation');
  });

  it('puts the level in the spec title', () => {
    expect(lineChartSpec(line([pfe])).title).toBe('Exposure - After collateral');
  });

  it('refuses an amount chart without exposure level', () => {
    expect(ruleCode(() => lineChartSpec(line([pfe], { level: 'NotAnExposure' })))).toBe(
      'chart.missing_exposure_level',
    );
    expect(() =>
      lineChartSpec(line([{ ...pfe, unit: 'bps' }], { level: 'NotAnExposure', unit: 'bps' })),
    ).not.toThrow();
  });
});

describe('no mixing of measure families', () => {
  it('refuses an economic and a regulatory series in the same chart', () => {
    expect(ruleCode(() => lineChartSpec(line([pfe, ead])))).toBe('chart.mixed_families');
    expect(ruleCode(() => stackedBarChartSpec({ ...common, ...barInput([pfe, ead]) }))).toBe(
      'chart.mixed_families',
    );
  });

  it('refuses a waterfall mixing economic and regulatory steps', () => {
    expect(
      ruleCode(() =>
        waterfallSpec({
          ...common,
          steps: [
            { label: 'PFE', value: 1, kind: 'start', family: 'Economic' },
            { label: 'EAD', value: 0, kind: 'total', family: 'Regulatory' },
          ],
        }),
      ),
    ).toBe('chart.mixed_families');
  });

  it('accepts current exposure with regulatory (gross MtM to RC) and accounting with regulatory', () => {
    expect(() => lineChartSpec(line([{ ...pfe, family: 'CurrentExposure' }, ead]))).not.toThrow();
    expect(() => lineChartSpec(line([{ ...pfe, family: 'Accounting' }, ead]))).not.toThrow();
  });

  it('refuses a published benchmark with recomputed figures', () => {
    const benchmark: CrSeries = { ...ead, id: 'p3', origin: 'PublishedBenchmark' };
    expect(ruleCode(() => lineChartSpec(line([ead, benchmark])))).toBe('chart.mixed_origins');
  });
});

describe('single Y axis', () => {
  const utilisation: CrSeries = {
    id: 'util',
    name: 'Utilisation',
    family: 'Regulatory',
    unit: 'percent',
    points: [
      ['1Y', 0.5],
      ['2Y', 0.6],
    ],
  };

  it('requires rebase100 for series of different units', () => {
    expect(ruleCode(() => lineChartSpec(line([ead, utilisation])))).toBe('chart.mixed_units');
  });

  it('rebases each series to 100 on its first point', () => {
    const spec = lineChartSpec(line([ead, utilisation], { rebase100: true }));
    expect(spec.unit).toBe('index');
    expect(spec.series[0]?.points.map((p) => p.y)).toEqual([100, 112.5]);
    expect(spec.series[1]?.points[1]?.y).toBeCloseTo(120);
  });

  it('refuses limit lines on a rebased chart', () => {
    expect(
      ruleCode(() =>
        lineChartSpec(
          line([ead, utilisation], { rebase100: true, limits: [{ value: 1, kind: 'limit' }] }),
        ),
      ),
    ).toBe('chart.limits_with_rebase');
  });
});

function barInput(series: CrSeries[]) {
  return {
    series,
    unit: 'money' as const,
    orientation: 'column' as const,
    xTitle: '',
    events: [],
    stacking: 'normal' as const,
  };
}

describe('chart specs', () => {
  it('aligns series on the union of categories and sorts date axes', () => {
    const spec = lineChartSpec(
      line(
        [
          {
            ...ead,
            points: [
              ['2026-02-28', 2],
              ['2026-01-31', 1],
            ],
          },
        ],
        { xKind: 'date' },
      ),
    );
    expect(spec.series[0]?.points.map((p) => p.y)).toEqual([1, 2]);
    expect(spec.table.rows.map((r) => r[0])).toEqual(['2026-01-31', '2026-02-28']);
  });

  it('refuses a non ISO date on a date axis', () => {
    expect(
      ruleCode(() =>
        lineChartSpec(line([{ ...ead, points: [['31/01/2026', 1]] }], { xKind: 'date' })),
      ),
    ).toBe('chart.invalid_date');
  });

  it('computes running totals of a waterfall for the data table', () => {
    const spec = waterfallSpec({
      ...common,
      steps: [
        { label: 'Gross', value: 500e6, kind: 'start', family: 'CurrentExposure' },
        { label: 'Netting', value: -200e6, kind: 'delta', family: 'CurrentExposure' },
        { label: 'After netting', value: 0, kind: 'subtotal', family: 'CurrentExposure' },
        { label: 'Collateral', value: -100e6, kind: 'delta', family: 'CurrentExposure' },
        { label: 'RC', value: 0, kind: 'total', family: 'Regulatory' },
      ],
    });
    expect(spec.table.rows.map((r) => r[1])).toEqual([
      '500.0 m EUR',
      '-200.0 m EUR',
      '300.0 m EUR',
      '-100.0 m EUR',
      '200.0 m EUR',
    ]);
  });

  it('refuses a start value that is not the first step of a waterfall', () => {
    expect(
      ruleCode(() =>
        waterfallSpec({
          ...common,
          steps: [
            { label: 'A', value: 500, kind: 'start', family: 'CurrentExposure' },
            { label: 'B', value: -100, kind: 'delta', family: 'CurrentExposure' },
            { label: 'C', value: 300, kind: 'start', family: 'CurrentExposure' },
            { label: 'D', value: 0, kind: 'total', family: 'CurrentExposure' },
          ],
        }),
      ),
    ).toBe('chart.waterfall_start');
  });

  it('colours horizontal bars by threshold status', () => {
    const spec = horizontalBarChartSpec({
      ...common,
      level: 'NotAnExposure',
      family: 'Economic',
      seriesName: 'Utilisation',
      unit: 'percent',
      thresholds: { warn: 0.8, breach: 1 },
      items: [
        { label: 'A', value: 0.5 },
        { label: 'B', value: 0.85 },
        { label: 'C', value: 1.04 },
      ],
    });
    expect(spec.series[0]?.points.map((p) => p.className)).toEqual([
      'cr-point-ok',
      'cr-point-warn',
      'cr-point-breach',
    ]);
    expect(spec.limits.map((l) => l.kind)).toEqual(['warn', 'breach']);
  });

  it('builds a Pareto of the top N on a single percentage axis', () => {
    const spec = paretoChartSpec({
      ...common,
      family: 'Regulatory',
      seriesName: 'EAD',
      top: 2,
      items: [
        { label: 'C', value: 10 },
        { label: 'A', value: 50 },
        { label: 'B', value: 40 },
      ],
    });
    expect(spec.unit).toBe('percent');
    expect(spec.yMax).toBe(1);
    expect(spec.categories).toEqual(['A', 'B']);
    expect(spec.series.map((s) => s.mark)).toEqual(['column', 'line']);
    expect(spec.series[1]?.points.map((p) => p.y)).toEqual([0.5, 0.9]);
  });
});
