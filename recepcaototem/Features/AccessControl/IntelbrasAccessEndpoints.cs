using System.Security.Cryptography;
using System.Text;
using GestaoPredio.Application.AccessControl;
using GestaoPredio.Domain.AccessControl;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace recepcaototem.Features.AccessControl;

/// <summary>
/// Where an Intelbras Bio-T controller reports to. Proven for the SS 3532 MF on firmware
/// V3.002.00IB000.0.R.20250625: the device is configured with a server IP, port and <em>path</em>, and
/// either posts events ("POST de Eventos") or asks per access ("Modo Online", answered with id/auth/message).
///
/// The exact payload is not documented in anything we have, so this starts in capture mode: it
/// authenticates the device, records the <em>structure</em> of what arrived, and answers <c>auth: false</c>.
/// The door never opens from here and no arrival is confirmed until the normalizer is written against a
/// real captured payload.
/// </summary>
public static class IntelbrasAccessEndpoints
{
    /// <summary>Well under anything a controller would legitimately send, photo included.</summary>
    private const int MaxBodyBytes = 256 * 1024;

    public static IEndpointRouteBuilder MapIntelbrasAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/integrations/intelbras/access/{secret}").AllowAnonymous();
        group.MapPost("", Report);
        // "Modo Online" polls this to decide whether the server is reachable.
        group.MapGet("/keep-alive", KeepAlive);
        return endpoints;
    }

    private static async Task<IResult> KeepAlive(string secret, ApplicationDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var device = await AuthenticateAsync(secret, db, ct);
        if (device is null) return Results.NotFound();
        device.MarkSeen(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { status = "ok" });
    }

    private static async Task<IResult> Report(string secret, HttpContext context, ApplicationDbContext db,
        TimeProvider time, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Intelbras.Access");
        var device = await AuthenticateAsync(secret, db, ct);
        if (device is null)
        {
            // Deliberately indistinguishable from a wrong path: an unauthenticated caller learns nothing.
            logger.LogWarning("Access report rejected: unknown or inactive device. Ip: {Ip}",
                context.Connection.RemoteIpAddress?.ToString());
            return Results.NotFound();
        }

        var body = await ReadBodyAsync(context.Request, ct);
        if (body is null)
        {
            logger.LogWarning("Access report rejected: body over {Max} bytes. DeviceId: {DeviceId}",
                MaxBodyBytes, device.Id);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var now = time.GetUtcNow();
        // Until the manual confirms a unique event id in the payload, identical bytes from the same device
        // are the same event — which is what "transmissão continuada" replays after an outage.
        var key = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{device.Id:N}|{body}"))).ToLowerInvariant();

        device.MarkSeen(now);
        db.AccessEvents.Add(AccessEvent.Capture(device.Id, key, AccessEventDisposition.Captured,
            AccessPayloadShape.Describe(body), now));
        var disposition = AccessEventDisposition.Captured;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            // A replay. The row is already there; record nothing and still answer the device.
            db.ChangeTracker.Clear();
            disposition = AccessEventDisposition.Duplicate;
        }

        logger.LogInformation(
            "Access report captured. DeviceId: {DeviceId}; Serial: {Serial}; Disposition: {Disposition}; IdempotencyKey: {Key}",
            device.Id, device.SerialNumber, disposition, key);

        // Capture mode: never grant. The shape of the reply follows the documented Modo Online contract
        // (id, auth, message) and is harmless to a device that only posts events.
        return Results.Ok(new
        {
            id = ExtractId(body),
            auth = false,
            message = "LUMIS em modo de captura: acesso nao liberado."
        });
    }

    private static async Task<AccessDevice?> AuthenticateAsync(string secret, ApplicationDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length is < 32 or > 200) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return await db.AccessDevices.SingleOrDefaultAsync(x => x.SecretHash == hash && x.IsActive, ct);
    }

    private static async Task<string?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxBodyBytes) return null;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var buffer = new char[MaxBodyBytes + 1];
        var read = await reader.ReadBlockAsync(buffer, ct);
        return read > MaxBodyBytes ? null : new string(buffer, 0, read);
    }

    /// <summary>
    /// Modo Online expects the reply to echo the request's id. The field name is a guess until we see a
    /// real payload, so a miss is null rather than an error.
    /// </summary>
    private static string? ExtractId(string body)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            foreach (var name in new[] { "id", "Id", "ID" })
                if (document.RootElement.TryGetProperty(name, out var value))
                    return value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? value.GetString()
                        : value.GetRawText();
            return null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        string.Equals(postgres.ConstraintName, "UX_AccessEvents_IdempotencyKey", StringComparison.Ordinal);
}
