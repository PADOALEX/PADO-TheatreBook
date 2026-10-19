# 🎭 PADO TheatreBook

**Theatre Booking & Ticket Management API** — a .NET 10 / PostgreSQL backend rebuilt from two educational C++ theatre systems, with database-enforced protection against concurrent double booking.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4) ![PostgreSQL 17](https://img.shields.io/badge/PostgreSQL-17-4169E1) ![EF Core](https://img.shields.io/badge/EF%20Core-10-512BD4) ![API](https://img.shields.io/badge/API-REST-blue)

## What it does

- Theatre productions, halls, numbered seats and scheduled performances
- Enforces non-overlapping performances **per hall**, including concurrent admin requests
- Reserves a **specific seat for a specific performance**; prevents duplicate active reservations via PostgreSQL unique partial index
- Reservation status: Pending → Confirmed (demo payment only), or Pending/Confirmed → Cancelled
- Issues ticket on demo confirmation; cancellation invalidates it and frees the seat
- Customer registration/login and JWT roles (Administrator / Customer), ownership checks
- Seat availability, occupancy/sales reports and historical price-at-booking snapshot in UAH
- Unit tests plus real-PostgreSQL integration tests, including 12 parallel booking attempts

**Payment is simulated. No card collection, payment gateway, real money movement or refunds.**

## Quick start (Docker Desktop + Docker Compose)

1. Copy `.env.example` to `.env`. Replace **all** sample values with unique local secrets (JWT key 32+ bytes).
2. Run `docker compose up --build -d`.
3. Open `http://localhost:8080/openapi/v1.json` (OpenAPI JSON in Development); `http://localhost:8080/health` is the liveness endpoint.
4. Register a customer at `POST /api/auth/register`, sign in at `POST /api/auth/login`. The configured admin account is seeded on first startup; sign in with the `ADMIN_EMAIL` and `ADMIN_PASSWORD` values from your local `.env`.
5. Use bearer JWT `Authorization: Bearer <token>` for authenticated operations.

Demo startup runs database migrations automatically after PostgreSQL becomes healthy. The first build may take longer due to SDK restore. No secrets are tracked in Git.

## API

| Method | Endpoint | Access |
|---|---|---|
| POST | `/api/auth/register`, `/api/auth/login` | Public |
| GET | `/api/halls`, `/api/halls/{id}`, `/api/productions` | Public |
| POST | `/api/halls` (rows × seats), `/api/productions` | Administrator |
| GET | `/api/performances`, `/api/performances/{id}`, `/api/performances/{id}/seats` | Public |
| POST | `/api/performances`, `/api/performances/{id}/cancel` | Administrator |
| POST | `/api/performances/{id}/reservations` | Customer or Administrator |
| GET | `/api/reservations`, `/api/users/me` | Authenticated (own data; admin can list all) |
| POST | `/api/reservations/{id}/confirm-demo-payment`, `/api/reservations/{id}/cancel` | Owner or Administrator |
| GET | `/api/tickets/{id}` | Owner or Administrator |
| GET | `/api/reports/performances/{id}` | Administrator |

### Typical request flow

1. Admin: create a hall, e.g. `{"name":"Main Hall","rows":5,"seatsPerRow":12}`.
2. Admin: create a production, e.g. `{"title":"Hamlet","description":"Tragedy","durationMinutes":120}`.
3. Admin: schedule it: `{"hallId":"<uuid>","productionId":"<uuid>","startsAt":"2027-01-15T19:00:00Z","priceUah":250}` (choose a future UTC timestamp).
4. Customer: GET performance seats; POST reservation `{"seatId":"<seat-uuid>"}`.
5. Customer: POST `/api/reservations/{id}/confirm-demo-payment` or `/cancel`.
6. Admin: GET sales and occupancy report.

## Critical technical feature: no double booking

The database has a **partial unique index** on `(PerformanceId, SeatId)` limited to Pending and Confirmed states. Two simultaneous requests cannot both commit the same seat. The conflict becomes HTTP **409** (SQLSTATE 23505). Show overlaps are separately blocked by a GiST exclusion constraint (SQLSTATE 23P01). Reservation confirmation/cancellation takes row-level locks, and show cancellation is serialized with seat booking. See [architecture and transaction design](docs/ARCHITECTURE.md).

## Run tests

Requires .NET 10 SDK and a **disposable PostgreSQL 17 test database** with permission to create the `btree_gist` extension.

```bash
dotnet restore PADO.TheatreBook.slnx
dotnet build PADO.TheatreBook.slnx -c Release
# Point ONLY to a disposable DB, never a real customer database:
export TEST_DB_CONNECTION='Host=localhost;Port=5432;Database=theatrebook_tests;Username=theatre;Password=your-test-secret'
dotnet test PADO.TheatreBook.slnx -c Release --no-build
```

PowerShell: `$env:TEST_DB_CONNECTION = 'Host=localhost;Port=5432;Database=theatrebook_tests;Username=theatre;Password=your-test-secret'` before `dotnet test`. GitHub Actions runs the same tests with a PostgreSQL service. Tests are **not** backed by EF InMemory.

## Project layout

- `src/PADO.TheatreBook.Api` — HTTP endpoints, authentication, rules, EF model, migrations, booking transactions
- `tests/PADO.TheatreBook.Tests` — domain tests and API/PostgreSQL integration/concurrency tests
- `docs/SOURCE_AUDIT.md` — comparison of both C++ archives and decisions
- `docs/ARCHITECTURE.md` — schema, concurrency strategy, guarantees and limitations
- `Dockerfile`, `compose.yaml`, `.github/workflows/ci.yml` — local run and CI

## Portfolio notes

This is an educational API and a deliberately scoped MVP — not a commercial payment processor. It demonstrates C#/.NET, REST, EF Core, PostgreSQL constraints and concurrency-aware transactional design. See the [source audit](docs/SOURCE_AUDIT.md) for the original C++ code's defects and the migration rationale.

**Author:** [PADOALEX](https://github.com/PADOALEX) · **License:** MIT
