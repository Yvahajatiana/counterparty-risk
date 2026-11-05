export interface WidgetNavItem {
  /** Short code displayed in the side navigation, e.g. "W1". */
  readonly code: string;
  /** Route segment, e.g. "w01". */
  readonly path: string;
}

/**
 * Side navigation entries for the 12 dashboard widgets.
 * Labels and feature routes are filled in by #13 once the widgets are specified.
 */
export const WIDGET_NAV_ITEMS: readonly WidgetNavItem[] = Array.from({ length: 12 }, (_, index) => {
  const number = index + 1;
  return { code: `W${number}`, path: `w${String(number).padStart(2, '0')}` };
});
