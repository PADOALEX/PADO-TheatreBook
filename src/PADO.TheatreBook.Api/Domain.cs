namespace PADO.TheatreBook.Api;

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Customer = "Customer";
}

public enum ReservationStatus { Pending = 0, Confirmed = 1, Cancelled = 2 }

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Customer;
}

public sealed class Production
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int DurationMinutes { get; set; }
}

public sealed class Hall
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<Seat> Seats { get; set; } = [];
}

public sealed class Seat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HallId { get; set; }
    public Hall Hall { get; set; } = null!;
    public string RowLabel { get; set; } = "";
    public int Number { get; set; }
}

public sealed class Performance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HallId { get; set; }
    public Hall Hall { get; set; } = null!;
    public Guid ProductionId { get; set; }
    public Production Production { get; set; } = null!;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public decimal PriceUah { get; set; }
    public bool IsCancelled { get; set; }
}

public sealed class Reservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PerformanceId { get; set; }
    public Performance Performance { get; set; } = null!;
    public Guid SeatId { get; set; }
    public Seat Seat { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public AppUser Customer { get; set; } = null!;
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal PriceAtBookingUah { get; set; }
}

public sealed class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReservationId { get; set; }
    public Reservation Reservation { get; set; } = null!;
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class TheatreRules
{
    // [start, end): adjacent performances are allowed.
    public static bool Overlap(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
        => aStart < bEnd && bStart < aEnd;

    public static bool CanConfirm(ReservationStatus status) => status == ReservationStatus.Pending;
    public static bool CanCancel(ReservationStatus status) => status != ReservationStatus.Cancelled;
    public static bool OccupiesSeat(ReservationStatus status) => status != ReservationStatus.Cancelled;
}
