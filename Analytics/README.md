# ReceiptCollector.Analytics

This project consists of a backend API and a frontend application for receipt analytics.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (v18 or higher)
- [PostgreSQL](https://www.postgresql.org/)
- [MongoDB](https://www.mongodb.com/)

## Backend Setup

### Configuration

1. Navigate to the backend directory:
   ```bash
   cd src/ReceiptCollector.Analytics.Api
   ```

2. Configure the database connection strings in `appsettings.json` or `appsettings.Development.json`:
   ```json
   {
     "ConnectionStrings": {
       "Postgres": "Host=localhost;Database=receipt_analytics;Username=postgres;Password=yourpassword",
       "Mongo": "mongodb://localhost:27017/receipt_data"
     }
   }
   ```

### Running the Backend

1. Install dependencies:
   ```bash
   dotnet restore
   ```

2. Run database migrations:
   ```bash
   cd ../ReceiptCollector.Analytics.Migrations
   dotnet run
   ```

3. Return to the API directory and run the application:
   ```bash
   cd ../ReceiptCollector.Analytics.Api
   dotnet run
   ```

The backend will start on `http://localhost:5000` (or as configured in `launchSettings.json`).

## Frontend Setup

### Configuration

1. Navigate to the frontend directory:
   ```bash
   cd frontend
   ```

2. Install dependencies:
   ```bash
   npm install
   ```

### Running the Frontend

1. Start the development server:
   ```bash
   npm run dev
   ```

The frontend will start on `http://localhost:5173` (or as configured in `vite.config.ts`).

## Development Workflow

For development, you'll typically want to run both the backend and frontend simultaneously:

1. Start the backend (from `src/ReceiptCollector.Analytics.Api`):
   ```bash
   dotnet watch run
   ```

2. In a separate terminal, start the frontend (from `frontend`):
   ```bash
   npm run dev
   ```

## Project Structure

- `src/`: Main source code
  - `ReceiptCollector.Analytics.Api`: Backend API
  - `ReceiptCollector.Analytics.Application`: Application layer
  - `ReceiptCollector.Analytics.Domain`: Domain models
  - `ReceiptCollector.Analytics.Infrastructure`: Infrastructure services
  - `ReceiptCollector.Analytics.Migrations`: Database migration scripts
- `frontend/`: React frontend application
- `tests/`: Unit and integration tests