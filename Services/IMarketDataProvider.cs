using RupeeDesk.Mvc.Models;

namespace RupeeDesk.Mvc.Services;

public interface IMarketDataProvider
{
    Task<MarketFeed> GetFeedAsync(CancellationToken cancellationToken = default);
}