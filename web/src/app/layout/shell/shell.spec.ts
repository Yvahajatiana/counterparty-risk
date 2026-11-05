import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { routes } from '../../app.routes';
import { WIDGET_NAV_ITEMS } from '../navigation';
import { Shell } from './shell';

describe('Shell', () => {
  it('lists the 12 widgets W1 to W12 with their routes', async () => {
    await render(Shell, { providers: [provideRouter(routes)] });

    const nav = screen.getByRole('navigation', { name: 'Widgets' });
    const links = Array.from(nav.querySelectorAll('a'));
    expect(links.map((link) => link.textContent?.trim())).toEqual(
      Array.from({ length: 12 }, (_, i) => `W${i + 1}`),
    );
    expect(links[0]?.getAttribute('href')).toBe('/w01');
    expect(links[11]?.getAttribute('href')).toBe('/w12');
  });

  it('cycles the theme from the top bar', async () => {
    const { fixture } = await render(Shell, { providers: [provideRouter(routes)] });
    const button = screen.getByRole('button', { name: /Theme: System/ });

    button.click();
    await fixture.whenStable();

    expect(button.textContent).toContain('Theme: Light');
  });
});

describe('WIDGET_NAV_ITEMS', () => {
  it('uses zero-padded route segments', () => {
    expect(WIDGET_NAV_ITEMS.map((item) => item.path)).toContain('w09');
    expect(WIDGET_NAV_ITEMS).toHaveLength(12);
  });
});
