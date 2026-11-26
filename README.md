# Googols

**Learn Smarter. Grow Further. Achieve Anything.**
AI-powered learning platform for Classes 1–12 (Kerala State, CBSE, ICSE) and entrance exams, in Malayalam and English.

| Layer | Technology |
|---|---|
| Frontend | React 19 + TypeScript (Vite), React Router, TanStack Query, SignalR client |
| Backend | ASP.NET Core (.NET 10), minimal-API REST + OpenAPI, modular monolith with Clean Architecture |
| Data | SQL Server, EF Core 10, ASP.NET Core Identity |
| AI | Azure OpenAI (behind `IAiModelProvider`), Azure AI Search for RAG (behind `ICurriculumRetriever`) |
| Platform | Hangfire (jobs), Redis (cache), Azure Blob Storage, SignalR, OpenTelemetry → Azure Monitor |

## Repository layout

```
Googols/
├── Googols.slnx                      # open in Visual Studio 2026 / Rider / `dotnet` CLI
├── src/
│   ├── Googols.Domain/               # entities, rules; no dependencies
│   ├── Googols.Application/          # use-case logic, DTOs, validators, ports (interfaces)
│   ├── Googols.Infrastructure/       # EF Core, Identity, Azure OpenAI/Search/Blob, Redis, Hangfire
│   ├── Googols.WebApi/               # HTTP endpoints, auth policies, SignalR hub, OpenTelemetry
│   └── Googols.Worker/               # Hangfire server for background jobs
├── tests/
│   ├── Googols.UnitTests/
│   └── Googols.ArchitectureTests/    # enforces the layer dependency rule
├── frontend/googols-web/             # React app
└── docs/                             # blueprint, architecture, DB, API, AI, security, roadmap, status
```

## First run (Windows)

Prerequisites: .NET 10 SDK, Node 22+, SQL Server LocalDB (comes with Visual Studio), optionally Azurite and Redis.

```powershell
# 1. Restore and build
dotnet restore Googols.slnx
dotnet build Googols.slnx
dotnet test Googols.slnx

# 2. Create the database (first migration)
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialFoundation -p src/Googols.Infrastructure -s src/Googols.WebApi -o Persistence/Migrations
dotnet ef database update -p src/Googols.Infrastructure -s src/Googols.WebApi

# 3. Secrets (never in appsettings.json)
cd src/Googols.WebApi
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:Deployment" "<deployment-name>"
dotnet user-secrets set "AzureOpenAI:ApiKey" "<key>"
dotnet user-secrets set "AzureSearch:Endpoint" "https://<search>.search.windows.net"
dotnet user-secrets set "AzureSearch:ApiKey" "<key>"

# 4. Run API (https://localhost:7180) and React app (http://localhost:5173)
dotnet run --project src/Googols.WebApi --launch-profile https
cd frontend/googols-web; npm install; npm run dev
```

Without Azure keys the app still runs: the AI teacher returns *"not available right now"* (HTTP 503) instead of failing.
Without Redis, an in-memory cache is used. Blob storage defaults to Azurite (`UseDevelopmentStorage=true`).

Start with [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md) to see what's built and verified.
