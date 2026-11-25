import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { WIDGET_NAV_ITEMS } from '../navigation';
import { Shell } from './shell';

// No routes: the shell is tested alone, without loading a widget page (and its HTTP calls).
const providers = [provideRouter([])];

describe('Shell', () => {
  it('lists the 12 widgets with their routes', async () => {
    await render(Shell, { providers });

    const nav = screen.getByRole('navigation', { name: 'Widgets' });
    const links = Array.from(nav.querySelectorAll('a'));
    expect(links.map((link) => link.textContent?.trim())).toEqual(
      WIDGET_NAV_ITEMS.map((item) => item.title),
    );
    expect(links[0]?.getAttribute('href')).toBe('/summary');
    expect(links[11]?.getAttribute('href')).toBe('/history');
  });

  it('cycles the theme from the top bar', async () => {
    const { fixture } = await render(Shell, { providers });
    const button = screen.getByRole('button', { name: /Theme: System/ });

    button.click();
    await fixture.whenStable();

    expect(button.textContent).toContain('Theme: Light');
  });
});

describe('WIDGET_NAV_ITEMS', () => {
  it('uses unique route segments', () => {
    expect(new Set(WIDGET_NAV_ITEMS.map((item) => item.path)).size).toBe(12);
    expect(WIDGET_NAV_ITEMS).toHaveLength(12);
  });
});
