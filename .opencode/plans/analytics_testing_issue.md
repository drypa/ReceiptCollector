# Task: Fix Testing Infrastructure Issue

## Problem Description
The Analytics project lacks comprehensive testing infrastructure and has inconsistent test coverage.

## Current Issues

### 1. Missing Test Projects
- No unit test projects for core libraries
- No integration test projects
- No end-to-end test projects

### 2. Incomplete Test Coverage
Existing tests (if any) likely don't cover:
- Database operations
- Error handling scenarios
- Edge cases
- Performance scenarios

### 3. Missing Test Data Setup
No standardized way to set up test data across different test types.

## Solution Steps

### Step 1: Create Test Projects Structure
Create proper test project structure:
```
Analytics/
├── src/
│   ├── ReceiptCollector.Analytics.Api/
│   │   └── Tests/ (Unit tests for API)
│   ├── ReceiptCollector.Analytics.Infrastructure/
│   │   └── Tests/ (Unit tests for infrastructure)
│   └── ReceiptCollector.Analytics.Migrations/
│       └── Tests/ (Unit tests for migrations)
└── tests/
    ├── IntegrationTests/ (Integration tests)
    └── EndToEndTests/ (E2E tests)
```

### Step 2: Add Testing Packages
Add necessary NuGet packages:
- `Microsoft.NET.Test.Sdk`
- `xunit` or `nunit`
- `Moq` for mocking
- `FluentAssertions` for assertions
- `Respawn` for database testing
- `NSubstitute` as alternative to Moq

### Step 3: Create Test Infrastructure
Create shared test infrastructure:
```csharp
// TestBase.cs
public abstract class TestBase : IDisposable
{
    protected TestBase()
    {
        // Setup common test dependencies
    }

    public void Dispose()
    {
        // Cleanup
    }
}
```

### Step 4: Add Database Testing Support
Add database testing support:
```csharp
// TestDatabaseFactory.cs
public class TestDatabaseFactory : IDisposable
{
    private readonly RespawnGenerator _respawnGenerator;
    
    public TestDatabaseFactory()
    {
        _respawnGenerator = RespawnGenerator.New("migrations");
    }
    
    public async Task ResetDatabaseAsync(ReceiptDbContext context)
    {
        await _respawnGenerator.ResetAsync(context);
    }
}
```

### Step 5: Add Test Data Builders
Create test data builders:
```csharp
// ReceiptBuilder.cs
public class ReceiptBuilder
{
    private readonly Receipt _receipt = new Receipt();
    
    public ReceiptBuilder WithId(string id)
    {
        _receipt.Id = id;
        return this;
    }
    
    // Other builder methods...
    
    public Receipt Build() => _receipt;
}
```

## Files to Create
- Test project files (xunit/nunit)
- Shared test infrastructure
- Database testing utilities
- Test data builders

## Testing Strategy
1. Start with unit tests for core logic
2. Add integration tests for database operations
3. Add E2E tests for complete workflows
4. Implement CI pipeline to run all tests
