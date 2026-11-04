# Third-party licenses (web)

Main runtime libraries of the Angular front end. Versions are pinned exactly in `package.json`
and locked in `package-lock.json`. The licence decisions come from
[ADR-001](../docs/adr/0001-stack-and-architecture.md) (#3). The consolidated licence documentation
is maintained in #41.

| Package | Version | License | Usage |
| --- | --- | --- | --- |
| `@angular/*` (core, common, router, forms, platform-browser) | 22.2.4 | MIT | Framework |
| `@angular/material`, `@angular/cdk` | 22.2.6 | MIT | UI components (corporate M3 theme) |
| `ag-grid-community` | 36.3.0 | MIT | Data grids (wrapper in #14) |
| `ag-grid-angular` | 36.3.0 | MIT | Angular binding for AG Grid |
| **`highcharts`** | **13.1.1** | **Highsoft proprietary license, see below** | Charts (wrapper in #14) |
| `highcharts-angular` | 5.4.1 | MIT | Angular binding for Highcharts (the wrapper only) |
| `@fontsource-variable/inter` | 5.3.0 | OFL-1.1 | Inter font, self-hosted |
| `rxjs` | 7.8.2 | Apache-2.0 | Reactive streams |
| `tslib` | 2.9.1 | 0BSD | TypeScript helpers |
| `tailwindcss` (build time) | 4.3.4 | MIT | Layout utilities |

Only **AG Grid Community** is used (MIT). AG Grid Enterprise modules (`ag-grid-enterprise`) are not
installed and must not be added: they require a commercial licence key.

## Highcharts: non-commercial restriction

> **Highcharts is NOT open source.** It is free only for **non-commercial use** (personal,
> educational, non-profit, or demonstration). **Any commercial use requires a paid licence**
> from Highsoft AS: <https://www.highcharts.com/license>.

- This public demonstration repository qualifies as non-commercial use (ADR-001, #3).
- If the project is ever used commercially (internal use in a bank included), either buy a
  Highcharts licence or switch to Apache ECharts (Apache-2.0). #14 must isolate Highcharts behind
  a `ChartAdapter` layer so the switch does not affect the widgets.
- The Highcharts `credits` option stays enabled (licence attribution).
- The Highcharts export server is not used: no chart data leaves the browser.
- The `highcharts-angular` wrapper is MIT, but it does not change the licence of `highcharts`.

## Automated licence gate

`npm run check:licenses` (`scripts/check-licenses.mjs`, run in `web-ci.yml`) reads
`package-lock.json` and fails on any licence outside the allow-list (MIT, ISC, BSD, Apache-2.0,
0BSD, OFL-1.1, CC0, CC-BY-4.0, BlueOak, Python-2.0, Unlicense). The only exceptions are:

| Package | Licence | Justification |
| --- | --- | --- |
| `highcharts` | Highsoft proprietary | Non-commercial use only, see above and ADR-001 (#3) |
| `lightningcss*` (dev only) | MPL-2.0 | Build-time CSS tool used by Tailwind, never shipped in the bundle |

Any new exception requires a Lead review and an update of this file.
