import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { KpiTileComponent } from './kpi-tile';

describe('KpiTile', () => {
  it('shows the value with its level, variations and link', async () => {
    const { container } = await render(KpiTileComponent, {
      inputs: {
        label: 'Total EAD',
        value: 1_100_000_000,
        unit: 'money',
        level: 'EAD',
        vision: 'Regulatory',
        route: '/regulatory-ead',
        status: 'warn',
        previousDay: 1_000_000_000,
        previousMonth: 1_200_000_000,
      },
      providers: [provideRouter([])],
    });

    expect(screen.getByText('1,100.0 m EUR')).toBeTruthy();
    expect(screen.getByText('Regulatory · EAD')).toBeTruthy();
    const link = screen.getByRole('link');
    expect(link.getAttribute('href')).toBe('/regulatory-ead');
    expect(link.getAttribute('data-status')).toBe('warn');

    const changes = Array.from(container.querySelectorAll('[data-direction]'));
    expect(changes.map((c) => c.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'D-1 +10.0% (adverse)',
      'M-1 -8.3% (favourable)',
    ]);
  });

  it('reads a lower value as adverse when higher is better', async () => {
    const { container } = await render(KpiTileComponent, {
      inputs: {
        label: 'Collateral coverage',
        value: 0.7,
        unit: 'percent',
        level: 'After collateral',
        route: '/collateral',
        previousDay: 0.75,
        higherIsWorse: false,
      },
      providers: [provideRouter([])],
    });
    expect(container.querySelector('[data-assessment]')?.getAttribute('data-assessment')).toBe(
      'adverse',
    );
  });
});
