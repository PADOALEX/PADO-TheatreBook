# Legacy C++ audit (19 October 2026)

The old repositories are *domain references*, not production libraries. Binary artifacts, Visual Studio caches and local databases are intentionally excluded.

## `test + theatre manag(1).zip`

Sources examined: `THEATRE_Test/main.cpp`, `Performance.{h,cpp}`, `Ticket.{h,cpp}`, `Utils.{h,cpp}`, sample TXT files.

**Usable domain ideas:** performance title/date/duration/actors/description; future-date validation; overlapping-show prevention; browsing upcoming productions, search by title/date/actor; ticket sale, ticket ID, buyer and availability; file persistence; optional feedback ratings.

**Known defects:** `Ticket::generateTicket()` decrements seats; `sellTicket()` decrements again. Seats are assigned from remaining capacity, not selected or uniquely persisted. `Utils.cpp` defines `clearPerformanceHistory` twice; a conflicting rule on matching actor initials is not a sound identity check; venue identity is absent from overlap checks. Dates use inconsistent comments and UI formats; data are stored in plain files without concurrency control. Ownership of linked-list pointers is manual; reloading and clearing may invalidate ticket-to-performance pointers. These defects are not carried forward.

## `theatre-management-system(2).zip`

Sources examined: models `Customer`, `Venue`, `Performance`, `Ticket`; services `SchedulingService`, `TicketService`, `ReportService`; `Repository`, `FileManager`, `main.cpp`, CMake configuration, tests.

**Usable domain ideas:** explicit venue, performance, customer and ticket entities; separation of scheduling, sales, reporting and persistence concerns; unit-test intent.

**Known defects:** headers/implementations disagree on constructors, fields and function names (e.g., `Performance`, `Ticket`, `Venue`). `Performance::checkOverlap()` always returns `false`. Placeholder calls from `main.cpp` do not match service signatures. Services redefine classes in implementation files, reference missing methods or headers, and the provided tests refer to nonexistent signatures. No transactional guarantees, persistent seat identity or real protection against duplicate seat purchases. README assertions are broader than the actual implementation.

## Decisions

| Migrated concept | TheatreBook interpretation |
| --- | --- |
| Venue/capacity | `Hall` with immutable prebuilt `Seat` rows |
| Performance | A `Production` scheduled as a `Performance` in one hall at a time |
| Overlap | Real interval intersection with PostgreSQL exclusion constraint |
| Ticket sale | Customer-owned reservation for an explicit seat and performance |
| Availability | Derived from active reservations; never a mutable counter |
| Sale history | Database reservations, demo-paid tickets and reporting |
| Reports | Confirmed ticket count and historical price snapshot |
| Actor conflict and ratings | Deferred: not needed for validated booking MVP |

No C++ library is required to build or run the new system.
