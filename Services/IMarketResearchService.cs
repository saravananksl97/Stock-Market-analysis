using RupeeDesk.Mvc.Models;

namespace RupeeDesk.Mvc.Services;

public interface IMarketResearchService
{
    Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IntradayViewModel> GetIntradayAsync(string? symbol, CancellationToken cancellationToken = default);
    Task<SwingViewModel> GetSwingAsync(string? symbol, CancellationToken cancellationToken = default);
    Task<ResearchViewModel> GetResearchAsync(string? symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StrategyInfo>> GetStrategiesAsync();
    Task<BacktestViewModel> GetBacktestAsync(string? symbol, string? strategy, int sessions, decimal capital, CancellationToken cancellationToken = default);
}