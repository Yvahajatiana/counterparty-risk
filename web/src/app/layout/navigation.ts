export interface WidgetNavItem {
  /** Route segment, e.g. "summary". */
  readonly path: string;
  readonly title: string;
  /** Business question answered by the widget (widget catalogue). */
  readonly question: string;
}

const WIDGETS: readonly (readonly [string, string, string])[] = [
  ['summary', 'Summary', 'Where does counterparty risk stand today, and what has moved?'],
  [
    'current-exposure',
    'Current exposure',
    'How much would we lose if the counterparty defaulted today, before and after mitigation?',
  ],
  ['future-exposure', 'Future exposure', 'How much could we lose tomorrow, and at which horizon?'],
  ['limits', 'Limits', 'Who consumes too much limit, and since when?'],
  ['regulatory-ead', 'Regulatory EAD', 'Which exposure is used for capital, and how is it built?'],
  [
    'rwa-capital',
    'RWA and capital',
    'How much capital does counterparty risk consume, and why has it moved?',
  ],
  [
    'cva-xva',
    'CVA and XVA',
    'What does counterparty risk cost in accounting value, and how sensitive is it?',
  ],
  ['cds', 'CDS', 'Which role do CDS play in counterparty risk?'],
  [
    'collateral',
    'Collateral and margins',
    'Is our exposure well covered by collateral, and are margin calls under control?',
  ],
  [
    'concentration',
    'Concentration',
    'Do we depend too much on a few counterparties, sectors or countries?',
  ],
  ['stress-wwr', 'Stress and WWR', 'What happens to the exposure in an adverse scenario?'],
  ['history', 'History', 'How has risk evolved over time, and when did it break its trend?'],
];

/** Side navigation entries for the 12 dashboard widgets. */
export const WIDGET_NAV_ITEMS: readonly WidgetNavItem[] = WIDGETS.map(
  ([path, title, question]) => ({
    path,
    title,
    question,
  }),
);

export function widgetNav(path: string): WidgetNavItem {
  const item = WIDGET_NAV_ITEMS.find((candidate) => candidate.path === path);
  if (!item) {
    throw new Error(`Unknown widget route: ${path}`);
  }
  return item;
}
