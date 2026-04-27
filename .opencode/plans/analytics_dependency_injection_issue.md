# Task: Fix Dependency Injection Configuration Issue

## Problem Description
The `AddInfrastructure` extension method in `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` has a critical flaw in how it configures the PostgreSQL database context.

## Current Issues

### 1. Missing DbContext Lifetime Configuration (lines 49-60)
```csharp
services.AddDbContext<ReceiptDbContext>((sp, builder) =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("Postgres connection string is not configured.");
    }

    builder
        .UseNpgsql(options.ConnectionString)
        .UseSnakeCaseNamingConvention();
});
```

**Issue**: The DbContext is registered without specifying a lifetime scope. This can lead to:
- Memory leaks if not properly scoped
- Connection pool exhaustion
- Thread safety issues in web applications

### 2. Missing Migration Configuration
The configuration doesn't enable automatic migrations, which means the application won't know if the database schema is out of sync.

## Solution Steps

### Step 1: Add Proper Scoping
Add `.AddDbContextScoped()` or explicitly specify the lifetime:
```csharp
services.AddDbContext<ReceiptDbContext>((sp, builder) =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("Postgres connection string is not configured.");
    }

    builder
        .UseNpgsql(options.ConnectionString)
        .UseSnakeCaseNamingConvention();
}, ServiceLifetime.Scoped);  // Add this line
```

### Step 2: Enable Migrations
Add migration configuration:
```csharp
services.AddDbContext<ReceiptDbContext>((sp, builder) =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("Postgres connection string is not configured.");
    }

    builder
        .UseNpgsql(options.ConnectionString)
        .UseSnakeCaseNamingConvention()
        .EnableSensitiveDataLogging(false)  // For development only
        .EnableDetailedErrors(false);      // For production
}, ServiceLifetime.Scoped);
```

### Step 3: Add Health Checks (Optional Enhancement)
Add database health checks to monitor connection status:
```csharp
services.AddHealthChecks()
    .AddDbContextCheck<ReceiptDbContext>();
```

## Files to Modify
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` (lines 49-60)

## Testing Strategy
1. Verify DbContext is properly scoped in web requests
2. Test connection pooling behavior under load
3. Verify migrations are detected and applied correctly
4. Check for memory leaks during stress testing
