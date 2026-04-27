# Task: Fix Error Handling and Recovery Issue

## Problem Description
The Analytics services have inconsistent error handling that can lead to application crashes or silent failures.

## Current Issues

### 1. Inconsistent Exception Handling (lines 85-107)
```csharp
try
{
    await ExecuteScriptAsync(connection, transaction, scriptContent, cancellationToken).ConfigureAwait(false);
    await RecordScriptAsync(connection, transaction, script.Name, cancellationToken).ConfigureAwait(false);
    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    _logger.LogInformation("Script {ScriptName} applied successfully.", script.Name);
}
catch (Exception ex)
{
    try
    {
        if (transaction.Connection is not null)  // BUG: Always null
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
    }
    catch (Exception rollbackEx)
    {
        _logger.LogError(rollbackEx, "Failed to rollback transaction for script {ScriptName}.", script.Name);
    }

    _logger.LogError(ex, "Failed to apply script {ScriptName}. Transaction rolled back.", script.Name);
    throw;  // Re-throws immediately
}
```

**Issues**:
- Transactions not properly rolled back (as previously identified)
- No retry logic for transient failures
- Immediate re-throw without recovery attempts
- No circuit breaker pattern

### 2. Missing Graceful Degradation
Services fail completely instead of degrading gracefully when dependencies are unavailable.

## Solution Steps

### Step 1: Implement Retry Policy
Add Polly retry policy for database operations:
```csharp
// Add to DI configuration
services.AddTransient<AsyncRetryPolicy>(sp => Policy
    .Handle<NpgsqlException>()
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
```

### Step 2: Fix Transaction Rollback
Fix the rollback logic as previously identified in the migration issue task.

### Step 3: Add Circuit Breaker
Add circuit breaker for database operations:
```csharp
services.AddTransient<AsyncCircuitBreakerPolicy>(sp => Policy
    .Handle<NpgsqlException>()
    .CircuitBreakerAsync(3, TimeSpan.FromMinutes(1), OnBreak, OnReset, OnHalfOpen));
```

### Step 4: Implement Graceful Degradation
Add fallback mechanisms:
- Cache last known good state
- Return stale data when primary source fails
- Log degradation events

## Files to Modify
- `Analytics/src/ReceiptCollector.Analytics.Migrations/MigrationRunner.cs` (lines 85-107)
- Consider adding Polly package for resilience patterns

## Testing Strategy
1. Test retry behavior with simulated failures
2. Verify circuit breaker trips and resets correctly
3. Test graceful degradation scenarios
4. Verify proper rollback on failure
