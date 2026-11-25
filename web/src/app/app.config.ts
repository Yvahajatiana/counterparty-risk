import { provideHttpClient, withFetch } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHighcharts } from 'highcharts-angular';
import { routes } from './app.routes';
import { ThemeService } from './core/theme/theme.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch()),
    // Loaded lazily with the first chart: core, waterfall series (more) and the accessibility module.
    provideHighcharts({
      instance: () => import('highcharts/esm/highcharts').then((m) => m.default),
      modules: () => [
        import('highcharts/esm/highcharts-more'),
        import('highcharts/esm/modules/accessibility'),
      ],
    }),
    // Instantiated before the first render so that data-theme is set without a flash.
    provideAppInitializer(() => {
      inject(ThemeService);
    }),
  ],
};
