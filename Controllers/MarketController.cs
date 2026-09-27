using Microsoft.AspNetCore.Mvc;
using RupeeDesk.Mvc.Services;

namespace RupeeDesk.Mvc.Controllers;

public sealed class MarketController(IMarketResearchService researchService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await researchService.GetDashboardAsync(cancellationToken));

    public async Task<IActionResult> Intraday(string? symbol, CancellationToken cancellationToken) =>
        View(await researchService.GetIntradayAsync(symbol, cancellationToken));

    public async Task<IActionResult> Swing(string? symbol, CancellationToken cancellationToken) =>
        View(await researchService.GetSwingAsync(symbol, cancellationToken));

    public async Task<IActionResult> Research(string? symbol, CancellationToken cancellationToken) =>
        View(await researchService.GetResearchAsync(symbol, cancellationToken));

    public async Task<IActionResult> Strategies() => View(await researchService.GetStrategiesAsync());

    public async Task<IActionResult> Backtest(string? symbol, string? strategy, int sessions = 60, decimal capital = 100000, CancellationToken cancellationToken = default) =>
        View(await researchService.GetBacktestAsync(symbol, strategy, sessions, capital, cancellationToken));

    public IActionResult Error() => View();
}