import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { widgetNav } from '../../layout/navigation';
import { WidgetHeader } from '../../shared/ui/widget-header';

/** Page of a widget that is not built yet: title and business question only. */
@Component({
  selector: 'app-widget-placeholder',
  imports: [WidgetHeader],
  template: `
    <app-widget-header [widget]="widget()" />
    <p class="cr-card cr-text-muted">Coming in a next iteration of the demo.</p>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WidgetPlaceholder {
  /** Bound from the route `data.path` (withComponentInputBinding). */
  readonly path = input.required<string>();
  protected readonly widget = computed(() => widgetNav(this.path()));
}
