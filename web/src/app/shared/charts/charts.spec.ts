import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { render, screen } from '@testing-library/angular';
import { ChartFrame } from './chart-frame';
import { ChartSpec, CrSeries } from './chart.models';
import { HorizontalBarChart } from './horizontal-bar-chart';
import { ChartCanvas } from './highcharts/chart-canvas';
import { LineChart } from './line-chart';
import { ParetoChart } from './pareto-chart';
import { WaterfallChart } from './waterfall-chart';

/** Stands in for the Highcharts canvas (no SVG layout in jsdom); records the spec it gets. */
@Component({
  selector: 'app-chart-canvas',
  template: `<div data-testid="canvas">{{ spec().kind }}</div>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class StubCanvas {
  readonly spec = input.required<ChartSpec>();
}

beforeEach(() => {
  TestBed.overrideComponent(ChartFrame, {
    remove: { imports: [ChartCanvas] },
    add: { imports: [StubCanvas] },
  });
});

const ee: CrSeries = {
  id: 'ee',
  name: 'EE',
  family: 'Economic',
  points: [
    ['1Y', 10e6],
    ['2Y', 12e6],
  ],
};

describe('chart components', () => {
  it('shows the exposure level in the title', async () => {
    await render(LineChart, {
      inputs: { title: 'Exposure', level: 'AfterCollateral', series: [ee] },
    });
    expect(screen.getByText('Exposure - After collateral')).toBeTruthy();
    expect(screen.getByTestId('canvas').textContent).toBe('cartesian');
  });

  it('does not draw a chart mixing economic and regulatory measures, with an explicit error in dev', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const ead: CrSeries = { ...ee, id: 'ead', name: 'EAD', family: 'Regulatory' };
    await render(LineChart, { inputs: { title: 'Mixed', level: 'Ead', series: [ee, ead] } });

    expect(screen.queryByTestId('canvas')).toBeNull();
    expect(screen.getByRole('alert').textContent).toContain(
      'Economic (EE/PFE) and regulatory (EAD/RWA) measures cannot be shown in the same chart',
    );
    expect(error).toHaveBeenCalledWith(expect.stringContaining('chart.mixed_families'));
    error.mockRestore();
  });

  it('requires rebase100 for series of different units', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const utilisation: CrSeries = { ...ee, id: 'u', unit: 'percent' };
    const { rerender } = await render(LineChart, {
      inputs: { title: 'Units', level: 'AfterCollateral', series: [ee, utilisation] },
    });
    expect(screen.getByRole('alert').textContent).toContain('rebase100');

    await rerender({
      inputs: {
        title: 'Units',
        level: 'AfterCollateral',
        series: [ee, utilisation],
        rebase100: true,
      },
      partialUpdate: true,
    });
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.getByTestId('canvas')).toBeTruthy();
    expect(screen.getAllByText('100.0').length).toBeGreaterThan(0);
    vi.restoreAllMocks();
  });

  it('renders untrusted labels as text in the data table', async () => {
    const { container } = await render(HorizontalBarChart, {
      inputs: {
        title: 'Utilisation',
        level: 'NotAnExposure',
        family: 'Economic',
        seriesName: 'Utilisation',
        items: [{ label: '<img src=x onerror=alert(1)>', value: 0.85 }],
      },
    });
    expect(container.querySelector('img')).toBeNull();
    expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeTruthy();
    expect(screen.getByText('85.0%')).toBeTruthy();
  });

  it('exposes a data table for keyboard and screen reader users', async () => {
    await render(WaterfallChart, {
      inputs: {
        title: 'CCR exposure',
        level: { from: 'GrossBeforeCrm', to: 'AfterCollateral' },
        steps: [
          { label: 'Gross', value: 5e6, kind: 'start', family: 'CurrentExposure' },
          { label: 'RC', value: 0, kind: 'total', family: 'Regulatory' },
        ],
      },
    });
    expect(
      screen.getByText('CCR exposure - Gross exposure before CRM to After collateral'),
    ).toBeTruthy();
    expect(screen.getByRole('table')).toBeTruthy();
    expect(screen.getAllByText('5.0 m EUR')).toHaveLength(2);
  });

  it('renders a Pareto of the top N', async () => {
    await render(ParetoChart, {
      inputs: {
        title: 'Top counterparties by EAD',
        level: 'Ead',
        family: 'Regulatory',
        seriesName: 'EAD',
        top: 2,
        items: [
          { label: 'A', value: 60 },
          { label: 'B', value: 30 },
          { label: 'C', value: 10 },
        ],
      },
    });
    expect(screen.getByText('Top counterparties by EAD - EAD')).toBeTruthy();
    expect(screen.getAllByRole('row')).toHaveLength(3);
  });
});
