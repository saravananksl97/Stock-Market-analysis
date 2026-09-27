using RupeeDesk.Mvc.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DemoMarketDataProvider>();
builder.Services.AddScoped<UpstoxMarketDataProvider>();
builder.Services.AddScoped<IMarketDataProvider, ConfigurableMarketDataProvider>();
builder.Services.AddScoped<IMarketResearchService, MarketResearchService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Market/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Market}/{action=Index}/{id?}");

app.Run();