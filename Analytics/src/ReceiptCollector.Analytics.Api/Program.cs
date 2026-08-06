using ReceiptCollector.Analytics.Api.Middleware;
using ReceiptCollector.Analytics.Api.Modules.Commodities;
using ReceiptCollector.Analytics.Api.Modules.Merchants;
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
app.UseStaticFiles();
app.UseMiddleware<UserAuthCookieMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapReceiptEndpoints();
app.MapUserAuthEndpoints();
app.MapCommodityEndpoints();
app.MapMerchantEndpoints();
app.MapFallbackToFile("/index.html");

app.Run();
