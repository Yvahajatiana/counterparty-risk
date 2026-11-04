# Counterparty Risk Dashboard

Counterparty credit risk (CCR) dashboard: 12 widgets based on standard Basel / CRR indicators
(exposure from gross to net, EE / PFE, limits, SA-CCR EAD, RWA, CVA, CDS, collateral,
concentration, stress, history).

Built with .NET 10 (API, risk engine, ingestion) and Angular (AG Grid Community, Highcharts), fed by public open data (BIS, EBA, GLEIF, ECB, CFTC SDR) and a synthetic
portfolio. No confidential bank data.

- Security: see [SECURITY.md](SECURITY.md) (Secure by Design is the top priority).
- Work breakdown: see GitHub issues and milestones.
