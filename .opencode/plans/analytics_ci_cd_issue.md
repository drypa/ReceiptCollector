# Задача: Исправить проблему с CI/CD Pipeline

- **Приоритет**: HIGH
- **Цель**: Создать и настроить CI/CD pipeline для проекта Analytics, чтобы обеспечить автоматизацию сборки, тестирования и развертывания.

## Описание проблемы

### Локация
- Проект: `Analytics/`
- Отсутствуют файлы конфигурации CI/CD в `.github/workflows/`

### Текущий код
- Нет автоматизированных процессов сборки и тестирования
- Развертывание происходит вручную
- Нет интеграции тестов в pipeline

### Проблема
Отсутствие CI/CD pipeline приводит к:
1. Ручной сборке и развертыванию, что увеличивает время до выхода в продакшн
2. Отсутствию автоматизированного тестирования, что повышает риск ошибок
3. Нет контроля качества кода (тестовое покрытие, стиль кода)
4. Нет механизма отката в случае сбоя развертывания

## План решения

### Шаг 1: Создать CI workflow
Создать GitHub Actions workflow для автоматизации сборки и тестирования:
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

### Шаг 2: Добавить quality gates
Добавить проверки качества кода в pipeline:
- Минимальное покрытие тестами (например, 80%)
- Проверка стиля кода
- Сканирование на уязвимости зависимостей

### Шаг 3: Создать CD workflow
Создать workflow для автоматизации развертывания:
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

### Шаг 4: Добавить мониторинг развертывания
Добавить проверки после развертывания:
- Проверка работоспособности сервиса
- Проверка подключения к базе данных
- Мониторинг доступности API эндпоинтов

## Тестирование

### Команды
```bash
git checkout -b feature/analytics-ci-cd
git add .github/workflows/
git commit -m "Add CI/CD pipeline for Analytics"
git push origin feature/analytics-ci-cd
```

### Ожидаемые результаты
1. CI workflow запускается при создании pull request и проверяет:
   - Успешная сборка проекта
   - Прохождение всех тестов
   - Покрытие кода не ниже 80%
2. CD workflow развертывает приложение на dev сервере после успешного прохождения CI
3. Мониторинг подтверждает работоспособность сервиса после развертывания

## Критерии успеха
- [ ] Создан и работает CI workflow в `.github/workflows/dotnet-ci.yml`
- [ ] Все тесты проходят автоматически при push в main
- [ ] Покрытие кода не ниже 80%
- [ ] CD workflow развертывает приложение на dev сервере
- [ ] Мониторинг подтверждает работоспособность сервиса после развертывания
- [ ] Есть механизм отката в случае сбоя
