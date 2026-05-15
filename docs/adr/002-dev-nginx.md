# ADR 002: Развертывание Nginx прокси для аналитических сервисов в разработке

## Статус
Принято

## Контекст
В среде разработки нам нужно обеспечить единую точку доступа к обоим аналитическим API и фронтенд-сервисам, которые теперь будут запускаться локально в IDE. В настоящее время разработчикам необходимо управлять отдельными портами (8085 для API, 3000 для фронтенда), что усложняет локальную отладку.

## Решение
Мы добавим сервис nginx в `docker-compose.develop.yml`, который будет выступать в роли обратного прокси для обоих компонентов аналитики:
- Проксирование запросов `/api/` к аналитическому API, работающему на localhost:8085 
- Проксирование всех остальных запросов к фронтенду аналитики, работающему на localhost:3000

## Детали реализации
### 1. Конфигурация Nginx (`nginx.conf`)
```nginx
events {
    worker_connections 1024;
}

http {
    upstream analytics_api {
        server host.docker.internal:8085;
    }

    upstream analytics_frontend {
        server host.docker.internal:3000;
    }

    server {
        listen 80;
        server_name localhost;

        location /api/ {
            proxy_pass http://analytics_api/;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }

        location / {
            proxy_pass http://analytics_frontend/;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }
    }
}
```

### 2. Обновления Docker Compose
Добавьте следующее в `docker-compose.develop.yml`:
```yaml
nginx:
  image: nginx:alpine
  container_name: receipt-nginx
  ports:
    - "8080:80"
  volumes:
    - ./nginx.conf:/etc/nginx/nginx.conf
  networks:
    - collector-net
```

### 3. Требования к настройке среды
1. Убедитесь, что `host.docker.internal` правильно настроен в среде разработки
2. Подтвердите, что аналитическое API запускается на порту 8085 локально (dotnet run)
3. Подтвердите, что фронтенд аналитики запускается на порту 3000 локально (npm start или аналогичный способ)

## Преимущества
- Единственная точка доступа к сервисам аналитики во время локальной отладки
- Чистые URL-адреса без явных портов в среде разработки
- Соответствие существующим паттернам Docker Compose
- Возможность унифицированной рабочей среды для разработки

## Риски и меры по их устранению
- Конфликты портов: Использование порта 8080 для избежания конфликтов с существующими сервисами
- Связь по сети хоста: Убедитесь, что `host.docker.internal` корректно разрешается на платформе