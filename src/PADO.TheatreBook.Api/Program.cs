using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PADO.TheatreBook.Api;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
// Initial migration is hand-authored SQL; schema is validated by PostgreSQL integration tests.
// EF's snapshot-diff heuristic cannot compare this hand-authored baseline reliably.
builder.Services.AddDbContext<TheatreDb>(o => o.UseNpgsql(connectionString)
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddScoped<BookingService>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0, AutoReplenishment = true
        }));
});
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
AuthService.RegisterAuth(builder.Services, builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Startup migration is intended for one-instance demo deployments. See docs/ARCHITECTURE.md.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TheatreDb>();
    await db.Database.MigrateAsync();
    var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<AppUser>>();
    await AuthService.SeedAdminAsync(db, hasher, app.Configuration);
}

var api = app.MapGroup("/api").AddEndpointFilter<RequestValidationFilter>();
AuthService.MapAuth(api);
Endpoints.MapTheatre(api);
Endpoints.MapReservations(api);
Endpoints.MapReports(api);
app.Run();

public partial class Program { }
