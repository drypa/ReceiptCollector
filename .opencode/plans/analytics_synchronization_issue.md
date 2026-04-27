# Task: Fix Receipt Synchronization Service Issue

## Problem Description
The `ReceiptSynchronizationHostedService` in the Analytics API has several critical issues that prevent proper synchronization of receipts from MongoDB to PostgreSQL.

## Current Issues

### 1. Database Connection Validation Timing (lines 20-30)
```csharp
// Problem: Connection validation happens AFTER service startup
try
{
    await _dbContext.Database.GetConnectionStringAsync(cancellationToken);
}
catch (Exception ex)
{
    throw new InvalidOperationException("Unable to connect to the analytics database. Ensure the database is created and accessible with the configured credentials.");
}
```

**Issue**: The connection validation occurs during `ConfigureServices` which happens at application startup, but if the PostgreSQL database doesn't exist yet, this will fail before migrations can run.

### 2. Missing Migration Check (lines 15-30)
The service doesn't verify that migrations have been applied before attempting synchronization.

### 3. Insufficient Error Handling
The catch block logs the error but re-throws without proper cleanup, which could leave the application in an unstable state.

## Solution Steps

### Step 1: Move Connection Validation to StartAsync
Move database connection validation from constructor to `StartAsync` method so migrations can run first.

**Before**:
```csharp
public ReceiptSynchronizationHostedService(ReceiptDbContext dbContext, ReceiptSynchronizationService synchronizationService)
{
    _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    _synchronizationService = synchronizationService ?? throw new ArgumentNullException(nameof(synchronizationService));

    // Validation happens here - too early!
    try
    {
        await _dbContext.Database.GetConnectionStringAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("Unable to connect to the analytics database...");
    }
}
```

**After**:
```csharp
public ReceiptSynchronizationHostedService(ReceiptDbContext dbContext, ReceiptSynchronizationService synchronizationService)
{
    _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    _synchronizationService = synchronizationService ?? throw new ArgumentNullException(nameof(synchronizationService));
}

public override async Task StartAsync(CancellationToken cancellationToken)
{
    // Validate connection here, after migrations have run
    try
    {
        await _dbContext.Database.GetConnectionStringAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("Unable to connect to the analytics database...");
    }
    
    // Continue with synchronization
    await _synchronizationService.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
}
```

### Step 2: Add Migration Verification
Add a method to verify migrations are applied:
```csharp
private async Task VerifyMigrationsAppliedAsync(CancellationToken cancellationToken)
{
    var pendingMigrations = await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
    if (pendingMigrations.Any())
    {
        throw new InvalidOperationException("Database migrations have not been applied. Run the migration project first.");
    }
}
```

### Step 3: Improve Error Handling
Enhance error handling to:
- Log detailed error information
- Attempt graceful recovery for transient failures
- Provide clear guidance in error messages

## Files to Modify
- `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs` (lines 15-40)
- Consider adding a new service class or modifying existing synchronization service

## Testing Strategy
1. Test with database not yet created (should fail gracefully)
2. Test after migrations are applied (should succeed)
3. Test with connection string issues
4. Verify error messages are helpful for troubleshooting
