using System.Security.Claims;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Notifications;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Api.Configuration;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Whatsapp;

public sealed record WhatsappOptInRequest(bool OptIn) : IStrictModuleRequest;
public sealed record WhatsappPhoneRequest(string Phone) : IStrictModuleRequest;
public sealed record WhatsappOptInResponse(string Status, DateTimeOffset? ChangedAt, string? Source, string? TextVersion);
public sealed record WhatsappOptOutResponse(int Customers, int Professionals);
public sealed record WhatsappOptInGrantResponse(int Customers);
/// <summary>One record found by number, as the reception sees it: masked name only, never the full name or number.</summary>
public sealed record WhatsappOptInRecordResponse(Guid Id, string Kind, string MaskedName, bool HasAccount, bool IsActive,
    WhatsappOptInResponse OptIn);
public sealed record WhatsappOptInLookupResponse(IReadOnlyList<WhatsappOptInRecordResponse> Records);

/// <summary>
/// Operational WhatsApp opt-in (docs/operations/whatsapp-consent.md).
/// <list type="bullet">
/// <item>A customer or professional grants and withdraws for themselves, signed in, in their own area.</item>
/// <item>The reception, with the person present, can look a number up, record a customer's opt-in (the attendant is
/// audited as the attester) or record a withdrawal for anyone with the number — the only path for customers without
/// an account. Staff never opt a professional in.</item>
/// </list>
/// Phone numbers travel only in request bodies (never in URLs) and are never written to the audit log.
/// </summary>
public static class WhatsappOptInEndpoints
{
    public static IEndpointRouteBuilder MapWhatsappOptInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/customer/me/whatsapp-opt-in", CustomerGet).RequireAuthorization(IdentityConfiguration.CustomerPolicy);
        endpoints.MapPut("/api/customer/me/whatsapp-opt-in", CustomerPut).RequireAuthorization(IdentityConfiguration.CustomerPolicy)
            .AddEndpointFilter<AntiforgeryFilter>();
        endpoints.MapGet("/api/professional/me/whatsapp-opt-in", ProfessionalGet).RequireAuthorization("Professional");
        endpoints.MapPut("/api/professional/me/whatsapp-opt-in", ProfessionalPut).RequireAuthorization("Professional")
            .AddEndpointFilter<AntiforgeryFilter>();
        var reception = endpoints.MapGroup("/api/reception").RequireAuthorization("Operations");
        reception.MapPost("/whatsapp-opt-in/lookup", ReceptionLookup).AddEndpointFilter<AntiforgeryFilter>();
        reception.MapPost("/whatsapp-opt-in", ReceptionGrant).AddEndpointFilter<AntiforgeryFilter>();
        reception.MapPost("/whatsapp-opt-out", ReceptionOptOut).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    internal static WhatsAppOptInActor Actor(HttpContext context) => new(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier), context.Connection.RemoteIpAddress?.ToString(),
        context.TraceIdentifier);

    internal static AuditEntry Audit(HttpContext context, string targetType, Guid targetId, string? targetUserId, bool granted,
        DateTimeOffset occurredAt) =>
        WhatsAppOptInService.Audit(Actor(context), targetType, targetId, targetUserId, granted, occurredAt);

    internal static WhatsappOptInResponse Response(WhatsAppOptInState optIn) => new(
        WhatsAppOptInStorage.Status(optIn.Status),
        optIn.ChangedAt,
        optIn.Source is { } source ? WhatsAppOptInStorage.Source(source) : null,
        optIn.TextVersion);

    private static async Task<IResult> CustomerGet(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationUserId == userId, cancellationToken);
        return customer is null ? Results.NotFound() : Results.Ok(Response(customer.WhatsAppOptIn));
    }

    private static async Task<IResult> CustomerPut(WhatsappOptInRequest request, HttpContext context, ApplicationDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.ApplicationUserId == userId, cancellationToken);
        if (customer is null) return Results.NotFound();
        var now = time.GetUtcNow();
        var changed = request.OptIn
            ? customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerPortal, now)
            : customer.RevokeWhatsAppOptIn(WhatsAppOptInSource.CustomerPortal, now);
        if (changed) db.AuditEntries.Add(Audit(context, "CUSTOMER", customer.Id, userId, request.OptIn, now));
        return await SaveAsync(() => db.SaveChangesAsync(cancellationToken), () => Response(customer.WhatsAppOptIn));
    }

    private static async Task<IResult> ProfessionalGet(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var professional = await db.Professionals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ApplicationUserId == userId && x.IsActive, cancellationToken);
        return professional is null ? Results.NotFound() : Results.Ok(Response(professional.WhatsAppOptIn));
    }

    private static async Task<IResult> ProfessionalPut(WhatsappOptInRequest request, HttpContext context, ApplicationDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.ApplicationUserId == userId && x.IsActive, cancellationToken);
        if (professional is null) return Results.NotFound();
        var now = time.GetUtcNow();
        var changed = request.OptIn
            ? professional.GrantWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, now)
            : professional.RevokeWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, now);
        if (changed) db.AuditEntries.Add(Audit(context, "PROFESSIONAL", professional.Id, userId, request.OptIn, now));
        return await SaveAsync(() => db.SaveChangesAsync(cancellationToken), () => Response(professional.WhatsAppOptIn));
    }

    private static async Task<IResult> ReceptionLookup(WhatsappPhoneRequest request, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!WhatsAppNormalizer.TryNormalize(request.Phone ?? "", out var phone)) return InvalidPhone();
        var customers = await db.Customers.AsNoTracking().Where(x => x.NormalizedPhone == phone).ToListAsync(cancellationToken);
        var professionals = await db.Professionals.AsNoTracking().Where(x => x.WhatsApp == phone).ToListAsync(cancellationToken);
        return Results.Ok(new WhatsappOptInLookupResponse([
            .. customers.Select(x => new WhatsappOptInRecordResponse(x.Id, "CUSTOMER", MaskName(x.Name), x.ApplicationUserId is not null,
                x.IsActive, Response(x.WhatsAppOptIn))),
            .. professionals.Select(x => new WhatsappOptInRecordResponse(x.Id, "PROFESSIONAL", MaskName(x.Name), x.ApplicationUserId is not null,
                x.IsActive, Response(x.WhatsAppOptIn)))
        ]));
    }

    /// <summary>The person is at the desk and says yes: the attendant is audited as the one who recorded it.</summary>
    private static async Task<IResult> ReceptionGrant(WhatsappPhoneRequest request, HttpContext context, WhatsAppOptInService optIns,
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!WhatsAppNormalizer.TryNormalize(request.Phone ?? "", out var phone)) return InvalidPhone();
        if (!await db.Customers.AnyAsync(x => x.NormalizedPhone == phone && x.IsActive, cancellationToken))
            return Results.NotFound(new ApiError("CUSTOMER_NOT_FOUND", "Nenhum cliente ativo com este número."));
        return await SaveAsync(
            async () => new WhatsappOptInGrantResponse(await optIns.GrantCustomersByPhoneAsync(phone, WhatsAppOptInSource.Reception,
                Actor(context), cancellationToken)),
            result => result);
    }

    /// <summary>Withdrawal requested in person or by phone: every customer and professional record with that number.</summary>
    private static async Task<IResult> ReceptionOptOut(WhatsappPhoneRequest request, HttpContext context, WhatsAppOptInService optIns,
        CancellationToken cancellationToken)
    {
        if (!WhatsAppNormalizer.TryNormalize(request.Phone ?? "", out var phone)) return InvalidPhone();
        return await SaveAsync(
            () => optIns.RevokeByPhoneAsync(phone, WhatsAppOptInSource.Reception, Actor(context), cancellationToken),
            result => new WhatsappOptOutResponse(result.Customers, result.Professionals));
    }

    /// <summary>"Maria Clara Souza" → "Maria S.": enough for the attendant to confirm with the person, nothing more.</summary>
    internal static string MaskName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch { 0 => "", 1 => parts[0], _ => $"{parts[0]} {parts[^1][0]}." };
    }

    private static IResult InvalidPhone() =>
        Results.BadRequest(new ApiError("WHATSAPP_RECIPIENT_INVALID", "Informe um número de WhatsApp válido."));

    private static Task<IResult> SaveAsync<TResponse>(Func<Task> save, Func<TResponse> response) =>
        SaveAsync(async () => { await save(); return true; }, _ => response());

    private static async Task<IResult> SaveAsync<TResult, TResponse>(Func<Task<TResult>> save, Func<TResult, TResponse> response)
    {
        try
        {
            return Results.Ok(response(await save()));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Json(new ApiError("CONCURRENCY_CONFLICT", "O registro foi alterado. Tente novamente."), statusCode: 409);
        }
    }
}
