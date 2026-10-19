using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PADO.TheatreBook.Api;

public static class Endpoints
{
    public static void MapTheatre(RouteGroupBuilder api)
    {
        api.MapPost("/halls", async (HallRequest input, TheatreDb db, CancellationToken ct) =>
        {
            var hall = new Hall { Name = input.Name.Trim() };
            for (var row = 0; row < input.Rows; row++)
                for (var number = 1; number <= input.SeatsPerRow; number++)
                    hall.Seats.Add(new Seat { HallId = hall.Id, RowLabel = ((char)('A' + row)).ToString(), Number = number });
            db.Halls.Add(hall);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { return Results.Conflict(new { error = "A hall with this name exists" }); }
            return Results.Created($"/api/halls/{hall.Id}",
                new { hall.Id, hall.Name, Capacity = hall.Seats.Count });
        }).RequireAuthorization(Roles.Administrator);

        api.MapGet("/halls", async (TheatreDb db, CancellationToken ct) =>
            Results.Ok(await db.Halls.AsNoTracking()
                .OrderBy(h => h.Name).Select(h => new { h.Id, h.Name, Capacity = h.Seats.Count })
                .ToListAsync(ct)));

        api.MapGet("/halls/{id:guid}", async (Guid id, TheatreDb db, CancellationToken ct) =>
        {
            var hall = await db.Halls.AsNoTracking().Where(h => h.Id == id)
                .Select(h => new { h.Id, h.Name, Seats = h.Seats.OrderBy(s => s.RowLabel)
                    .ThenBy(s => s.Number).Select(s => new { s.Id, s.RowLabel, s.Number }).ToList() })
                .SingleOrDefaultAsync(ct);
            return hall is null ? Results.NotFound() : Results.Ok(hall);
        });

        api.MapPost("/productions", async (ProductionRequest input, TheatreDb db, CancellationToken ct) =>
        {
            var item = new Production
            {
                Title = input.Title.Trim(), Description = input.Description?.Trim() ?? "",
                DurationMinutes = input.DurationMinutes
            };
            db.Productions.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/productions/{item.Id}",
                new { item.Id, item.Title, item.Description, item.DurationMinutes });
        }).RequireAuthorization(Roles.Administrator);

        api.MapGet("/productions", async (TheatreDb db, CancellationToken ct) =>
            Results.Ok(await db.Productions.AsNoTracking().OrderBy(p => p.Title).ToListAsync(ct)));

