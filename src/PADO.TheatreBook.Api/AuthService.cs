using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Microsoft.AspNetCore.RateLimiting;

namespace PADO.TheatreBook.Api;

public static class AuthService
{
    public static void RegisterAuth(IServiceCollection services, IConfiguration config)
    {
        var key = config["Security:JwtKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Security:JwtKey must be set to a random secret of at least 32 UTF-8 bytes.");
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = Issuer(config),
                    ValidateAudience = true, ValidAudience = Audience(config),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.Email, RoleClaimType = ClaimTypes.Role
                };
            });
        services.AddAuthorizationBuilder().AddPolicy(Roles.Administrator,
            p => p.RequireRole(Roles.Administrator));
    }

    private static string Issuer(IConfiguration cfg) => cfg["Security:Issuer"] ?? "PADO.TheatreBook";
    private static string Audience(IConfiguration cfg) => cfg["Security:Audience"] ?? "PADO.TheatreBook.Client";

    public static string Issue(AppUser user, IConfiguration config)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Security:JwtKey"]!));
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var token = new JwtSecurityToken(Issuer(config), Audience(config), claims,
            expires: DateTime.UtcNow.AddHours(2), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static async Task SeedAdminAsync(TheatreDb db, IPasswordHasher<AppUser> hasher,
        IConfiguration cfg, CancellationToken ct = default)
    {
        var email = cfg["Admin:Email"]?.Trim().ToLowerInvariant();
        var password = cfg["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password)) return;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || password.Length < 10)
            throw new InvalidOperationException("Set both Admin:Email and Admin:Password (10+ chars), or neither.");
        if (await db.Users.AnyAsync(x => x.Email == email, ct))
            return; // Existing users are NEVER promoted by environment config.
        var user = new AppUser { Email = email, Role = Roles.Administrator };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    public static void MapAuth(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").RequireRateLimiting("auth");
        group.MapPost("/register", async (RegisterRequest input, TheatreDb db,
            IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            var email = input.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(x => x.Email == email, ct))
                return Results.Conflict(new { error = "Email already registered" });
            var user = new AppUser { Email = email, Role = Roles.Customer };
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            db.Users.Add(user);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { return Results.Conflict(new { error = "Email already registered" }); }
            return Results.Created("/api/users/me", new { user.Id, user.Email, user.Role });
        });

        group.MapPost("/login", async (LoginRequest input, TheatreDb db,
            IPasswordHasher<AppUser> hasher, IConfiguration cfg, CancellationToken ct) =>
        {
            var email = input.Email.Trim().ToLowerInvariant();
            var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password)
                == PasswordVerificationResult.Failed)
                return Results.Unauthorized();
            return Results.Ok(new { AccessToken = Issue(user, cfg), TokenType = "Bearer", ExpiresInSeconds = 7200 });
        });

        api.MapGet("/users/me", async (ClaimsPrincipal principal, TheatreDb db, CancellationToken ct) =>
        {
            var id = UserId(principal);
            var user = await db.Users.Where(u => u.Id == id)
                .Select(u => new { u.Id, u.Email, u.Role }).SingleOrDefaultAsync(ct);
            return user is null ? Results.NotFound() : Results.Ok(user);
        }).RequireAuthorization();
    }
}
