using RupeeDesk.Mvc.Models;

namespace RupeeDesk.Mvc.Services;

public sealed class MarketResearchService(IMarketDataProvider marketDataProvider) : IMarketResearchService
{
    private const decimal RiskFraction = .01m;
    private const decimal RoundTripCostFraction = .001m;

    public async Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var feed = await marketDataProvider.GetFeedAsync(cancellationToken);
        return new DashboardViewModel
        {
            Candidates = BuildRows(feed.Stocks, BuildIntradayPlan, useDailyCandles: false),
            FeedStatus = feed.Status,
            IsDemo = feed.IsDemo,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public async Task<IntradayViewModel> GetIntradayAsync(string? symbol, CancellationToken cancellationToken = default)
    {
        var feed = await marketDataProvider.GetFeedAsync(cancellationToken);
        var stocks = feed.Stocks;
        var selected = FindStock(stocks, symbol);
        return new IntradayViewModel
        {
            Candidates = BuildRows(stocks, BuildIntradayPlan, useDailyCandles: false),
            SelectedStock = selected,
            SelectedPlan = BuildIntradayPlan(selected),
            FeedStatus = feed.Status,
            IsDemo = feed.IsDemo
        };
    }

    public async Task<SwingViewModel> GetSwingAsync(string? symbol, CancellationToken cancellationToken = default)
    {
        var feed = await marketDataProvider.GetFeedAsync(cancellationToken);
        var selected = FindStock(feed.Stocks, symbol);
        return new SwingViewModel
        {
            Candidates = BuildRows(feed.Stocks, BuildSwingPlan, useDailyCandles: true),
            SelectedStock = selected,
            SelectedPlan = BuildSwingPlan(selected),
            Findings = BuildFindings(selected),
            FeedStatus = feed.Status,
            IsDemo = feed.IsDemo
        };
    }

    public async Task<ResearchViewModel> GetResearchAsync(string? symbol, CancellationToken cancellationToken = default)
    {
        var feed = await marketDataProvider.GetFeedAsync(cancellationToken);
        var selected = FindStock(feed.Stocks, symbol);
        return new ResearchViewModel
        {
            Stock = selected,
            Stocks = feed.Stocks,
            IntradayPlan = BuildIntradayPlan(selected),
            SwingPlan = BuildSwingPlan(selected),
            Findings = BuildFindings(selected),
            FeedStatus = feed.Status,
            IsDemo = feed.IsDemo
        };
    }

    public Task<IReadOnlyList<StrategyInfo>> GetStrategiesAsync()
    {
        IReadOnlyList<StrategyInfo> strategies =
        [
            new("Opening Range Breakout + VWAP", "Intraday · 5-minute bars", "Mark the first three 5-minute bars. Long only after a completed close above the opening range high, session VWAP and EMA(9) > EMA(21). Short is the mirrored condition below the range low.", "Signal candle close is the modeled entry. Initial stop is the larger of 1.2 × recent average range or 0.2% of entry. Target is 2R. Exit at stop, target or session close; if stop and target touch in one candle, assume stop first.", "False breakouts, opening gaps, thin liquidity and same-candle stop/target ambiguity. Skip if the stop distance makes the planned risk or quantity unsuitable.", "Liquid NSE names on a trend day; avoid treating a high confluence count as a probability of profit."),
            new("Daily EMA Pullback + Reclaim", "Swing · daily bars", "Require EMA(20) > EMA(50) for a long. The previous bar must test the 20-day EMA; the latest completed daily bar must close above EMA(20) and above the previous high. Short is mirrored.", "Modeled entry is the signal-day close. Initial stop is 1.5 × the mean high-low range of the last 14 sessions, target is 2R, and the backtest time exit is 10 sessions. Re-check the level at the next open for gaps.", "Whipsaw in sideways markets, overnight gaps through stops, corporate actions and stale fundamental data. The demo has no corporate-action adjustment.", "Directional trends after a controlled pullback; confirm fundamentals, earnings dates and index/sector context separately."),
            new("Donchian Range Breakout", "Swing · daily bars", "Watch for a completed close beyond the prior 20-session high/low with volume above its 20-day average and a rising EMA(50). The current day is excluded from the channel calculation.", "Enter only after the close confirms the break. Use a 2 × ATR(14) initial stop or a trailing 10-session channel, whichever your tested plan specifies; cap rupee risk before placing the order.", "Late entries after extended moves, overnight gaps, and high turnover costs. This rule is documented but not selected for the current backtest implementation.", "Broad market expansion and liquid names; validate with point-in-time data before live use.")
        ];
        return Task.FromResult(strategies);
    }

    public async Task<BacktestViewModel> GetBacktestAsync(string? symbol, string? strategy, int sessions, decimal capital, CancellationToken cancellationToken = default)
    {
        var feed = await marketDataProvider.GetFeedAsync(cancellationToken);
        var stock = FindStock(feed.Stocks, symbol);
        var normalizedStrategy = string.Equals(strategy, "swing", StringComparison.OrdinalIgnoreCase) ? "swing" : "intraday";
        var lookback = Math.Clamp(sessions, 20, 120);
        var startingCapital = Math.Clamp(capital, 10000m, 100000000m);
        var trades = normalizedStrategy == "swing"
            ? SimulateSwing(stock, lookback, startingCapital)
            : SimulateIntraday(stock, lookback, startingCapital);
        var result = Summarize(stock, normalizedStrategy, lookback, startingCapital, trades, feed);
        return result;
    }

    private static IReadOnlyList<ScannerRow> BuildRows(IReadOnlyList<StockSnapshot> stocks, Func<StockSnapshot, TradePlan> buildPlan, bool useDailyCandles)
    {
        return stocks.Select(stock =>
        {
            var candles = useDailyCandles ? stock.DailyCandles : stock.IntradayCandles;
            var closes = candles.Select(candle => candle.Close).ToList();
            var atr = AverageRange(candles.TakeLast(14).ToList());
            return new ScannerRow(stock, buildPlan(stock), Rsi(closes, 14), Ema(closes, 20), Ema(closes, 50), stock.Price == 0 ? 0 : atr / stock.Price * 100);
        })
        .OrderByDescending(row => row.Plan.Triggered)
        .ThenByDescending(row => row.Plan.RuleChecksPassed)
        .ThenByDescending(row => Math.Abs(row.Stock.ChangePercent))
        .ToList();
    }

    private static TradePlan BuildIntradayPlan(StockSnapshot stock)
    {
        var candles = stock.IntradayCandles;
        if (candles.Count < 22)
        {
            return WaitPlan("Opening Range + VWAP", "At least 22 completed 5-minute bars are required.");
        }

        var opening = candles.Take(3).ToList();
        var rangeHigh = opening.Max(candle => candle.High);
        var rangeLow = opening.Min(candle => candle.Low);
        var closes = candles.Select(candle => candle.Close).ToList();
        var ema9 = Ema(closes, 9);
        var ema21 = Ema(closes, 21);
        var currentVwap = Vwap(candles);
        var last = candles[^1];
        var longChecks = (last.Close > rangeHigh ? 1 : 0) + (last.Close > currentVwap ? 1 : 0) + (ema9 > ema21 ? 1 : 0);
        var shortChecks = (last.Close < rangeLow ? 1 : 0) + (last.Close < currentVwap ? 1 : 0) + (ema9 < ema21 ? 1 : 0);
        var side = longChecks == 3 ? "LONG" : shortChecks == 3 ? "SHORT" : "WAIT";
        var triggered = side != "WAIT";
        var entry = triggered ? RoundToTick(last.Close) : (decimal?)null;
        var atr = AverageRange(candles.TakeLast(14).ToList());
        var risk = entry is null ? 0 : Math.Max(atr * 1.2m, entry.Value * .002m);
        decimal? stop = entry is null ? null : RoundToTick(entry.Value - (side == "SHORT" ? -risk : risk));
        decimal? target = entry is null ? null : RoundToTick(entry.Value + (side == "SHORT" ? -risk * 2 : risk * 2));
        var triggerBuffer = .05m;

        return new TradePlan
        {
            Strategy = "Opening Range Breakout + VWAP",
            Side = side,
            Triggered = triggered,
            Entry = entry,
            Stop = stop,
            Target = target,
            WatchLong = RoundToTick(rangeHigh + triggerBuffer),
            WatchShort = RoundToTick(rangeLow - triggerBuffer),
            RiskReward = triggered ? 2 : null,
            QuantityForOnePercentRisk = risk > 0 ? Math.Floor(1000m / risk) : null,
            RuleChecksPassed = Math.Max(longChecks, shortChecks),
            TriggerDescription = $"First 15-minute range: {rangeLow:N2}–{rangeHigh:N2}; VWAP {currentVwap:N2}; EMA 9 {ema9:N2}, EMA 21 {ema21:N2}.",
            InvalidatedWhen = side == "LONG" ? $"5-minute close below {stop:N2}." : side == "SHORT" ? $"5-minute close above {stop:N2}." : "No trade until a completed candle confirms all three rules.",
            Explanation = triggered ? "All three rule checks agree on the latest completed bar. This is a conditional model entry, not an order." : "No complete setup on the latest bar. Watch both range edges; do not force an entry."
        };
    }

    private static TradePlan BuildSwingPlan(StockSnapshot stock)
    {
        var candles = stock.DailyCandles;
        if (candles.Count < 51)
        {
            return WaitPlan("Daily EMA Pullback", "At least 51 daily candles are required to warm up EMA(50).");
        }

        var closes = candles.Select(candle => candle.Close).ToList();
        var ema20Series = EmaSeries(closes, 20);
        var ema50Series = EmaSeries(closes, 50);
        var last = candles[^1];
        var previous = candles[^2];
        var longChecks = (ema20Series[^1] > ema50Series[^1] ? 1 : 0) + (previous.Low <= ema20Series[^2] ? 1 : 0) + (last.Close > ema20Series[^1] ? 1 : 0) + (last.Close > previous.High ? 1 : 0);
        var shortChecks = (ema20Series[^1] < ema50Series[^1] ? 1 : 0) + (previous.High >= ema20Series[^2] ? 1 : 0) + (last.Close < ema20Series[^1] ? 1 : 0) + (last.Close < previous.Low ? 1 : 0);
        var side = longChecks == 4 ? "LONG" : shortChecks == 4 ? "SHORT" : "WAIT";
        decimal? entry = side == "WAIT" ? null : RoundToTick(last.Close);
        var risk = entry is null ? 0 : Math.Max(AverageRange(candles.TakeLast(14).ToList()) * 1.5m, entry.Value * .01m);
        decimal? stop = entry is null ? null : RoundToTick(entry.Value - (side == "SHORT" ? -risk : risk));
        decimal? target = entry is null ? null : RoundToTick(entry.Value + (side == "SHORT" ? -risk * 2 : risk * 2));

        return new TradePlan
        {
            Strategy = "Daily EMA Pullback + Reclaim",
            Side = side,
            Triggered = side != "WAIT",
            Entry = entry,
            Stop = stop,
            Target = target,
            WatchLong = RoundToTick(previous.High + .05m),
            WatchShort = RoundToTick(previous.Low - .05m),
            RiskReward = side == "WAIT" ? null : 2,
            QuantityForOnePercentRisk = risk > 0 ? Math.Floor(1000m / risk) : null,
            RuleChecksPassed = Math.Max(longChecks, shortChecks),
            TriggerDescription = $"EMA 20 {ema20Series[^1]:N2}; EMA 50 {ema50Series[^1]:N2}; previous-bar reclaim level {previous.High:N2} / {previous.Low:N2}.",
            InvalidatedWhen = side == "LONG" ? $"Daily close below {stop:N2}. Recheck gap risk at the next open." : side == "SHORT" ? $"Daily close above {stop:N2}. Recheck gap risk at the next open." : "Wait for a completed daily pullback-and-reclaim setup.",
            Explanation = side == "WAIT" ? "The four daily trend and reclaim checks do not currently agree." : "All four daily checks agree. The signal is based on the latest candle; confirm next-session execution and event risk."
        };
    }

    private static TradePlan WaitPlan(string strategy, string reason) => new()
    {
        Strategy = strategy,
        Explanation = reason,
        TriggerDescription = reason,
        InvalidatedWhen = "No active plan."
    };

    private static IReadOnlyList<ResearchFinding> BuildFindings(StockSnapshot stock)
    {
        var candles = stock.DailyCandles;
        if (candles.Count < 50)
        {
            return [new("History", "Insufficient", "Need 50 or more daily candles for EMA trend context.", "caution")];
        }

        var closes = candles.Select(candle => candle.Close).ToList();
        var ema20 = Ema(closes, 20);
        var ema50 = Ema(closes, 50);
        var rsi = Rsi(closes, 14);
        var atr = AverageRange(candles.TakeLast(14).ToList());
        var averageVolume = candles.TakeLast(21).Take(20).Average(candle => candle.Volume);
        var volumeRatio = averageVolume > 0 ? candles[^1].Volume / averageVolume : 0;
        var fundamentals = stock.Fundamentals;
        var trend = stock.Price > ema20 && ema20 > ema50 ? "Uptrend" : stock.Price < ema20 && ema20 < ema50 ? "Downtrend" : "Mixed / range";

        var findings = new List<ResearchFinding>
        {
            new("Daily trend", trend, $"Price {stock.Price:N2} · EMA20 {ema20:N2} · EMA50 {ema50:N2}.", trend == "Uptrend" ? "good" : trend == "Downtrend" ? "caution" : "neutral"),
            new("RSI (14)", rsi.ToString("N1"), rsi >= 70 ? "Elevated momentum; chase risk is higher." : rsi <= 30 ? "Weak momentum; oversold is not itself a buy signal." : "Mid-range momentum; pair with trend and levels.", rsi is >= 70 or <= 30 ? "caution" : "neutral"),
            new("ATR (14)", $"₹{atr:N2} · {(stock.Price == 0 ? 0 : atr / stock.Price * 100):N2}%", "Mean high-low range proxy; gaps can exceed this estimate.", "neutral"),
            new("Volume vs 20-day avg", $"{volumeRatio:N2}×", "Volume confirmation uses the provider's daily candles; demo volume is synthetic.", volumeRatio >= 1.5m ? "good" : "neutral"),
            new("Relative strength", fundamentals?.RelativeStrengthPercent is decimal relative ? $"{relative:+0.00;-0.00}%" : "Not supplied", "Requires point-in-time benchmark-relative returns from your provider.", "neutral"),
            new("Valuation / growth / leverage", fundamentals is null ? "Not supplied" : "Provider fields available", fundamentals is null ? "No fundamentals are fabricated in demo mode. Add licensed, timestamped fundamentals to the provider payload." : "Review reporting period, peer group and filing date before using these fields.", "caution")
        };

        if (fundamentals is not null)
        {
            findings.Add(new("P/E", fundamentals.PriceEarnings?.ToString("N1") ?? "Not supplied", fundamentals.AsOf is DateTime asOf ? $"Provider snapshot: {asOf:yyyy-MM-dd}. Compare within sector." : "Provider did not include a fundamentals timestamp.", "neutral"));
            findings.Add(new("Revenue growth", fundamentals.RevenueGrowthPercent is decimal growth ? $"{growth:+0.0;-0.0}%" : "Not supplied", "Check quarter/year basis and reported-versus-estimated values.", growthTone(fundamentals.RevenueGrowthPercent)));
            findings.Add(new("Debt / equity", fundamentals.DebtToEquity?.ToString("N2") ?? "Not supplied", "Industry context matters; financial-sector leverage is not comparable to industrials.", "neutral"));
        }

        return findings;

        static string growthTone(decimal? growth) => growth > 0 ? "good" : growth < 0 ? "caution" : "neutral";
    }

    private static StockSnapshot FindStock(IReadOnlyList<StockSnapshot> stocks, string? symbol)
    {
        return stocks.FirstOrDefault(stock => string.Equals(stock.Symbol, symbol, StringComparison.OrdinalIgnoreCase)) ?? stocks.First();
    }

    private static decimal Ema(IReadOnlyList<decimal> values, int period) => EmaSeries(values, period).LastOrDefault();

    private static List<decimal> EmaSeries(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var alpha = 2m / (period + 1);
        var result = new List<decimal>(values.Count) { values[0] };
        for (var index = 1; index < values.Count; index++)
        {
            result.Add(values[index] * alpha + result[^1] * (1 - alpha));
        }
        return result;
    }

    private static decimal Vwap(IReadOnlyList<Candle> candles)
    {
        var totalVolume = candles.Sum(candle => candle.Volume);
        return totalVolume == 0 ? 0 : candles.Sum(candle => ((candle.High + candle.Low + candle.Close) / 3) * candle.Volume) / totalVolume;
    }

    private static decimal AverageRange(IReadOnlyList<Candle> candles) => candles.Count == 0 ? 0 : candles.Average(candle => candle.High - candle.Low);

    private static decimal Rsi(IReadOnlyList<decimal> closes, int period)
    {
        if (closes.Count <= period)
        {
            return 50;
        }
        decimal gains = 0, losses = 0;
        for (var index = closes.Count - period; index < closes.Count; index++)
        {
            var change = closes[index] - closes[index - 1];
            if (change > 0) gains += change; else losses -= change;
        }
        if (losses == 0) return gains == 0 ? 50 : 100;
        var relativeStrength = gains / losses;
        return 100 - 100 / (1 + relativeStrength);
    }

    private static decimal RoundToTick(decimal price) => Math.Round(price / .05m, MidpointRounding.AwayFromZero) * .05m;

    private static List<BacktestTrade> SimulateIntraday(StockSnapshot stock, int sessions, decimal capital)
    {
        var trades = new List<BacktestTrade>();
        var equity = capital;
        var selectedSessions = stock.IntradaySessions.TakeLast(sessions);
        foreach (var candles in selectedSessions)
        {
            if (candles.Count < 22) continue;
            var opening = candles.Take(3).ToList();
            var rangeHigh = opening.Max(candle => candle.High);
            var rangeLow = opening.Min(candle => candle.Low);
            var closes = candles.Select(candle => candle.Close).ToList();
            var ema9 = EmaSeries(closes, 9);
            var ema21 = EmaSeries(closes, 21);
            var side = 0;
            var entryIndex = -1;

            for (var index = 3; index < candles.Count; index++)
            {
                var bar = candles[index];
                var vwap = Vwap(candles.Take(index + 1).ToList());
                if (bar.Close > rangeHigh && bar.Close > vwap && ema9[index] > ema21[index]) { side = 1; entryIndex = index; break; }
                if (bar.Close < rangeLow && bar.Close < vwap && ema9[index] < ema21[index]) { side = -1; entryIndex = index; break; }
            }
            if (entryIndex < 0) continue;

            var entry = RoundToTick(candles[entryIndex].Close);
            var atr = AverageRange(candles.Skip(Math.Max(0, entryIndex - 13)).Take(Math.Min(14, entryIndex + 1)).ToList());
            var risk = Math.Max(atr * 1.2m, entry * .002m);
            var stop = entry - side * risk;
            var target = entry + side * risk * 2;
            var exit = candles[^1].Close;
            var exitReason = "Session close";

            for (var index = entryIndex + 1; index < candles.Count; index++)
            {
                var bar = candles[index];
                var stopHit = side == 1 ? bar.Low <= stop : bar.High >= stop;
                var targetHit = side == 1 ? bar.High >= target : bar.Low <= target;
                if (!stopHit && !targetHit) continue;
                exit = stopHit ? stop : target;
                exitReason = stopHit ? "Stop (conservative same-bar priority)" : "2R target";
                break;
            }

            AddTrade(trades, ref equity, candles[entryIndex].Time.ToString("yyyy-MM-dd"), side, entry, exit, risk, exitReason);
        }
        return trades;
    }

    private static List<BacktestTrade> SimulateSwing(StockSnapshot stock, int sessions, decimal capital)
    {
        var candles = stock.DailyCandles;
        var trades = new List<BacktestTrade>();
        if (candles.Count < 52) return trades;
        var closes = candles.Select(candle => candle.Close).ToList();
        var ema20 = EmaSeries(closes, 20);
        var ema50 = EmaSeries(closes, 50);
        var firstSignalIndex = Math.Max(50, candles.Count - sessions);
        var equity = capital;
        var index = firstSignalIndex;

        while (index < candles.Count - 1)
        {
            var previous = candles[index - 1];
            var current = candles[index];
            var longSetup = ema20[index] > ema50[index] && previous.Low <= ema20[index - 1] && current.Close > ema20[index] && current.Close > previous.High;
            var shortSetup = ema20[index] < ema50[index] && previous.High >= ema20[index - 1] && current.Close < ema20[index] && current.Close < previous.Low;
            if (!longSetup && !shortSetup) { index++; continue; }

            var side = longSetup ? 1 : -1;
            var entry = RoundToTick(current.Close);
            var atrBars = candles.Skip(Math.Max(0, index - 13)).Take(Math.Min(14, index + 1)).ToList();
            var risk = Math.Max(AverageRange(atrBars) * 1.5m, entry * .01m);
            var stop = entry - side * risk;
            var target = entry + side * risk * 2;
            var endIndex = Math.Min(index + 10, candles.Count - 1);
            var exit = candles[endIndex].Close;
            var exitReason = "10-session time exit";

            for (var forward = index + 1; forward <= endIndex; forward++)
            {
                var bar = candles[forward];
                var stopHit = side == 1 ? bar.Low <= stop : bar.High >= stop;
                var targetHit = side == 1 ? bar.High >= target : bar.Low <= target;
                if (!stopHit && !targetHit) continue;
                exit = stopHit ? stop : target;
                exitReason = stopHit ? "Stop (conservative same-bar priority)" : "2R target";
                endIndex = forward;
                break;
            }

            AddTrade(trades, ref equity, current.Time.ToString("yyyy-MM-dd"), side, entry, exit, risk, exitReason);
            index = endIndex + 1;
        }
        return trades;
    }

    private static void AddTrade(List<BacktestTrade> trades, ref decimal equity, string session, int side, decimal entry, decimal exit, decimal riskPerShare, string reason)
    {
        var quantity = Math.Floor(equity * RiskFraction / riskPerShare);
        if (quantity < 1) return;
        var gross = (exit - entry) * side * quantity;
        var costs = entry * quantity * RoundTripCostFraction;
        var net = gross - costs;
        var returnPercent = equity == 0 ? 0 : net / equity * 100;
        equity += net;
        trades.Add(new BacktestTrade(session, side == 1 ? "LONG" : "SHORT", entry, exit, net, returnPercent, reason));
    }

    private static BacktestViewModel Summarize(StockSnapshot stock, string strategy, int sessions, decimal capital, IReadOnlyList<BacktestTrade> trades, MarketFeed feed)
    {
        var winners = trades.Where(trade => trade.NetPnl > 0).ToList();
        var losers = trades.Where(trade => trade.NetPnl <= 0).ToList();
        var equity = capital;
        var peak = capital;
        var maxDrawdown = 0m;
        foreach (var trade in trades)
        {
            equity += trade.NetPnl;
            peak = Math.Max(peak, equity);
            if (peak > 0) maxDrawdown = Math.Max(maxDrawdown, (peak - equity) / peak * 100);
        }
        var grossProfit = winners.Sum(trade => trade.NetPnl);
        var grossLoss = Math.Abs(losers.Sum(trade => trade.NetPnl));
        var profitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 99.99m : 0;

        return new BacktestViewModel
        {
            Symbol = stock.Symbol,
            Strategy = strategy,
            Sessions = sessions,
            SessionsRun = strategy == "intraday"
                ? Math.Min(sessions, stock.IntradaySessions.Count)
                : Math.Min(sessions, Math.Max(0, stock.DailyCandles.Count - 51)),
            StartingCapital = capital,
            EndingCapital = equity,
            NetReturnPercent = capital == 0 ? 0 : (equity / capital - 1) * 100,
            WinRatePercent = trades.Count == 0 ? 0 : (decimal)winners.Count / trades.Count * 100,
            ProfitFactor = profitFactor,
            MaxDrawdownPercent = maxDrawdown,
            EstimatedCostPercent = RoundTripCostFraction * 100,
            Trades = trades,
            FeedStatus = feed.Status,
            IsDemo = feed.IsDemo || stock.DataSource == "Synthetic demo"
        };
    }
}