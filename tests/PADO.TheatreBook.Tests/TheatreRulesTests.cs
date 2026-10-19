using PADO.TheatreBook.Api;
using Xunit;

namespace PADO.TheatreBook.Tests;

public sealed class TheatreRulesTests
{
    private static readonly DateTime T = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Overlap_detects_intersection()
    {
        Assert.True(TheatreRules.Overlap(T, T.AddHours(2), T.AddHours(1), T.AddHours(3)));
    }

    [Fact]
    public void Adjacent_shows_do_not_overlap()
    {
        Assert.False(TheatreRules.Overlap(T, T.AddHours(2), T.AddHours(2), T.AddHours(4)));
    }

    [Theory]
    [InlineData(ReservationStatus.Pending, true)]
    [InlineData(ReservationStatus.Confirmed, true)]
    [InlineData(ReservationStatus.Cancelled, false)]
    public void Seat_occupancy_follows_status(ReservationStatus status, bool expected)
    {
        Assert.Equal(expected, TheatreRules.OccupiesSeat(status));
    }

    [Fact]
    public void Only_pending_reservations_can_be_paid()
    {
        Assert.True(TheatreRules.CanConfirm(ReservationStatus.Pending));
        Assert.False(TheatreRules.CanConfirm(ReservationStatus.Confirmed));
        Assert.False(TheatreRules.CanConfirm(ReservationStatus.Cancelled));
    }
}
