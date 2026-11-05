import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ThemeMode, ThemeService } from '../../core/theme/theme.service';
import { WIDGET_NAV_ITEMS } from '../navigation';

const THEME_LABELS: Record<ThemeMode, string> = {
  system: 'System',
  light: 'Light',
  dark: 'Dark',
};

/** Application shell: top bar and side navigation (W1-W12). Content is added by #13. */
@Component({
  selector: 'app-shell',
  imports: [
    MatButtonModule,
    MatListModule,
    MatSidenavModule,
    MatToolbarModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Shell {
  private readonly theme = inject(ThemeService);

  protected readonly navItems = WIDGET_NAV_ITEMS;
  protected readonly themeLabel = computed(() => THEME_LABELS[this.theme.mode()]);

  protected cycleTheme(): void {
    this.theme.cycle();
  }
}
