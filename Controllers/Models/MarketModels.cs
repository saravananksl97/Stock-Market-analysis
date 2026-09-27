namespace RupeeDesk.Mvc.Models;

public sealed class Candle
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

public sealed class Fundamentals
{
    public decimal? MarketCapCrore { get; set; }
    public decimal? PriceEarnings { get; set; }
    public decimal? RevenueGrowthPercent { get; set; }
    public decimal? DebtToEquity { get; set; }
    public decimal? RelativeStrengthPercent { get; set; }
    public DateTime? AsOf { get; set; }
}

public sealed class StockSnapshot
{
    public string Symbol { get; set; } = "";
    public string Company { get; set; } = "";
    public string Sector { get; set; } = "";
    public decimal Price { get; set; }
    public decimal ChangePercent { get; set; }
    public string DataSource { get; set; } = "Synthetic demo";
    public DateTime AsOf { get; set; }
    public Fundamentals? Fundamentals { get; set; }
    public List<Candle> IntradayCandles { get; set; } = [];
    public List<Candle> DailyCandles { get; set; } = [];
    public List<List<Candle>> IntradaySessions { get; set; } = [];
}

public sealed record MarketFeed(IReadOnlyList<StockSnapshot> Stocks, string Status, bool IsDemo);

public sealed class TradePlan
{
    public string Strategy { get; set; } = "";
    public string Side { get; set; } = "WAIT";
    public bool Triggered { get; set; }
    public decimal? Entry { get; set; }
    public decimal? Stop { get; set; }
    public decimal? Target { get; set; }
    public decimal? WatchLong { get; set; }
    public decimal? WatchShort { get; set; }
    public decimal? RiskReward { get; set; }
    public decimal? QuantityForOnePercentRisk { get; set; }
    public int RuleChecksPassed { get; set; }
    public string TriggerDescription { get; set; } = "";
    public string InvalidatedWhen { get; set; } = "";
    public string Explanation { get; set; } = "";
}

public sealed record ScannerRow(StockSnapshot Stock, TradePlan Plan, decimal Rsi, decimal Ema20, decimal Ema50, decimal AtrPercent);

public sealed class DashboardViewModel
{
    public required IReadOnlyList<ScannerRow> Candidates { get; init; }
    public required string FeedStatus { get; init; }
    public required bool IsDemo { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

public sealed class IntradayViewModel
{
    public required IReadOnlyList<ScannerRow> Candidates { get; init; }
    public required StockSnapshot SelectedStock { get; init; }
    public required TradePlan SelectedPlan { get; init; }
    public required string FeedStatus { get; init; }
    public required bool IsDemo { get; init; }
}

public sealed record ResearchFinding(string Label, string Value, string Interpretation, string Tone);

public sealed class SwingViewModel
{
    public required IReadOnlyList<ScannerRow> Candidates { get; init; }
    public required StockSnapshot SelectedStock { get; init; }
    public required TradePlan SelectedPlan { get; init; }
    public required IReadOnlyList<ResearchFinding> Findings { get; init; }
    public required string FeedStatus { get; init; }
    public required bool IsDemo { get; init; }
}

public sealed record StrategyInfo(string Name, string Horizon, string EntryRules, string ExitRules, string FailureModes, string BestUse);

public sealed record BacktestTrade(string Session, string Side, decimal Entry, decimal Exit, decimal NetPnl, decimal ReturnPercent, string ExitReason);

public sealed class BacktestViewModel
{
    public string Symbol { get; init; } = "RELIANCE";
    public string Strategy { get; init; } = "intraday";
    public int Sessions { get; init; } = 60;
    public int SessionsRun { get; init; }
    public decimal StartingCapital { get; init; } = 100000;
    public decimal EndingCapital { get; init; }
    public decimal NetReturnPercent { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal EstimatedCostPercent { get; init; }
    public IReadOnlyList<BacktestTrade> Trades { get; init; } = [];
    public string FeedStatus { get; init; } = "";
    public bool IsDemo { get; init; }
}

public sealed class ResearchViewModel
{
    public required StockSnapshot Stock { get; init; }
    public required IReadOnlyList<StockSnapshot> Stocks { get; init; }
    public required TradePlan IntradayPlan { get; init; }
    public required TradePlan SwingPlan { get; init; }
    public required IReadOnlyList<ResearchFinding> Findings { get; init; }
    public required string FeedStatus { get; init; }
    public required bool IsDemo { get; init; }
}