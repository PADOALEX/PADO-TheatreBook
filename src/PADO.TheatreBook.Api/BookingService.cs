using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PADO.TheatreBook.Api;

public sealed class BookingService(TheatreDb db)
{
    // PostgreSQL partial unique index wins even if separate servers race.
    public async Task<IResult> ReserveAsync(Guid performanceId, Guid seatId, Guid customerId, CancellationToken ct)
    {
        if (seatId == Guid.Empty) return Results.BadRequest(new { error = "SeatId is required" });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Performances.FromSqlInterpolated(
            $"SELECT * FROM \"Performances\" WHERE \"Id\" = {performanceId} FOR SHARE").ToListAsync(ct);
        var performance = rows.SingleOrDefault();
        if (performance is null) return Results.NotFound();
        if (performance.IsCancelled || performance.StartsAtUtc <= DateTime.UtcNow)
            return Results.Conflict(new { error = "Performance is cancelled or has started" });
        if (!await db.Seats.AnyAsync(x => x.Id == seatId && x.HallId == performance.HallId, ct))
            return Results.BadRequest(new { error = "Seat does not belong to this performance's hall" });

        var reservation = new Reservation
        {
            PerformanceId = performanceId, SeatId = seatId,
            CustomerId = customerId, PriceAtBookingUah = performance.PriceUah
        };
        db.Reservations.Add(reservation);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Conflict(new { error = "Seat is already reserved for this performance" });
        }
        return Results.Created($"/api/reservations/{reservation.Id}",
            new { reservation.Id, reservation.PerformanceId, reservation.SeatId,
                Status = reservation.Status.ToString(), reservation.PriceAtBookingUah });
    }

    public async Task<IResult> ConfirmAsync(Guid id, Guid currentUser, bool admin, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Reservations.FromSqlInterpolated(
            $"SELECT * FROM \"Reservations\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
        var reservation = rows.SingleOrDefault();
        if (reservation is null) return Results.NotFound();
        if (reservation.CustomerId != currentUser && !admin) return Results.Forbid();
        if (!TheatreRules.CanConfirm(reservation.Status))
            return Results.Conflict(new { error = "Only pending reservations can be confirmed" });
        // No external money movement: transition plus ticket creation is one atomic transaction.
        reservation.Status = ReservationStatus.Confirmed;
        var ticket = new Ticket { ReservationId = id };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { reservation.Id, Status = reservation.Status.ToString(),
            TicketId = ticket.Id, ticket.IssuedAtUtc, Payment = "DEMO_ONLY" });
    }

    public async Task<IResult> CancelAsync(Guid id, Guid currentUser, bool admin, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Reservations.FromSqlInterpolated(
            $"SELECT * FROM \"Reservations\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
        var reservation = rows.SingleOrDefault();
        if (reservation is null) return Results.NotFound();
        if (reservation.CustomerId != currentUser && !admin) return Results.Forbid();
        if (!TheatreRules.CanCancel(reservation.Status))
            return Results.Conflict(new { error = "Reservation already cancelled" });
        reservation.Status = ReservationStatus.Cancelled; // Index releases seat on COMMIT.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { reservation.Id, Status = reservation.Status.ToString(),
            Refund = "NOT_IMPLEMENTED" });
    }
}
