# Task: Fix Migration Script Execution Issue

## Problem Description
The migration runner in `Analytics/src/ReceiptCollector.Analytics.Migrations/MigrationRunner.cs` has a critical bug in the script execution logic. When executing SQL scripts, it doesn't properly handle transaction rollback on failure.

## Current Issues
1. **Rollback Logic Flaw**: In lines 92-103 of `MigrationRunner.cs`, when an exception occurs during script execution, the code attempts to rollback the transaction but has a bug checking `transaction.Connection` which is always null after the transaction is created.

## Solution Steps

### Step 1: Fix Rollback Logic
Locate the problematic section in `MigrationRunner.cs` (lines 92-103):
```csharp
catch (Exception ex)
{
    try
    {
        if (transaction.Connection is not null)  // BUG: This will always be null
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
    }
    catch (Exception rollbackEx)
    {
        _logger.LogError(rollbackEx, "Failed to rollback transaction for script {ScriptName}.", script.Name);
    }

    _logger.LogError(ex, "Failed to apply script {ScriptName}. Transaction rolled back.", script.Name);
    throw;
}
```

**Fix**: Remove the null check since `transaction` is already available:
```csharp
catch (Exception ex)
{
    try
    {
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (Exception rollbackEx)
    {
        _logger.LogError(rollbackEx, "Failed to rollback transaction for script {ScriptName}.", script.Name);
    }

    _logger.LogError(ex, "Failed to apply script {ScriptName}. Transaction rolled back.", script.Name);
    throw;
}
```

### Step 2: Add Error Recovery
Add retry logic for transient failures:
- Implement exponential backoff for database connection issues
- Log detailed error information including the failing SQL statement

### Step 3: Test the Fix
Create unit tests to verify:
1. Successful script execution commits transaction
2. Failed script execution rolls back transaction properly
3. Multiple scripts execute in correct order
4. Already applied scripts are skipped correctly

## Files to Modify
- `Analytics/src/ReceiptCollector.Analytics.Migrations/MigrationRunner.cs`

## Testing Strategy
1. Create mock database for testing
2. Test with both valid and invalid SQL scripts
3. Verify transaction rollback behavior
4. Test script ordering and deduplication
