using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PADO.TheatreBook.Api;
using Xunit;

namespace PADO.TheatreBook.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "integration-admin@theatrebook.test";
    public const string AdminPassword = "Integration-Admin-Password-2026";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
                    ?? throw new InvalidOperationException("TEST_DB_CONNECTION must point to a disposable PostgreSQL test DB"),
                ["Security:JwtKey"] = "TheatreBook-Integration-Test-Key-Never-Use-Outside-Tests-2026",
                ["Admin:Email"] = AdminEmail,
                ["Admin:Password"] = AdminPassword
            }));
    }
}

public sealed class BookingIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory factory;
    public BookingIntegrationTests(ApiFactory factory) => this.factory = factory;

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await ReadAsync(response)).GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<HttpClient> CustomerAsync()
    {
        var email = Guid.NewGuid().ToString("N") + "@theatrebook.test";
        using var anonymous = factory.CreateClient();
        var registered = await anonymous.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Customer-Test-Password-2026" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var body = await ReadAsync(registered);
        Assert.Equal("Customer", body.GetProperty("role").GetString());
        return await LoginAsync(email, "Customer-Test-Password-2026");
    }

    private async Task<(Guid performanceId, Guid seatId, HttpClient admin)> SetupAsync(int seats = 1)
    {
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var hallResponse = await admin.PostAsJsonAsync("/api/halls", new
        {
            name = "Hall-" + Guid.NewGuid().ToString("N"), rows = 1, seatsPerRow = seats
        });
        Assert.Equal(HttpStatusCode.Created, hallResponse.StatusCode);
        var hallId = (await ReadAsync(hallResponse)).GetProperty("id").GetGuid();
        var hall = await admin.GetAsync($"/api/halls/{hallId}");
        var seatId = (await ReadAsync(hall)).GetProperty("seats")[0].GetProperty("id").GetGuid();
        var productionResponse = await admin.PostAsJsonAsync("/api/productions",
            new { title = "Test Drama", description = "For integration tests", durationMinutes = 90 });
        Assert.Equal(HttpStatusCode.Created, productionResponse.StatusCode);
        var productionId = (await ReadAsync(productionResponse)).GetProperty("id").GetGuid();
        var performance = await admin.PostAsJsonAsync("/api/performances", new
        {
            hallId, productionId, startsAt = DateTimeOffset.UtcNow.AddDays(3),
            priceUah = 150m
        });
        Assert.Equal(HttpStatusCode.Created, performance.StatusCode);
        return ((await ReadAsync(performance)).GetProperty("id").GetGuid(), seatId, admin);
    }

    [Fact]
    public async Task Parallel_booking_allows_exactly_one_winner()
    {
        var (performanceId, seatId, admin) = await SetupAsync();
        using (admin)
        using (var customer = await CustomerAsync())
        {
            var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
                customer.PostAsJsonAsync($"/api/performances/{performanceId}/reservations", new { seatId })));
            Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(11, attempts.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        }
    }

    [Fact]
    public async Task Payment_cancellation_and_rebooking_update_reports()
    {
        var (performanceId, seatId, admin) = await SetupAsync();
        using (admin)
        using (var customer = await CustomerAsync())
        {
            var reservation = await customer.PostAsJsonAsync(
                $"/api/performances/{performanceId}/reservations", new { seatId });
            Assert.Equal(HttpStatusCode.Created, reservation.StatusCode);
            var id = (await ReadAsync(reservation)).GetProperty("id").GetGuid();

            var payment = await customer.PostAsync($"/api/reservations/{id}/confirm-demo-payment", null);
            Assert.Equal(HttpStatusCode.OK, payment.StatusCode);
            var ticketId = (await ReadAsync(payment)).GetProperty("ticketId").GetGuid();
            Assert.Equal(HttpStatusCode.Conflict,
                (await customer.PostAsync($"/api/reservations/{id}/confirm-demo-payment", null)).StatusCode);

            var reportBefore = await ReadAsync(await admin.GetAsync($"/api/reports/performances/{performanceId}"));
            Assert.Equal(1, reportBefore.GetProperty("sold").GetInt32());
            Assert.Equal(150m, reportBefore.GetProperty("revenueUah").GetDecimal());

            Assert.Equal(HttpStatusCode.OK,
                (await customer.PostAsync($"/api/reservations/{id}/cancel", null)).StatusCode);
            var ticket = await ReadAsync(await customer.GetAsync($"/api/tickets/{ticketId}"));
            Assert.False(ticket.GetProperty("valid").GetBoolean());

            var replacement = await customer.PostAsJsonAsync(
                $"/api/performances/{performanceId}/reservations", new { seatId });
            Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
            var reportAfter = await ReadAsync(await admin.GetAsync($"/api/reports/performances/{performanceId}"));
            Assert.Equal(0, reportAfter.GetProperty("sold").GetInt32());
            Assert.Equal(1, reportAfter.GetProperty("pending").GetInt32());
            Assert.Equal(0m, reportAfter.GetProperty("revenueUah").GetDecimal());
        }
    }

    [Fact]
    public async Task Overlapping_show_is_rejected_but_another_hall_can_schedule()
    {
        var (performanceId, seatId, admin) = await SetupAsync();
        using (admin)
        {
            var existing = await ReadAsync(await admin.GetAsync($"/api/performances/{performanceId}"));
            var originalHall = existing.GetProperty("hallId").GetGuid();
            var startsAt = existing.GetProperty("startsAtUtc").GetDateTimeOffset();
            var productionId = (await ReadAsync(await admin.GetAsync("/api/productions")))
                .EnumerateArray().Last().GetProperty("id").GetGuid();
            // Select any actual production; the venue is the relevant constraint.
            var overlap = await admin.PostAsJsonAsync("/api/performances",
                new { hallId = originalHall, productionId, startsAt = startsAt.AddMinutes(45), priceUah = 100m });
            Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        }
    }

    [Fact]
    public async Task Customers_cannot_create_halls()
    {
        using var customer = await CustomerAsync();
        var response = await customer.PostAsJsonAsync("/api/halls",
            new { name = "Unauthorized", rows = 1, seatsPerRow = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
