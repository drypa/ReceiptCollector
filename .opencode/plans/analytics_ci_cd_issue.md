# Task: Fix CI/CD Pipeline Issue

## Problem Description
The Analytics project lacks a proper CI/CD pipeline, making it difficult to ensure code quality and automate deployments.

## Current Issues

### 1. Missing CI Configuration
- No GitHub Actions workflows
- No Azure Pipelines configuration
- No build automation

### 2. Incomplete Test Automation
- Tests not integrated into pipeline
- No test coverage reporting
- No quality gates

### 3. Missing Deployment Strategy
- No deployment automation
- No environment promotion strategy
- No rollback mechanism

## Solution Steps

### Step 1: Create CI Workflow
Create GitHub Actions workflow for CI:
```yaml
name: .NET Build and Test

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
    
    - name: Restore dependencies
      run: dotnet restore Analytics/src/ReceiptCollector.Analytics.sln
    
    - name: Build
      run: dotnet build Analytics/src/ReceiptCollector.Analytics.sln --no-restore --configuration Release
    
    - name: Test
      run: dotnet test Analytics/src/ReceiptCollector.Analytics.sln --no-build --configuration Release --collect:"XPlat Code Coverage"
    
    - name: Upload coverage reports to Codecov
      uses: codecov/codecov-action@v3
```

### Step 2: Add Quality Gates
Add quality checks to pipeline:
- Test coverage minimum (e.g., 80%)
- Code style enforcement
- Security scanning
- Dependency vulnerability checking

### Step 3: Create CD Workflow
Create deployment workflow:
```yaml
name: Deploy Analytics Service

on:
  workflow_run:
    workflows: [".NET Build and Test"]
    branches: [main]
    types:
      - completed

jobs:
  deploy-dev:
    if: ${{ github.event.workflow_run.conclusion == 'success' }}
    runs-on: ubuntu-latest
    environment: development
    
    steps:
    - uses: actions/checkout@v4
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
    
    - name: Build Docker image
      run: docker build -t receiptcollector-analytics-dev -f Analytics/Dockerfile .
    
    - name: Log in to Docker registry
      uses: docker/login-action@v2
      with:
        username: ${{ secrets.DOCKER_USERNAME }}
        password: ${{ secrets.DOCKER_PASSWORD }}
    
    - name: Push image
      run: |
        docker tag receiptcollector-analytics-dev ${{ secrets.DOCKER_USERNAME }}/receiptcollector-analytics:dev-${{ github.sha }}
        docker push ${{ secrets.DOCKER_USERNAME }}/receiptcollector-analytics:dev-${{ github.sha }}
    
    - name: Deploy to dev
      uses: appleboy/ssh-action@master
      with:
        host: ${{ secrets.DEV_SERVER_HOST }}
        username: ${{ secrets.DEV_SERVER_USER }}
        key: ${{ secrets.DEV_SERVER_SSH_KEY }}
        script: |
          docker pull ${{ secrets.DOCKER_USERNAME }}/receiptcollector-analytics:dev-${{ github.sha }}
          docker stop analytics-dev || true
          docker rm analytics-dev || true
          docker run -d --name analytics-dev \
            -p 8080:80 \
            -p 443:443 \
            --env-file .env.dev \
            ${{ secrets.DOCKER_USERNAME }}/receiptcollector-analytics:dev-${{ github.sha }}
```

### Step 4: Add Monitoring
Add health check monitoring:
- Verify service is running after deployment
- Check database connectivity
- Monitor API endpoints

## Files to Create
- `.github/workflows/dotnet-ci.yml` - CI workflow
- `.github/workflows/dotnet-cd-dev.yml` - Dev deployment workflow  
- `.github/workflows/dotnet-cd-prod.yml` - Prod deployment workflow
- `docker-compose.dev.yml` - Development Docker Compose
- `docker-compose.prod.yml` - Production Docker Compose

## Testing Strategy
1. Test CI workflow with pull requests
2. Verify CD workflow deploys to dev environment
3. Test rollback procedure
4. Monitor deployment success/failure rates
