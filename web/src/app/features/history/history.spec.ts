import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { render, screen } from '@testing-library/angular';
import { HistoryResponse } from '../../core/api/dashboard.models';
import { ChartFrame } from '../../shared/charts/chart-frame';
import { ChartSpec } from '../../shared/charts/chart.models';
import { ChartCanvas } from '../../shared/charts/highcharts/chart-canvas';
import { History } from './history';

/** Stands in for the Highcharts canvas (no SVG layout in jsdom); shows the options under test. */
@Component({
  selector: 'app-chart-canvas',
  template: `<div data-testid="canvas">{{ summary() }}</div>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class StubCanvas {
  readonly spec = input.required<ChartSpec>();
  protected readonly summary = computed(() => {
    const spec = this.spec();
    return spec.kind === 'cartesian' ? `zoomX=${spec.zoomX} markers=${spec.markers}` : spec.kind;
  });
}

function response(indicator: string): HistoryResponse {
  return {
    quality: {
      asOfDate: '2026-11-24',
      reportingCurrency: 'EUR',
      source: 'Synthetic',
      status: 'Synthetic',
      completeness: 'Complete',
    },
    indicator: indicator as HistoryResponse['indicator'],
    label: 'Total EAD',
    level: 'EAD',
    vision: 'Regulatory',
    frequency: 'Daily',
    points: [
      { date: '2026-11-21', value: 1e9 },
      { date: '2026-11-24', value: 1.1e9 },
    ],
    events: [],
  };
}

async function renderWith(indicator: string) {
  TestBed.overrideComponent(ChartFrame, {
    remove: { imports: [ChartCanvas] },
    add: { imports: [StubCanvas] },
  });
  const result = await render(History, {
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  TestBed.inject(HttpTestingController)
    .expectOne((r) => r.url === '/api/dashboard/history')
    .flush(response(indicator));
  await result.fixture.whenStable();
  return result;
}

describe('History', () => {
  it('labels the chart with the level of the returned indicator, zoomable, without markers', async () => {
    await renderWith('Ead');
    expect(await screen.findByText('Total EAD (EUR m) - EAD')).toBeTruthy();
    expect(screen.getByTestId('canvas').textContent).toBe('zoomX=true markers=false');
  });

  it('refuses to draw an unknown indicator instead of guessing its exposure level', async () => {
    await renderWith('Unknown');
    expect((await screen.findByRole('alert')).textContent).toContain('Unknown indicator');
    expect(screen.queryByTestId('canvas')).toBeNull();
    expect(screen.queryByText(/ - EAD$/)).toBeNull();
  });
});
