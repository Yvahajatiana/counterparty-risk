import { Routes } from '@angular/router';
import { WIDGET_NAV_ITEMS } from './layout/navigation';

/** Widgets already built for the demo; the others show their placeholder page. */
const WIDGET_PAGES: Readonly<Record<string, Routes[number]['loadComponent']>> = {
  summary: () => import('./features/summary/summary').then((m) => m.Summary),
  'current-exposure': () =>
    import('./features/current-exposure/current-exposure').then((m) => m.CurrentExposure),
  'future-exposure': () =>
    import('./features/future-exposure/future-exposure').then((m) => m.FutureExposure),
  limits: () => import('./features/limits/limits').then((m) => m.Limits),
  history: () => import('./features/history/history').then((m) => m.History),
};

const placeholder = () =>
  import('./features/widget-placeholder/widget-placeholder').then((m) => m.WidgetPlaceholder);

// Component catalogue, development builds only: ngDevMode is false in production builds, so the
// route and its lazy chunk are removed from the bundle.
const DEV_ROUTES: Routes =
  typeof ngDevMode !== 'undefined' && ngDevMode
    ? [
        {
          path: 'dev/catalog',
          title: 'Component catalogue - Counterparty Risk',
          loadComponent: () =>
            import('./features/dev-catalog/dev-catalog').then((m) => m.DevCatalog),
        },
      ]
    : [];

export const routes: Routes = [
  // The summary is the landing page.
  { path: '', pathMatch: 'full', redirectTo: 'summary' },
  ...DEV_ROUTES,
  ...WIDGET_NAV_ITEMS.map((item) => ({
    path: item.path,
    title: `${item.title} - Counterparty Risk`,
    data: { path: item.path },
    loadComponent: WIDGET_PAGES[item.path] ?? placeholder,
  })),
  { path: '**', redirectTo: '' },
];
