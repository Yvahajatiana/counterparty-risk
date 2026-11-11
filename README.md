# Counterparty Risk Dashboard

Counterparty credit risk (CCR) dashboard: 12 widgets based on standard Basel / CRR indicators
(exposure from gross to net, EE / PFE, limits, SA-CCR EAD, RWA, CVA, CDS, collateral,
concentration, stress, history).

Built with .NET 10 (API, risk engine, ingestion) and Angular (AG Grid Community, Highcharts), fed by public open data (BIS, EBA, GLEIF, ECB, CFTC SDR) and a synthetic
portfolio. No confidential bank data.

- Security: see [SECURITY.md](SECURITY.md) (Secure by Design is the top priority).
- Work breakdown: see GitHub issues and milestones.

## Demo (Docker)

Front-end driven development: the Angular app is served by the BFF, and the dashboard endpoints
(`/api/dashboard/*`) return synthetic data until the Api implements them, one endpoint at a time.
Widgets built so far: Summary, Current exposure, Future exposure, Limits, History.

```bash
docker compose up --build -d   # then open http://localhost:8080
docker compose down
```

Local development without Docker: `dotnet run --project src/CounterpartyRisk.Bff` (mock data is on
in Development) and `npm start` in `web/` (http://localhost:4200, `/api` proxied to the BFF).
