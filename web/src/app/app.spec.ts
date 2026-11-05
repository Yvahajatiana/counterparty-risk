import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  it('renders the shell with the top bar and the widget navigation', async () => {
    await render(App, { providers: [provideRouter(routes)] });

    expect(screen.getByRole('banner')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Counterparty Risk' })).toBeTruthy();
    expect(screen.getByRole('navigation', { name: 'Widgets' })).toBeTruthy();
  });
});
