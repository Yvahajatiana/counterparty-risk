import { Routes } from '@angular/router';
import { WIDGET_NAV_ITEMS } from './layout/navigation';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Counterparty Risk',
    loadComponent: () => import('./features/home/home').then((m) => m.Home),
  },
  // Temporary empty pages, replaced by the feature routes of W1-W12 (#13).
  ...WIDGET_NAV_ITEMS.map((item) => ({
    path: item.path,
    title: `${item.code} - Counterparty Risk`,
    data: { code: item.code },
    loadComponent: () =>
      import('./features/widget-placeholder/widget-placeholder').then((m) => m.WidgetPlaceholder),
  })),
  { path: '**', redirectTo: '' },
];
