using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using RupeeDesk.Mvc.Models;

namespace RupeeDesk.Mvc.Services;

public sealed class UpstoxMarketDataProvider(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache)
{
    private static readonly InstrumentDescriptor[] Universe =
    [
        new("RELIANCE", "Reliance Industries", "Energy", "NSE_EQ|INE002A01018"),
        new("HDFCBANK", "HDFC Bank", "Banking", "NSE_EQ|INE040A01034"),
        new("ICICIBANK", "ICICI Bank", "Banking", "NSE_EQ|INE090A01021"),
        new("INFY", "Infosys", "IT", "NSE_EQ|INE009A01021"),
        new("TCS", "Tata Consultancy Services", "IT", "NSE_EQ|INE467B01029"),
        new("SBIN", "State Bank of India", "Banking", "NSE_EQ|INE062A01020"),
        new("LT", "Larsen & Toubro", "Industrials", "NSE_EQ|INE018A01030"),
        new("AXISBANK", "Axis Bank", "Banking", "NSE_EQ|INE238A01034")
    ];

    public async Task<MarketFeed> GetFeedAsync(string token, CancellationToken cancellationToken)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var cacheKey = $"upstox-feed-{tokenHash}";
        var cached = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20);
            return await LoadFeedAsync(token, cancellationToken);
        });
        return cached ?? throw new InvalidOperationException("Upstox returned no market feed.");
    }

    private async Task<MarketFeed> LoadFeedAsync(string token, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.upstox.com/");

        var keys = string.Join(",", Universe.Select(item => item.InstrumentKey));
        var quotes = await GetAsync<UpstoxEnvelope<Dictionary<string, UpstoxQuote>>>(
            client,
            $"v2/market-quote/quotes?instrument_key={Uri.EscapeDataString(keys)}",
            token,
            cancellationToken);

        if (!string.Equals(quotes.Status, "success", StringComparison.OrdinalIgnoreCase) || quotes.Data is null)
        {
            throw new HttpRequestException("Upstox quote response was not successful.");
        }

        var indiaNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
        var toDate = indiaNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var intradayFromDate = indiaNow.AddDays(-29).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dailyFromDate = indiaNow.AddDays(-400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var stocks = new List<StockSnapshot>();

        foreach (var item in Universe)
        {
            var quote = quotes.Data.Values.FirstOrDefault(value =>
                string.Equals(value.InstrumentToken, item.InstrumentKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.Symbol, item.Symbol, StringComparison.OrdinalIgnoreCase));
            if (quote is null || quote.LastPrice <= 0)
            {
                continue;
            }

            List<Candle> intradayCandles = [];
            List<Candle> dailyCandles = [];
            var candleDataAvailable = true;

            try
            {
                var intradayPath = $"v3/historical-candle/{Uri.EscapeDataString(item.InstrumentKey)}/minutes/5/{toDate}/{intradayFromDate}";
                var dailyPath = $"v3/historical-candle/{Uri.EscapeDataString(item.InstrumentKey)}/days/1/{toDate}/{dailyFromDate}";
                var intradayTask = GetAsync<UpstoxEnvelope<CandleData>>(client, intradayPath, token, cancellationToken);
                var dailyTask = GetAsync<UpstoxEnvelope<CandleData>>(client, dailyPath, token, cancellationToken);
                await Task.WhenAll(intradayTask, dailyTask);
                intradayCandles = ParseCandles(intradayTask.Result.Data?.Candles);
                dailyCandles = ParseCandles(dailyTask.Result.Data?.Candles);
                candleDataAvailable = intradayCandles.Count >= 22 && dailyCandles.Count >= 51;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                candleDataAvailable = false;
            }

            var intradaySessions = intradayCandles
                .GroupBy(candle => candle.Time.Date)
                .OrderBy(group => group.Key)
                .Select(group => group.OrderBy(candle => candle.Time).ToList())
                .ToList();
            var previousClose = quote.LastPrice - quote.NetChange;
            var changePercent = previousClose > 0 ? quote.NetChange / previousClose * 100 : 0;

            stocks.Add(new StockSnapshot
            {
                Symbol = item.Symbol,
                Company = item.Company,
                Sector = item.Sector,
                Price = quote.LastPrice,
                ChangePercent = changePercent,
                DataSource = candleDataAvailable ? "Upstox Analytics API" : "Upstox quotes · candle history unavailable",
                AsOf = ParseQuoteTime(quote),
                IntradayCandles = intradaySessions.LastOrDefault() ?? [],
                IntradaySessions = intradaySessions,
                DailyCandles = dailyCandles
            });
        }

        if (stocks.Count == 0)
        {
            throw new HttpRequestException("Upstox returned no quotes for the configured NSE universe.");
        }

        var latestQuote = stocks.Max(stock => stock.AsOf);
        var coverage = $"Upstox official read-only feed · {stocks.Count}/{Universe.Length} quotes · latest quote timestamp {latestQuote:yyyy-MM-dd HH:mm} UTC · cached for 20 seconds.";
        return new MarketFeed(stocks, coverage, false);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new JsonException("Upstox returned an empty JSON response.");
    }

    private static List<Candle> ParseCandles(List<JsonElement[]>? rows)
    {
        if (rows is null)
        {
            return [];
        }

        var candles = new List<Candle>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Length < 6 || row[0].ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(row[0].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp) ||
                !ReadDecimal(row[1], out var open) || !ReadDecimal(row[2], out var high) ||
                !ReadDecimal(row[3], out var low) || !ReadDecimal(row[4], out var close) ||
                !ReadDecimal(row[5], out var volume))
            {
                continue;
            }

            candles.Add(new Candle
            {
                Time = DateTime.SpecifyKind(timestamp.DateTime, DateTimeKind.Unspecified),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }

        return candles.OrderBy(candle => candle.Time).ToList();
    }

    private static bool ReadDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDecimal(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        value = default;
        return false;
    }

    private static DateTime ParseQuoteTime(UpstoxQuote quote)
    {
        if (DateTimeOffset.TryParse(quote.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            return timestamp.UtcDateTime;
        }

        if (long.TryParse(quote.LastTradeTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixMilliseconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime;
        }

        return DateTime.UtcNow;
    }

    private sealed record InstrumentDescriptor(string Symbol, string Company, string Sector, string InstrumentKey);

    private sealed class UpstoxEnvelope<T>
    {
        public string Status { get; set; } = "";
        public T? Data { get; set; }
    }

    private sealed class CandleData
    {
        public List<JsonElement[]> Candles { get; set; } = [];
    }

    private sealed class Ohlc
    {
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
    }

    private sealed class UpstoxQuote
    {
        [JsonPropertyName("instrument_token")]
        public string InstrumentToken { get; set; } = "";

        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = "";

        [JsonPropertyName("last_price")]
        public decimal LastPrice { get; set; }

        [JsonPropertyName("net_change")]
        public decimal NetChange { get; set; }

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = "";

        [JsonPropertyName("last_trade_time")]
        public string LastTradeTime { get; set; } = "";

        [JsonPropertyName("ohlc")]
        public Ohlc? Ohlc { get; set; }
    }
}