        api.MapGet("/productions/{id:guid}", async (Guid id, TheatreDb db, CancellationToken ct) =>
        {
            var item = await db.Productions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        api.MapPost("/performances", async (PerformanceRequest input, TheatreDb db, CancellationToken ct) =>
        {
            if (input.HallId == Guid.Empty || input.ProductionId == Guid.Empty)
                return Results.BadRequest(new { error = "Valid hall and production IDs are required" });
            if (input.StartsAt <= DateTimeOffset.UtcNow || input.StartsAt > DateTimeOffset.UtcNow.AddYears(5))
                return Results.BadRequest(new { error = "Start must be in the next five years" });
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var hall = await db.Halls.FindAsync([input.HallId], ct);
            var production = await db.Productions.FindAsync([input.ProductionId], ct);
            if (hall is null || production is null) return Results.BadRequest(new { error = "Hall or production not found" });
            var start = input.StartsAt.UtcDateTime;
            var end = start.AddMinutes(production.DurationMinutes);
            // Friendly check; PostgreSQL exclusion constraint catches ALL races.
            if (await db.Performances.AnyAsync(p => p.HallId == hall.Id && !p.IsCancelled &&
                p.StartsAtUtc < end && start < p.EndsAtUtc, ct))
                return Results.Conflict(new { error = "This hall has an overlapping performance" });
            var performance = new Performance
            {
                HallId = hall.Id, ProductionId = production.Id, StartsAtUtc = start,
                EndsAtUtc = end, PriceUah = input.PriceUah
            };
            db.Performances.Add(performance);
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23P01" })
            { return Results.Conflict(new { error = "This hall has an overlapping performance" }); }
            return Results.Created($"/api/performances/{performance.Id}",
                new { performance.Id, performance.HallId, performance.ProductionId,
                    performance.StartsAtUtc, performance.EndsAtUtc, performance.PriceUah });
        }).RequireAuthorization(Roles.Administrator);

        api.MapGet("/performances", async (TheatreDb db, CancellationToken ct) =>
            Results.Ok(await db.Performances.AsNoTracking()
                .OrderBy(p => p.StartsAtUtc)
                .Select(p => new { p.Id, p.ProductionId, Title = p.Production.Title,
                    p.HallId, Hall = p.Hall.Name, p.StartsAtUtc, p.EndsAtUtc, p.PriceUah, p.IsCancelled })
                .ToListAsync(ct)));

        api.MapGet("/performances/{id:guid}", async (Guid id, TheatreDb db, CancellationToken ct) =>
        {
            var p = await db.Performances.AsNoTracking().Where(p => p.Id == id)
                .Select(p => new { p.Id, Title = p.Production.Title, p.HallId,
                    Hall = p.Hall.Name, p.StartsAtUtc, p.EndsAtUtc, p.PriceUah, p.IsCancelled })
                .SingleOrDefaultAsync(ct);
            return p is null ? Results.NotFound() : Results.Ok(p);
        });

        api.MapGet("/performances/{id:guid}/seats", async (Guid id, TheatreDb db, CancellationToken ct) =>
        {
            var p = await db.Performances.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            if (p is null) return Results.NotFound();
            var busy = db.Reservations.Where(r => r.PerformanceId == id &&
                r.Status != ReservationStatus.Cancelled).Select(r => r.SeatId);
            var seats = await db.Seats.AsNoTracking().Where(s => s.HallId == p.HallId)
                .OrderBy(s => s.RowLabel).ThenBy(s => s.Number)
                .Select(s => new { s.Id, s.RowLabel, s.Number, Available = !busy.Contains(s.Id) })
                .ToListAsync(ct);
            return Results.Ok(new { p.Id, p.IsCancelled, Seats = seats });
        });

        api.MapPost("/performances/{id:guid}/cancel", async (Guid id, TheatreDb db, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // FOR UPDATE serializes with booking's FOR SHARE on the performance.
            var rows = await db.Performances.FromSqlInterpolated(
                $"SELECT * FROM \"Performances\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
            var p = rows.SingleOrDefault();
            if (p is null) return Results.NotFound();
            if (p.IsCancelled) return Results.Conflict(new { error = "Already cancelled" });
            if (await db.Reservations.AnyAsync(r => r.PerformanceId == id &&
                r.Status != ReservationStatus.Cancelled, ct))
                return Results.Conflict(new { error = "Cancel active reservations before cancelling the performance" });
            p.IsCancelled = true;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { p.Id, p.IsCancelled });
        }).RequireAuthorization(Roles.Administrator);
    }

    public static void MapReservations(RouteGroupBuilder api)
    {
        api.MapPost("/performances/{id:guid}/reservations", async (Guid id, ReservationRequest input,
            ClaimsPrincipal principal, BookingService bookings, CancellationToken ct) =>
        {
            var user = AuthService.UserId(principal);
            return user is null ? Results.Unauthorized()
                : await bookings.ReserveAsync(id, input.SeatId, user.Value, ct);
        }).RequireAuthorization();

        api.MapGet("/reservations", async (ClaimsPrincipal principal, TheatreDb db, CancellationToken ct) =>
        {
            var uid = AuthService.UserId(principal);
            if (uid is null) return Results.Unauthorized();
            var isAdmin = principal.IsInRole(Roles.Administrator);
            var items = await db.Reservations.AsNoTracking()
                .Where(r => isAdmin || r.CustomerId == uid.Value)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => new { r.Id, r.PerformanceId, r.SeatId,
                    Status = r.Status.ToString(), r.PriceAtBookingUah, r.CreatedAtUtc,
                    TicketId = db.Tickets.Where(t => t.ReservationId == r.Id)
                        .Select(t => (Guid?)t.Id).FirstOrDefault() })
                .ToListAsync(ct);
            return Results.Ok(items);
        }).RequireAuthorization();

        api.MapPost("/reservations/{id:guid}/confirm-demo-payment",
            async (Guid id, ClaimsPrincipal principal, BookingService bookings, CancellationToken ct) =>
            {
                var user = AuthService.UserId(principal);
                return user is null ? Results.Unauthorized()
                    : await bookings.ConfirmAsync(id, user.Value, principal.IsInRole(Roles.Administrator), ct);
            }).RequireAuthorization();

        api.MapPost("/reservations/{id:guid}/cancel",
            async (Guid id, ClaimsPrincipal principal, BookingService bookings, CancellationToken ct) =>
            {
                var user = AuthService.UserId(principal);
                return user is null ? Results.Unauthorized()
                    : await bookings.CancelAsync(id, user.Value, principal.IsInRole(Roles.Administrator), ct);
            }).RequireAuthorization();

        api.MapGet("/tickets/{id:guid}", async (Guid id, ClaimsPrincipal principal,
            TheatreDb db, CancellationToken ct) =>
        {
            var uid = AuthService.UserId(principal);
            if (uid is null) return Results.Unauthorized();
            var ticket = await db.Tickets.AsNoTracking()
                .Where(t => t.Id == id && (principal.IsInRole(Roles.Administrator) ||
                    t.Reservation.CustomerId == uid.Value))
                .Select(t => new { t.Id, t.ReservationId, t.IssuedAtUtc,
                    t.Reservation.PerformanceId, t.Reservation.SeatId,
                    Valid = t.Reservation.Status == ReservationStatus.Confirmed })
                .SingleOrDefaultAsync(ct);
            return ticket is null ? Results.NotFound() : Results.Ok(ticket);
        }).RequireAuthorization();
    }

    public static void MapReports(RouteGroupBuilder api)
    {
        api.MapGet("/reports/performances/{id:guid}", async (Guid id, TheatreDb db, CancellationToken ct) =>
        {
            var p = await db.Performances.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new { p.Id, p.HallId, p.PriceUah, p.StartsAtUtc, p.IsCancelled })
                .SingleOrDefaultAsync(ct);
            if (p is null) return Results.NotFound();
            var capacity = await db.Seats.CountAsync(s => s.HallId == p.HallId, ct);
            var held = await db.Reservations.CountAsync(r => r.PerformanceId == id &&
                r.Status == ReservationStatus.Pending, ct);
            var sold = await db.Reservations.CountAsync(r => r.PerformanceId == id &&
                r.Status == ReservationStatus.Confirmed, ct);
            var revenue = await db.Reservations.Where(r => r.PerformanceId == id &&
                r.Status == ReservationStatus.Confirmed)
                .SumAsync(r => (decimal?)r.PriceAtBookingUah, ct) ?? 0;
            return Results.Ok(new { p.Id, Capacity = capacity, Pending = held, Sold = sold,
                Available = capacity - held - sold, OccupancyPercent = capacity == 0 ? 0 :
                    Math.Round(100m * (held + sold) / capacity, 2),
                SalesOccupancyPercent = capacity == 0 ? 0 : Math.Round(100m * sold / capacity, 2),
                RevenueUah = revenue, Currency = "UAH" });
        }).RequireAuthorization(Roles.Administrator);
    }
}
