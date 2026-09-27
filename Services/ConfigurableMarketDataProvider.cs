using System.Net.Http.Json;
using System.Text.Json;
using RupeeDesk.Mvc.Models;

namespace RupeeDesk.Mvc.Services;

public sealed class ConfigurableMarketDataProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    DemoMarketDataProvider demoProvider,
    UpstoxMarketDataProvider upstoxProvider) : IMarketDataProvider
{
    public async Task<MarketFeed> GetFeedAsync(CancellationToken cancellationToken = default)
    {
        var upstoxToken = configuration["MarketData:UpstoxAnalyticsToken"];
        if (!string.IsNullOrWhiteSpace(upstoxToken))
        {
            try
            {
                return await upstoxProvider.GetFeedAsync(upstoxToken, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                return demoProvider.CreateFeed("Upstox feed unavailable; displaying synthetic demo data.");
            }
        }

        var endpoint = configuration["MarketData:QuotesEndpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            return demoProvider.CreateFeed();
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient();
            var stocks = await httpClient.GetFromJsonAsync<List<StockSnapshot>>(uri, cancellationToken);
            var validStocks = stocks?
                .Where(stock => !string.IsNullOrWhiteSpace(stock.Symbol) && stock.Price > 0)
                .ToList() ?? [];

            if (validStocks.Count == 0)
            {
                return demoProvider.CreateFeed("Provider returned no valid quotes; displaying synthetic demo data.");
            }

            foreach (var stock in validStocks)
            {
                stock.DataSource = "Configured provider";
                stock.AsOf = stock.AsOf == default ? DateTime.UtcNow : stock.AsOf;
            }

            var coverage = $"Configured provider: {validStocks.Count} symbol(s); quotes may be partial.";
            return new MarketFeed(validStocks, coverage, false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return demoProvider.CreateFeed("Provider unavailable; displaying synthetic demo data.");
        }
    }
}