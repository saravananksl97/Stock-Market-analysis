# Rupee Desk MVC

ASP.NET Core MVC market-research workspace for Indian equities. The app has independent Razor views for the overview, intraday scanner, swing research, deep research, strategy playbook and backtest.

## Run

Requires the .NET 10 SDK.

```powershell
cd swing-desk-mvc
dotnet restore
dotnet run
```

The default provider is deterministic synthetic sample data. It is labeled in the UI and is not historical or live NSE data.

## Free Preview Deployment

The included `Dockerfile` and `render.yaml` target a Render free web service in Singapore. Render free services sleep after 15 minutes without inbound traffic and can take about a minute to wake; Render explicitly does not recommend its free plan for production. It is suitable for a public preview, not dependable live trading or time-critical alerts.

To deploy, push this project directory as the root of a GitHub repository, sign in to Render, then create a new Blueprint from that repository and apply `render.yaml`. Render requires a connected Git repository; this workspace currently has no Git remote or hosting account connected. Set `MarketData__QuotesEndpoint` in the Render service's environment settings after deployment to use an authorized provider. Do not add provider tokens or credentials to this repository. Without a configured endpoint, the deployed app remains in labeled demo mode.

## Configure Market Data

For NSE quotes, the app supports Upstox's official read-only Analytics Token and market-data APIs. The token is free to use, requires an Upstox account, and is read-only; it cannot place or modify orders. Generate it from Upstox Developer Apps > Analytics and configure it as a server environment variable:

```powershell
$env:MarketData__UpstoxAnalyticsToken = "<set locally; do not commit>"
dotnet run
```

On Render, provide `MarketData__UpstoxAnalyticsToken` as a secret environment variable during Blueprint setup or in the service settings. Do not put tokens in Razor views, browser JavaScript, `appsettings.json`, or Git. The integration uses the Upstox full-market-quotes API and V3 5-minute and daily candle endpoints. The dashboard caches provider responses for 20 seconds and reports the provider timestamp. Confirm the feed's licensing and redistribution terms for your intended use. This project does not scrape NSE website endpoints and does not submit orders.

An optional normalized provider endpoint is also supported by setting `MarketData__QuotesEndpoint`; use it only for a provider you are authorized to access.

```json
[
  {
    "symbol": "RELIANCE",
    "company": "Reliance Industries",
    "sector": "Energy",
    "price": 1420.5,
    "changePercent": 0.8,
    "dataSource": "Licensed provider",
    "asOf": "2026-09-27T09:30:00Z",
    "fundamentals": {
      "marketCapCrore": 1920000,
      "priceEarnings": 25.4,
      "revenueGrowthPercent": 8.2,
      "debtToEquity": 0.42,
      "relativeStrengthPercent": 3.1,
      "asOf": "2026-06-30T00:00:00Z"
    },
    "intradayCandles": [
      { "time": "2026-09-27T09:15:00+05:30", "open": 1418, "high": 1422, "low": 1417, "close": 1420.5, "volume": 12500 }
    ],
    "dailyCandles": [
      { "time": "2026-09-26T00:00:00+05:30", "open": 1412, "high": 1422, "low": 1409, "close": 1420.5, "volume": 840000 }
    ],
    "intradaySessions": [
      [
        { "time": "2026-09-26T09:15:00+05:30", "open": 1412, "high": 1414, "low": 1410, "close": 1413, "volume": 8200 }
      ]
    ]
  }
]
```

For normalized provider responses and useful backtests, supply at least 51 daily candles and intraday history. Upstox's free V3 intraday history is limited to approximately one month for 1-15 minute intervals, so intraday backtests may have fewer than the requested 60/120 sessions. The app does not pad real history with synthetic candles. Missing fundamentals remain marked as unavailable. If the feed fails authorization or returns no valid quotes, the app falls back to marked synthetic demo data.

## Included Rules

- Intraday ORB + VWAP: first 15-minute range, VWAP and EMA(9/21) confirmation; modeled 1.2 × recent range stop and 2R target.
- Swing EMA pullback: EMA(20/50) trend, prior-bar EMA test and completed-bar reclaim; modeled 1.5 × average range stop, 2R target and 10-session backtest time exit.
- Donchian breakout: documented in the playbook, not enabled in the backtest engine.

Backtests use 1% equity risk at the initial stop, whole-share sizing, an estimated 0.10% round-trip notional cost, and stop-first priority when one candle touches both stop and target. They do not model all taxes, fees, market impact, partial fills, corporate actions or gap-through-stop losses. Short-side swing signals require an eligible product; cash-market shorting, borrow availability, futures margin and expiry are not modeled. Backtest results are not forecasts or recommendations.