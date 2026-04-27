# Task: Fix Logging Configuration Issue

## Problem Description
The Analytics services have inconsistent and incomplete logging configuration, which makes debugging difficult.

## Current Issues

### 1. Missing Structured Logging (lines 20-35)
```csharp
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
```

**Issues**:
- Uses `AddSimpleConsole` instead of proper structured logging
- No JSON formatting for easier parsing
- Missing correlation IDs for distributed tracing
- No log level configuration

### 2. Inconsistent Logging Between Projects
- Migrations project uses simple console logging
- API project may use different configuration
- No centralized logging strategy

## Solution Steps

### Step 1: Standardize on Serilog
Add Serilog for structured JSON logging:
```csharp
// Add to Program.cs
builder.Host.UseSerilog((ctx, lc) => lc
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss } {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .ReadFrom.Configuration(ctx.Configuration));
```

### Step 2: Configure Log Levels
Add log level configuration:
```csharp
builder.Services.Configure<LoggerFilterOptions>(options =>
{
    options.MinLevel = LogLevel.Information;
    options.Rules.Add(new LoggerFilterRule("Microsoft", LogLevel.Warning));
    options.Rules.Add(new LoggerFilterRule("System", LogLevel.Warning));
});
```

### Step 3: Add Correlation IDs
Add correlation ID enrichment:
```csharp
builder.Host.UseSerilog((ctx, lc) => lc
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId()
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss } {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .ReadFrom.Configuration(ctx.Configuration));
```

### Step 4: Add Health Checks Logging
Enhance health checks with logging:
```csharp
services.AddHealthChecks()
    .AddDbContextCheck<ReceiptDbContext>(options => options.ResultStatusCodes[HealthCheckResult.Healthy] = StatusCodes.Status200OK)
    .AddNpgSql(connectionString, healthQuery: "SELECT 1", name: "postgres-db");
```

## Files to Modify
- `Analytics/src/ReceiptCollector.Analytics.Migrations/Program.cs` (lines 20-35)
- `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs`

## Testing Strategy
1. Verify JSON log format is correct
2. Test correlation IDs propagate through requests
3. Verify log levels filter correctly
4. Check health check logging in logs
