using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PADO.TheatreBook.Api;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
builder.Services.AddDbContext<TheatreDb>(o => o.UseNpgsql(connectionString));
builder.Services.AddScoped<BookingService>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
AuthService.RegisterAuth(builder.Services, builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
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
