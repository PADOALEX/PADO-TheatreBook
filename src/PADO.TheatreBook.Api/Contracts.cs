using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PADO.TheatreBook.Api;

public interface IRequest { }

public sealed record RegisterRequest(
    [property: Required, EmailAddress, StringLength(254)] string Email,
    [property: Required, MinLength(10), MaxLength(128)] string Password) : IRequest;
public sealed record LoginRequest(
    [property: Required] string Email, [property: Required] string Password) : IRequest;
public sealed record HallRequest(
    [property: Required, StringLength(120, MinimumLength = 2)] string Name,
    [property: Range(1, 26)] int Rows,
    [property: Range(1, 50)] int SeatsPerRow) : IRequest;
public sealed record ProductionRequest(
    [property: Required, StringLength(200, MinimumLength = 2)] string Title,
    [property: StringLength(2000)] string Description,
    [property: Range(1, 1440)] int DurationMinutes) : IRequest;
public sealed record PerformanceRequest(
    Guid HallId, Guid ProductionId, DateTimeOffset StartsAt,
    [property: Range(typeof(decimal), "0", "1000000000")] decimal PriceUah) : IRequest;
public sealed record ReservationRequest(Guid SeatId) : IRequest;

public sealed class RequestValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var request in context.Arguments.OfType<IRequest>())
        {
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
            {
                var dictionary = errors.SelectMany(e =>
                    (e.MemberNames.Any() ? e.MemberNames : ["request"])
                    .Select(name => (name, message: e.ErrorMessage ?? "Invalid value")))
                    .GroupBy(x => x.name)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.message).ToArray());
                return Results.ValidationProblem(dictionary);
            }
        }
        return await next(context);
    }
}
