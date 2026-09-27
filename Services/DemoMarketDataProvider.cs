using RupeeDesk.Mvc.Models;

namespace RupeeDesk.Mvc.Services;

public sealed class DemoMarketDataProvider
{
    private static readonly (string Symbol, string Company, string Sector, decimal BasePrice, int Seed)[] Universe =
    [
        ("RELIANCE", "Reliance Industries", "Energy", 1420m, 17),
        ("HDFCBANK", "HDFC Bank", "Banking", 1768m, 31),
        ("ICICIBANK", "ICICI Bank", "Banking", 1282m, 46),
        ("INFY", "Infosys", "IT", 1540m, 58),
        ("TCS", "Tata Consultancy Services", "IT", 3185m, 73),
        ("SBIN", "State Bank of India", "Banking", 812m, 88),
        ("LT", "Larsen & Toubro", "Industrials", 3560m, 103),
        ("AXISBANK", "Axis Bank", "Banking", 1148m, 129)
    ];

    public MarketFeed CreateFeed(string status = "Synthetic sample prices; not live NSE data.")
    {
        var stocks = Universe.Select(BuildStock).ToList();
        return new MarketFeed(stocks, status, true);
    }

    private static StockSnapshot BuildStock((string Symbol, string Company, string Sector, decimal BasePrice, int Seed) item)
    {
        var random = new Random(item.Seed);
        var daily = new List<Candle>(180);
        var intradaySessions = new List<List<Candle>>(120);
        var previousClose = item.BasePrice * .91m;

        for (var dayIndex = 179; dayIndex >= 0; dayIndex--)
        {
            var sessionDate = DateTime.Today.AddDays(-dayIndex);
            var bars = new List<Candle>(75);
            var close = previousClose;

            for (var barIndex = 0; barIndex < 75; barIndex++)
            {
                var open = close;
                var drift = ((decimal)random.NextDouble() - .49m) * item.BasePrice * .0015m;
                close = Math.Max(item.BasePrice * .65m, open + drift);
                var spread = item.BasePrice * (.0003m + (decimal)random.NextDouble() * .0006m);
                var time = sessionDate.Date.AddHours(9).AddMinutes(15 + barIndex * 5);
                bars.Add(new Candle
                {
                    Time = time,
                    Open = open,
                    Close = close,
                    High = Math.Max(open, close) + spread,
                    Low = Math.Min(open, close) - spread,
                    Volume = 4000 + (decimal)random.NextDouble() * 24000
                });
            }

            daily.Add(new Candle
            {
                Time = sessionDate.Date,
                Open = bars[0].Open,
                Close = bars[^1].Close,
                High = bars.Max(bar => bar.High),
                Low = bars.Min(bar => bar.Low),
                Volume = bars.Sum(bar => bar.Volume)
            });
            if (dayIndex < 120)
            {
                intradaySessions.Add(bars);
            }
            previousClose = close;
        }

        var change = daily.Count > 1 ? (daily[^1].Close / daily[^2].Close - 1) * 100 : 0;
        return new StockSnapshot
        {
            Symbol = item.Symbol,
            Company = item.Company,
            Sector = item.Sector,
            Price = daily[^1].Close,
            ChangePercent = change,
            DataSource = "Synthetic demo",
            AsOf = DateTime.UtcNow,
            DailyCandles = daily,
            IntradaySessions = intradaySessions,
            IntradayCandles = intradaySessions[^1]
        };
    }
}