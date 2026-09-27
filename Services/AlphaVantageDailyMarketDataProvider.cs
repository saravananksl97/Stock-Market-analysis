using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using RupeeDesk.Mvc.Models;

namespace RupeeDesk.Mvc.Services;

public sealed class AlphaVantageDailyMarketDataProvider(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache)
{
    private static readonly InstrumentDescriptor[] Universe =
    [
        new("RELIANCE", "Reliance Industries", "Energy", "RELIANCE.BSE"),
        new("HDFCBANK", "HDFC Bank", "Banking", "HDFCBANK.BSE"),
        new("INFY", "Infosys", "IT", "INFY.BSE"),
        new("SBIN", "State Bank of India", "Banking", "SBIN.BSE")
    ];

    public async Task<MarketFeed> GetFeedAsync(string apiKey, CancellationToken cancellationToken)
    {
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
        var cacheKey = $"alpha-vantage-bse-eod-{keyHash}";
        var cached = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            return await LoadFeedAsync(apiKey, cancellationToken);
        });

        return cached ?? throw new HttpRequestException("Alpha Vantage returned no BSE daily data.");
    }

    private async Task<MarketFeed> LoadFeedAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://www.alphavantage.co/");

        var requests = Universe.Select(item => LoadStockAsync(client, item, apiKey, cancellationToken));
        var stocks = (await Task.WhenAll(requests)).Where(stock => stock is not null).Cast<StockSnapshot>().ToList();
        if (stocks.Count == 0)
        {
            throw new HttpRequestException("Alpha Vantage returned no BSE daily candles for the configured symbols.");
        }

        var latestDate = stocks.Max(stock => stock.AsOf);
        var coverage = $"Alpha Vantage BSE end-of-day candles · {stocks.Count}/{Universe.Length} symbols · latest session {latestDate:yyyy-MM-dd} · daily only, no intraday · cached 12 hours.";
        return new MarketFeed(stocks, coverage, false);
    }

    private static async Task<StockSnapshot?> LoadStockAsync(
        HttpClient client,
        InstrumentDescriptor instrument,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var path = $"query?function=TIME_SERIES_DAILY&symbol={Uri.EscapeDataString(instrument.ApiSymbol)}&outputsize=compact&apikey={Uri.EscapeDataString(apiKey)}";
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        foreach (var errorName in new[] { "Error Message", "Information", "Note" })
        {
            if (root.TryGetProperty(errorName, out var error))
            {
                throw new HttpRequestException($"Alpha Vantage: {error.GetString() ?? "request failed"}");
            }
        }

        if (!root.TryGetProperty("Time Series (Daily)", out var timeSeries) || timeSeries.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var candles = new List<Candle>();
        foreach (var session in timeSeries.EnumerateObject())
        {
            if (!DateTime.TryParseExact(session.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                !TryReadDecimal(session.Value, "1. open", out var open) ||
                !TryReadDecimal(session.Value, "2. high", out var high) ||
                !TryReadDecimal(session.Value, "3. low", out var low) ||
                !TryReadDecimal(session.Value, "4. close", out var close) ||
                !TryReadDecimal(session.Value, "5. volume", out var volume) || close <= 0)
            {
                continue;
            }

            candles.Add(new Candle
            {
                Time = date,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }

        candles = candles.OrderBy(candle => candle.Time).ToList();
        if (candles.Count == 0)
        {
            return null;
        }

        var last = candles[^1];
        var previousClose = candles.Count > 1 ? candles[^2].Close : last.Close;
        var changePercent = previousClose > 0 ? (last.Close / previousClose - 1) * 100 : 0;
        var asOf = new DateTimeOffset(last.Time.Date.AddHours(15).AddMinutes(30), TimeSpan.FromHours(5.5)).UtcDateTime;

        return new StockSnapshot
        {
            Symbol = instrument.Symbol,
            Company = instrument.Company,
            Sector = instrument.Sector,
            Price = last.Close,
            ChangePercent = changePercent,
            DataSource = "Alpha Vantage · BSE end-of-day",
            AsOf = asOf,
            DailyCandles = candles
        };
    }

    private static bool TryReadDecimal(JsonElement session, string fieldName, out decimal value)
    {
        value = default;
        return session.TryGetProperty(fieldName, out var field) &&
            field.ValueKind == JsonValueKind.String &&
            decimal.TryParse(field.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private sealed record InstrumentDescriptor(string Symbol, string Company, string Sector, string ApiSymbol);
}