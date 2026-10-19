using Microsoft.EntityFrameworkCore;

namespace PADO.TheatreBook.Api;

public sealed class TheatreDb(DbContextOptions<TheatreDb> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Production> Productions => Set<Production>();
    public DbSet<Performance> Performances => Set<Performance>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    // Used by the initial migration snapshot, keeping the model definition in one place.
    public static void ConfigureModel(ModelBuilder m)
    {
        m.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
        });
        m.Entity<Hall>(e =>
        {
            e.ToTable("Halls"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });
        m.Entity<Seat>(e =>
        {
            e.ToTable("Seats"); e.HasKey(x => x.Id);
            e.Property(x => x.RowLabel).HasMaxLength(4).IsRequired();
            e.HasOne(x => x.Hall).WithMany(x => x.Seats).HasForeignKey(x => x.HallId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.HallId, x.RowLabel, x.Number }).IsUnique();
        });
        m.Entity<Production>(e =>
        {
            e.ToTable("Productions"); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        });
        m.Entity<Performance>(e =>
        {
            e.ToTable("Performances"); e.HasKey(x => x.Id);
            e.Property(x => x.PriceUah).HasPrecision(12, 2);
            e.HasOne(x => x.Hall).WithMany().HasForeignKey(x => x.HallId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Production).WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.HallId, x.StartsAtUtc });
        });
        m.Entity<Reservation>(e =>
        {
            e.ToTable("Reservations"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.PriceAtBookingUah).HasPrecision(12, 2);
            e.HasOne(x => x.Performance).WithMany().HasForeignKey(x => x.PerformanceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Seat).WithMany().HasForeignKey(x => x.SeatId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            // The database index is the FINAL arbiter, including across app instances.
            e.HasIndex(x => new { x.PerformanceId, x.SeatId })
                .IsUnique().HasFilter("\"Status\" IN (0, 1)");
            e.HasIndex(x => x.CustomerId);
        });
        m.Entity<Ticket>(e =>
        {
            e.ToTable("Tickets"); e.HasKey(x => x.Id);
            e.HasOne(x => x.Reservation).WithOne()
                .HasForeignKey<Ticket>(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ReservationId).IsUnique();
        });
    }
}
