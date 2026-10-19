# Architecture and concurrency contract

## Scope

.NET 10 minimal API, EF Core 10, Npgsql, PostgreSQL 17, JWT bearer tokens, ASP.NET PasswordHasher. Authentication roles: anonymous read-only catalogue, Customer booking and own ticket lifecycle, Administrator catalogue creation, performance cancellation and reports. Admin is seeded only from explicitly configured environment credentials. Self-registration *always* gets Customer. There is no actual payment processor, refund or email integration.

## Data model

`Production` defines a show and its duration. `Hall` owns a fixed matrix of seats. `Performance` links a production and hall to a UTC half-open timeslot and a price in UAH. `Reservation` links a user, seat and performance, preserving price at booking. `Ticket` is created only by a successful demo confirmation and is valid only while its reservation is Confirmed.

No independent availability counter is stored. Seats are occupied by Pending and Confirmed reservations and become available only when a cancellation transaction commits. Pending reservations do not expire automatically in the MVP. This is intentional; no phantom reservation-expiry worker is claimed.

## Non-negotiable PostgreSQL guarantees

1. **One active booking per seat per performance**: unique partial index `("PerformanceId","SeatId") WHERE "Status" IN (0,1)`. Concurrent INSERTs against the same key cannot both commit. Loser gets SQLSTATE `23505`, returned as HTTP 409. A check-then-insert in C# *alone* is insufficient.
2. **No overlapping performances in one hall**: GiST exclusion constraint using `btree_gist` and `tstzrange("StartsAtUtc","EndsAtUtc",'[)')`, filtered to non-cancelled performances. Concurrent conflicting INSERTs cannot both commit. SQLSTATE `23P01` maps to HTTP 409.
3. **Booking and performance cancellation**: booking transaction takes a PostgreSQL `FOR SHARE` lock on the performance row; admin cancellation takes `FOR UPDATE` and refuses cancellation while an active reservation exists. This prevents booking a cancelled performance through this API under concurrent requests.
4. **Payment/cancellation races**: each transition locks its reservation with `FOR UPDATE`. Only Pending can be demo-confirmed; only non-Cancelled can be cancelled. Confirmation creates the ticket and changes the status in one transaction.
5. **Persistence constraints**: foreign keys for users, halls, seats, productions and reservations; unique hall names and seat positions; DB checks for valid status, positive duration, positive seat numbers and valid intervals.

PostgreSQL's default READ COMMITTED isolation plus row locks, unique index and exclusion constraint is deliberate. There is no distributed mutex, in-memory seat map or unsafe attempt to make SERIALIZABLE isolation alone prevent duplicates.

## Consistency trade-offs / limitations

- All booking/scheduling writes use the database; listing availability can become stale between GET and POST. The POST is authoritative.
- A cancelled confirmed reservation invalidates its ticket. The demo reports *net* confirmed sales, not recognized revenue or a historical accounting ledger; no monetary refund happens.
- Only creation and cancellation are included for shows; destructive modification of halls or existing seats is omitted intentionally to preserve reservation referential integrity.
- Roles are embedded in two-hour JWTs. Changing a role would require token expiry; there is no role-edit API.
- No external payment callbacks/webhooks, real financial settlement or expiring holds.
- The initial PostgreSQL migration is hand-authored SQL. EF Core's pending-model-changes heuristic is suppressed for this baseline; functional constraints are verified against PostgreSQL in CI. All future model changes require deliberate SQL migrations and matching snapshot updates. Do not rely on the suppressed heuristic to detect schema drift.
- The startup migrator assumes one API instance in a demo. In a scaled deployment, run EF migrations in a controlled one-time deployment step, and disable startup migration by code change before multi-replica deployment.
- No request idempotency token yet: a network retry for a *different* seat creates another reservation. Duplicate booking of the *same* seat remains safe.
- Concurrency tests should run on real PostgreSQL (never substitute EF Core InMemory/SQLite).
- Test DB in CI is disposable. Running integration tests against a shared live DB is not supported.

## Hardening before real-world use

Add structured request tracing, per-user auth throttling, operational migration orchestration, booking hold expiry, refresh/revocation tokens, stable payment event IDs and webhook signatures, proper refunds, auditing, and business-specific seating/accessibility policies. No security audit or production-grade payment certification is claimed.
