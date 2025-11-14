# ReceiptCollector Project Overview

## Project Description
ReceiptCollector is a multi-service application that collects receipt information from the Russian Tax Service's API ("Проверка чека"). The project is designed to collect purchase data using the nalog.ru API and consists of several services including backend collectors, a Telegram bot, and an Analytics service for spending management.

## Architecture
The project follows a microservice architecture with the following main components:

1. **Backend Service**: Written in Go, handles receipt collection from the nalog.ru API
2. **Telegram Bot**: Written in Go, provides a Telegram interface to the application
3. **Analytics Service**: A .NET 8 application for spending analysis and receipt management
4. **MongoDB**: Used for storing raw receipt data
5. **PostgreSQL**: Used for the Analytics service's normalized data
6. **Nginx**: Web server component

## Technology Stack
- **Backend**: Go (1.20+)
- **Analytics Service**: .NET 8, C#, ASP.NET Core
- **Frontend**: React (for Analytics UI)
- **Databases**: MongoDB, PostgreSQL
- **Containerization**: Docker, Docker Compose
- **Messaging**: gRPC for communication between services

## Project Structure
```
ReceiptCollector/
├── Analytics/          # .NET 8 Analytics service
├── backend/            # Go backend service
├── bot/                # Go Telegram bot
├── docker/             # Docker configurations
├── postgres/           # PostgreSQL configurations
├── api/                # API definitions
├── ...
```

## Building and Running

### Prerequisites
- Docker and Docker Compose
- Environment variables set in a `.env` file

### Build the Project
```bash
chmod +x ./build.sh
./build.sh
```
This command builds all services defined in the docker-compose.yml file.

### Run the Project
```bash
chmod +x ./up.sh
./up.sh
```
This command pulls the latest images and starts all services in detached mode.

### Stop the Project
```bash
chmod +x ./down.sh
./down.sh
```

### Development Mode
For development, you can run the Angular app separately:
```bash
cd ./webapp
npm run start
```

And build/run third-party components:
```bash
cd ./docker/nginx
./build.sh
cd ../..
./up.dev.sh
```

## Services

### Backend (Go)
- Located in `backend/` directory
- Collects receipt data from nalog.ru API
- Uses MongoDB for storage
- Runs receipt processing workers
- Exposes gRPC endpoints for the Telegram bot

### Telegram Bot (Go)
- Located in `bot/` directory
- Provides Telegram interface to the system
- Communicates with the backend via gRPC

### Analytics (.NET 8)
- Located in `Analytics/` directory
- Purpose: Transform and normalize purchase receipts from external MongoDB to relational PostgreSQL database
- Provides user analytics via web interface
- Features automatic and manual category assignment for receipt items
- Implements Telegram authentication
- Built with ASP.NET Core, React frontend
- Domain-driven design approach

## Docker Compose Configuration
The project uses docker-compose.yml to orchestrate multiple services:
- MongoDB for receipt storage
- PostgreSQL for analytics
- Backend collector service
- Telegram bot service
- Analytics service

## Key Scripts
- `build.sh`: Builds all Docker images
- `up.sh`: Starts all services
- `down.sh`: Stops all services
- `up.dev.sh`: Starts services in development mode
- `backup.sh` / `restore.sh`: Database backup and restore utilities

## Development Conventions
- Multi-service architecture with clear separation of concerns
- Container-first development approach
- Environment variable-based configuration
- gRPC for inter-service communication
- The Analytics service follows domain-driven design principles

## Analytics Service Specifics
The Analytics service (in the Analytics directory) is a comprehensive system for spending management with features including:
- Reading receipts from MongoDB and migrating to PostgreSQL
- Automatic and manual categorization of receipt items
- REST/HTTP API for frontends
- Admin UI for manual category adjustment
- User web UI (React) with analytics and query builder
- Telegram authentication
- Modern .NET 8 stack with EF Core

## Configuration
The system relies on environment variables defined in a `.env` file (referenced in docker-compose.yml) for configuration of database credentials, API secrets, and other settings.