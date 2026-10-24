using ReceiptCollector.Analytics.Api.Middleware;
using ReceiptCollector.Analytics.Api.Modules.Commodities;
using ReceiptCollector.Analytics.Api.Modules.Merchants;
using ReceiptCollector.Analytics.Api.Modules.PrecategorizedCommodities;
using ReceiptCollector.Analytics.Api.Modules.Receipts;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
// ADR-027 п.1: активность отмечается ДО статики — статика и SPA тоже считаются активностью (D3).
app.UseMiddleware<RequestActivityMiddleware>();
app.UseStaticFiles();
app.UseMiddleware<UserAuthCookieMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapReceiptEndpoints();
app.MapReceiptCategorizationEndpoints();
app.MapUserAuthEndpoints();
app.MapCommodityEndpoints();
app.MapMerchantEndpoints();
app.MapPrecategorizedCommodityEndpoints();
app.MapGet("/health", () => Results.Ok("healthy"));
app.MapFallbackToFile("/index.html");

app.Run();
