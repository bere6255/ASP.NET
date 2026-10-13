# Payments API

An ASP.NET Core 8 MVC-style Web API sample for account transfers, JWT authentication, SQL Server persistence, Redis-backed idempotency, and RabbitMQ events.

## Run locally without Docker (testing mode)

This mode keeps SQL Server real but replaces Redis idempotency caching/locking with in-memory implementations and disables RabbitMQ publishing/consuming. It is intended for one local API process only; it does not test distributed locking or message delivery. SQL Server is still required for authentication, balances, and transactions.

1. Install the .NET 8 SDK and SQL Server Express LocalDB (or another local SQL Server instance).
2. From the `PaymentsApi` directory, configure user-secrets for local testing:

   ```powershell
   dotnet user-secrets set "LocalTesting:UseInMemoryInfrastructure" "true"
   dotnet user-secrets set "ConnectionStrings:Default" "Server=(localdb)\MSSQLLocalDB;Database=PaymentsDb;Trusted_Connection=True;TrustServerCertificate=True"
   dotnet user-secrets set "Jwt:Key" "replace-with-a-random-secret-at-least-32-bytes-long"
   dotnet user-secrets set "Jwt:Issuer" "PaymentsApi"
   dotnet user-secrets set "Jwt:Audience" "PaymentsApi.Client"
   dotnet user-secrets set "Cors:AllowedOrigins:0" "https://localhost:5173"
   ```

   For a different SQL Server instance, replace the connection string with that instance's connection details. User-secrets are local to this project and should be used instead of committing local credentials.

3. Restore packages and create/apply the initial schema. This repository does not currently include an EF migration, so create one once:

   ```powershell
   dotnet tool install --global dotnet-ef --version 8.0.11
   dotnet restore
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

4. Start the API:

   ```powershell
   dotnet run
   ```

   Open `/swagger` on the HTTPS URL printed by `dotnet run`. Register two users and use `GET /api/accounts/me` to obtain account IDs. The registration endpoint creates one zero-balance account; fund local test accounts directly in SQL Server before testing a transfer.

With `LocalTesting:UseInMemoryInfrastructure` enabled, `/health` reports SQL Server and the in-memory cache; it does not probe RabbitMQ. Transfers still write to SQL Server, while event publication is logged and skipped.

## Run with Docker

1. Install the .NET 8 SDK and Docker Desktop.
2. Copy `.env.example` to `.env` and replace all sample credentials. `.env` is ignored by Git.
3. Start SQL Server, Redis, and RabbitMQ:

   ```powershell
   docker compose up -d
   ```

4. Configure `ConnectionStrings__Default`, `Redis__ConnectionString`, the `RabbitMq__*` settings, and `Jwt__Key` in `.env` or user-secrets. Then run `dotnet restore`, `dotnet ef database update`, and `dotnet run`.

Environment variables use double underscores for nested configuration keys, for example `Jwt__Key` and `ConnectionStrings__Default`.

## API notes

- Every response is `{ "status": true|false, "data": {}, "message": "" }`; HTTP status codes remain meaningful.
- `POST /api/auth/register`, `POST /api/auth/login`, and `POST /api/auth/refresh` provide authentication.
- `POST /api/transfers` requires a bearer token and an `Idempotency-Key` header of 8–128 characters.
- The source account must belong to the authenticated `sub`; a transfer debits that account and credits the destination account.
- `GET /health` checks SQL Server, Redis, and RabbitMQ reachability in the normal configuration. Local testing mode reports SQL Server and its in-memory cache instead.

## Why we did it this way

- **Idempotency:** a request hash distinguishes a legitimate retry from reusing a key for different transfer details. Redis avoids repeat database reads in the common case, while SQL uniqueness is authoritative under races.
- **ACID:** both account balance updates, transaction records, and the idempotency response are persisted in one SQL transaction.
- **Concurrency:** SQL Server `rowversion` detects stale balance writes and turns them into a retryable conflict instead of silently overwriting money.
- **At-least-once delivery:** publisher confirms and persistent messages improve broker durability, while consumers acknowledge only after recording successful processing.
- **Dead-letter queues:** bounded retries prevent permanently failing messages from blocking the main queue indefinitely.
- **Atomic updates:** lockout counters use `ExecuteUpdateAsync`, and refresh-token consumption is an atomic conditional update rather than read-modify-write.
- **Refresh-token reuse detection:** tokens are stored as hashes, rotated once, and replay revokes the complete token family.
- **Centralized errors:** exception handling, model validation, authentication, rate limits, and status-code pages all produce the same response envelope without leaking implementation details.
- **Next reliability improvement:** a transactional outbox should persist publish intent alongside the transfer, so a process crash after database commit cannot lose the corresponding RabbitMQ event.

## Tests

Run the test suite with:

```powershell
dotnet test .\Tests\PaymentsApi.Tests.csproj
```

Integration scenarios that require SQL Server, Redis, and RabbitMQ should run against disposable test instances; do not point tests at production resources.




cd C:\Users\USER\Documents\GitHub\ASP.NET\PaymentsApi

dotnet user-secrets set "LocalTesting:UseInMemoryInfrastructure" "true"
dotnet user-secrets set "ConnectionStrings:Default" "Server=(localdb)\MSSQLLocalDB;Database=paymentsdb;Trusted_Connection=True;TrustServerCertificate=True"
dotnet user-secrets set "Jwt:Key" "replace-with-a-random-secret-at-least-32-bytes-long"
dotnet user-secrets set "Jwt:Issuer" "PaymentsApi"
dotnet user-secrets set "Jwt:Audience" "PaymentsApi.Client"

dotnet tool install --global dotnet-ef --version 8.0.11
dotnet restore
dotnet ef migrations add InitialCreate
dotnet ef database update
dotnet run