FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY *.sln .
COPY src/ReceiptCollector.Analytics.Api/ReceiptCollector.Analytics.Api.csproj ./src/ReceiptCollector.Analytics.Api/ReceiptCollector.Analytics.Api.csproj
COPY src/ReceiptCollector.Analytics.Application/ReceiptCollector.Analytics.Application.csproj ./src/ReceiptCollector.Analytics.Application/ReceiptCollector.Analytics.Application.csproj
COPY src/ReceiptCollector.Analytics.Domain/ReceiptCollector.Analytics.Domain.csproj ./src/ReceiptCollector.Analytics.Domain/ReceiptCollector.Analytics.Domain.csproj
COPY src/ReceiptCollector.Analytics.Infrastructure/ReceiptCollector.Analytics.Infrastructure.csproj ./src/ReceiptCollector.Analytics.Infrastructure/ReceiptCollector.Analytics.Infrastructure.csproj
COPY src/ReceiptCollector.Analytics.Migrations/ReceiptCollector.Analytics.Migrations.csproj ./src/ReceiptCollector.Analytics.Migrations/ReceiptCollector.Analytics.Migrations.csproj
COPY tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj ./tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj
RUN dotnet restore

COPY . .

WORKDIR src/ReceiptCollector.Analytics.Api/
RUN ls -alqh
RUN dotnet publish -c Release -o /app/publish --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReceiptCollector.Analytics.Api.dll"]

