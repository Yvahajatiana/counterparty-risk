import Highcharts from 'highcharts/esm/highcharts';
import 'highcharts/esm/highcharts-more';
import type { Options, Point, TooltipFormatterCallbackFunction } from 'highcharts';
import {
  CommonChartInput,
  lineChartSpec,
  paretoChartSpec,
  stackedBarChartSpec,
  waterfallSpec,
} from '../chart-specs';
import { ChartSpec } from '../chart.models';
import { formatMoney } from '../../format/amount';
import { escapeText, highchartsAdapter } from './highcharts-adapter';

const common: CommonChartInput = {
  heading: 'Exposure',
  level: 'AfterCollateral',
  description: 'Accessible description',
  height: 320,
  currency: 'EUR',
};

const MALICIOUS = '<img src=x onerror=alert(1)>';

const specs: Record<string, ChartSpec> = {
  line: lineChartSpec({
    ...common,
    series: [
      {
        id: 'a',
        name: MALICIOUS,
        family: 'Economic',
        points: [
          [MALICIOUS, 1e6],
          ['1Y', 2e6],
        ],
      },
      { id: 'b', name: 'PFE', family: 'Economic', points: [['1Y', 3e6]], dash: 'dash' },
    ],
    unit: 'money',
    xKind: 'category',
    xTitle: 'Horizon',
    rebase100: false,
    limits: [{ value: 2e6, kind: 'limit', label: MALICIOUS }],
    events: [{ at: '1Y', label: MALICIOUS }],
  }),
  waterfall: waterfallSpec({
    ...common,
    steps: [
      { label: MALICIOUS, value: 5e6, kind: 'start', family: 'CurrentExposure' },
      { label: 'Netting', value: -2e6, kind: 'delta', family: 'CurrentExposure' },
      { label: 'RC', value: 0, kind: 'total', family: 'Regulatory' },
    ],
  }),
  stacked: stackedBarChartSpec({
    ...common,
    series: [{ id: 'a', name: 'IR', family: 'Regulatory', points: [['Bank', 1e6]] }],
    unit: 'money',
    orientation: 'bar',
    stacking: 'percent',
    xTitle: '',
    events: [],
  }),
  pareto: paretoChartSpec({
    ...common,
    family: 'Regulatory',
    seriesName: 'EAD',
    top: 10,
    items: [{ label: MALICIOUS, value: 1 }],
  }),
};

/** Every useHTML flag anywhere in the options tree. */
function useHtmlFlags(value: unknown, path = 'options'): [string, unknown][] {
  if (!value || typeof value !== 'object') {
    return [];
  }
  return Object.entries(value).flatMap(([key, child]) =>
    key === 'useHTML'
      ? [[`${path}.${key}`, child] as [string, unknown]]
      : useHtmlFlags(child, `${path}.${key}`),
  );
}

describe('Highcharts adapter', () => {
  for (const [name, spec] of Object.entries(specs)) {
    describe(name, () => {
      const options = highchartsAdapter.toOptions(spec);

      it('keeps useHTML false everywhere', () => {
        const flags = useHtmlFlags(options);
        expect(flags.length).toBeGreaterThan(0);
        expect(flags.filter(([, v]) => v !== false)).toEqual([]);
        expect(options.tooltip?.useHTML).toBe(false);
        expect(options.title?.useHTML).toBe(false);
      });

      it('uses styledMode, keeps the licence credits and has a single Y axis', () => {
        expect(options.chart?.styledMode).toBe(true);
        expect(options.credits?.enabled).toBe(true);
        expect(Array.isArray(options.yAxis)).toBe(false);
        expect(Array.isArray(options.xAxis)).toBe(false);
        expect(JSON.stringify(options)).not.toContain('"pie"');
      });

      it('enables the accessibility module with a description', () => {
        expect(options.accessibility?.enabled).toBe(true);
        expect(options.accessibility?.description).toBe('Accessible description');
      });

      it('never passes raw markup to Highcharts', () => {
        expect(JSON.stringify(options)).not.toContain('<img');
      });
    });
  }

  it('escapes markup characters', () => {
    expect(escapeText(`<b a="1">'&'</b>`)).toBe(
      '&lt;b a=&quot;1&quot;&gt;&#39;&amp;&#39;&lt;/b&gt;',
    );
  });

  it('formats tooltips as a single line of text', () => {
    const options = highchartsAdapter.toOptions(specs['line']!);
    const formatter = options.tooltip?.formatter as TooltipFormatterCallbackFunction;
    const point = {
      category: '1Y',
      y: 2.5,
      series: { name: 'EE' },
    } as unknown as Point;
    expect(formatter.call(point, undefined as never)).toBe('EE - 1Y: 2.5 m EUR');
  });

  it('scales amounts to millions and uses CSS classes for dashes and thresholds', () => {
    const options = highchartsAdapter.toOptions(specs['line']!);
    const series = options.series as { className?: string; data: { y: number | null }[] }[];
    expect(series[0]?.data.map((p) => p.y)).toEqual([1, 2]);
    expect(series[1]?.className).toBe('cr-series-dash');
    const yAxis = options.yAxis as { plotLines: { value: number; className: string }[] };
    expect(yAxis.plotLines[0]).toMatchObject({ value: 2, className: 'cr-plotline-breach' });
  });
});

