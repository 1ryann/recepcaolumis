using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Notifications;

/// <summary>Who changed an opt-in: a signed-in user (or none, e.g. a future inbound WhatsApp reply), from where.</summary>
public sealed record WhatsAppOptInActor(string? UserId, string? IpAddress, string CorrelationId);

public sealed record WhatsAppOptInByPhoneResult(int Customers, int Professionals);

/// <summary>
/// The single place that changes WhatsApp opt-ins by phone number and audits each change (docs/operations/whatsapp-consent.md).
/// The reception uses it today. A future inbound "SAIR" reply handler must call <see cref="RevokeByPhoneAsync"/> with
/// the sender's number, a new <see cref="WhatsAppOptInSource"/> for it and an actor without a user, instead of
/// writing its own logic; the send-time gate then stops every later message.
/// </summary>
public sealed class WhatsAppOptInService(ApplicationDbContext db, TimeProvider time)
{
    public const string GrantedAction = "WHATSAPP_OPT_IN_GRANTED";
    public const string RevokedAction = "WHATSAPP_OPT_IN_REVOKED";

    /// <summary>Audit entry of one change: actor, target record, origin IP, correlation. Never the phone number.</summary>
    public static AuditEntry Audit(WhatsAppOptInActor actor, string targetType, Guid targetId, string? targetUserId,
        bool granted, DateTimeOffset occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        Action = granted ? GrantedAction : RevokedAction,
        Result = "SUCCEEDED",
        ActorUserId = actor.UserId,
        TargetUserId = targetUserId,
        TargetEntityType = targetType,
        TargetEntityId = targetId,
        IpAddress = actor.IpAddress,
        OccurredAt = occurredAt,
        CorrelationId = actor.CorrelationId
    };

    /// <summary>Withdraws on every customer and professional record with the number. Always allowed, even without a prior opt-in.</summary>
    public async Task<WhatsAppOptInByPhoneResult> RevokeByPhoneAsync(string e164, WhatsAppOptInSource source,
        WhatsAppOptInActor actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        int customers = 0, professionals = 0;
        foreach (var customer in await db.Customers.Where(x => x.NormalizedPhone == e164).ToListAsync(cancellationToken))
        {
            if (!customer.RevokeWhatsAppOptIn(source, now)) continue;
            customers++;
            db.AuditEntries.Add(Audit(actor, "CUSTOMER", customer.Id, customer.ApplicationUserId, false, now));
        }
        foreach (var professional in await db.Professionals.Where(x => x.WhatsApp == e164).ToListAsync(cancellationToken))
        {
            if (!professional.RevokeWhatsAppOptIn(source, now)) continue;
            professionals++;
            db.AuditEntries.Add(Audit(actor, "PROFESSIONAL", professional.Id, professional.ApplicationUserId, false, now));
        }
        await db.SaveChangesAsync(cancellationToken);
        return new WhatsAppOptInByPhoneResult(customers, professionals);
    }

    /// <summary>
    /// Records an opt-in given in person to staff, for the customer record(s) with the number; the actor is audited as
    /// the person who attested it. Professionals are never opted in by staff: only they can, in their own area.
    /// </summary>
    public async Task<int> GrantCustomersByPhoneAsync(string e164, WhatsAppOptInSource source, WhatsAppOptInActor actor,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var granted = 0;
        foreach (var customer in await db.Customers.Where(x => x.NormalizedPhone == e164 && x.IsActive).ToListAsync(cancellationToken))
        {
            if (!customer.GrantWhatsAppOptIn(source, now)) continue;
            granted++;
            db.AuditEntries.Add(Audit(actor, "CUSTOMER", customer.Id, customer.ApplicationUserId, true, now));
        }
        await db.SaveChangesAsync(cancellationToken);
        return granted;
    }
}
