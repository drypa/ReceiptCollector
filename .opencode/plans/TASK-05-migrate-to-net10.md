# Migration Plan: Analytics Microservice from .NET 8 to .NET 10 LTS

## Current State Analysis
- The Analytics microservice consists of multiple projects targeting .NET 8.0
- Main API project uses ASP.NET Core 8.0
- Migrations and infrastructure projects also use .NET 8.0
- Configuration files are present for development and production environments

## Migration Plan

### Phase 1: Update Project Files
1. **API Project** (`ReceiptCollector.Analytics.Api.csproj`):
   - Change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`
   - Update ASP.NET Core package versions (Microsoft.AspNetCore.OpenApi, Swashbuckle.AspNetCore)

2. **Application Project** (`ReceiptCollector.Analytics.Application.csproj`):
   - Change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`

3. **Domain Project** (`ReceiptCollector.Analytics.Domain.csproj`):
   - Change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`

4. **Infrastructure Project** (`ReceiptCollector.Analytics.Infrastructure.csproj`):
   - Change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`
   - Update EFCore, Npgsql packages for .NET 10 compatibility

5. **Migrations Project** (`ReceiptCollector.Analytics.Migrations.csproj`):
   - Change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`
   - Update Microsoft.Extensions.Configuration and related packages

### Phase 2: Package Updates
- Update all NuGet package references to versions compatible with .NET 10
- Pay special attention to EFCore packages that may need version updates
- Ensure PostgreSQL and MongoDB driver compatibility

### Phase 3: Configuration Files Review
- Verify appsettings.json files don't contain deprecated settings for .NET 10
- Ensure connection strings remain valid in development environment (appsettings.Development.json)

### Phase 4: Testing Strategy
- Run all existing unit tests to ensure functionality is preserved
- Test integration with MongoDB and PostgreSQL databases
- Validate that API endpoints function correctly with new framework
- Verify migrations still work properly

This migration will provide access to .NET 10's performance improvements, security enhancements, and new features while maintaining the existing architecture and functionality of the Analytics microservice.