describe('Highcharts waterfall', () => {
  it('draws subtotals and totals as the cumulative amounts of the data table', () => {
    const spec = waterfallSpec({
      ...common,
      steps: [
        { label: 'Gross', value: 500e6, kind: 'start', family: 'CurrentExposure' },
        { label: 'Netting', value: -200e6, kind: 'delta', family: 'CurrentExposure' },
        { label: 'After netting', value: 0, kind: 'subtotal', family: 'CurrentExposure' },
        { label: 'Collateral', value: -100e6, kind: 'delta', family: 'CurrentExposure' },
        { label: 'After collateral', value: 0, kind: 'subtotal', family: 'CurrentExposure' },
        { label: 'Add-on', value: 300e6, kind: 'delta', family: 'Regulatory' },
        { label: 'EAD', value: 0, kind: 'total', family: 'Regulatory' },
      ],
    });
    const container = document.createElement('div');
    document.body.appendChild(container);
    const options = highchartsAdapter.toOptions(spec);
    const chart = Highcharts.chart(container, {
      ...options,
      chart: { ...options.chart, width: 600 },
    });

    const drawn = chart.series[0]!.points.map((p) => formatMoney((p.y ?? 0) * 1e6));
    expect(drawn).toEqual(spec.table.rows.map((r) => r[1]));
    expect(spec.table.rows.map((r) => r[1])).toEqual([
      '500.0 m EUR',
      '-200.0 m EUR',
      '300.0 m EUR',
      '-100.0 m EUR',
      '200.0 m EUR',
      '300.0 m EUR',
      '500.0 m EUR',
    ]);
    chart.destroy();
    container.remove();
  });
});

describe('Highcharts line options', () => {
  const series = (dash: 'solid' | 'dash' | 'dot', id: string) => ({
    id,
    name: id,
    family: 'Economic' as const,
    dash,
    points: Array.from({ length: 30 }, (_, i) => [`P${i}`, i] as const),
  });
  const line = (zoomX: boolean, markers: boolean | null) =>
    highchartsAdapter.toOptions(
      lineChartSpec({
        ...common,
        series: [series('solid', 'ee'), series('dash', 'pfe'), series('dot', 'ene')],
        unit: 'count',
        level: 'NotAnExposure',
        xKind: 'category',
        xTitle: '',
        rebase100: false,
        limits: [],
        events: [],
        zoomX,
        markers,
      }),
    );

  it('gives each series its own dash pattern class', () => {
    const classes = (line(false, null).series as { className?: string }[]).map((s) => s.className);
    expect(classes).toEqual([undefined, 'cr-series-dash', 'cr-series-dot']);
  });

  it('enables X zoom and forces markers on demand', () => {
    expect(line(true, false).chart?.zooming).toEqual({ type: 'x' });
    expect('zooming' in (line(false, null).chart ?? {})).toBe(false);
    const markers = (o: Options) =>
      (o.series as { marker: { enabled: boolean } }[]).map((s) => s.marker.enabled);
    expect(markers(line(false, null))).toEqual([false, false, false]);
    expect(markers(line(false, true))).toEqual([true, true, true]);
  });
});

describe('Highcharts rendering of untrusted labels', () => {
  it('renders a malicious category, series and title as text, never as an element', () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const options: Options = highchartsAdapter.toOptions(specs['line']!);
    const chart = Highcharts.chart(container, {
      ...options,
      chart: { ...options.chart, width: 600 },
    });

    expect(container.querySelector('svg')).not.toBeNull();
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('[onerror]')).toBeNull();
    expect(container.textContent).toContain('<img src=x onerror=alert(1)>');
    chart.destroy();
    container.remove();
  });
});
