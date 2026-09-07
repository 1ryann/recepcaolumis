using System.Security.Claims;
using GestaoPredio.Application.Finance;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Finance;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Finance;

public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/finance").RequireAuthorization("Operations");
        admin.MapGet("/charges", ListAdmin);
        admin.MapGet("/charges/{id:guid}", DetailAdmin);
        admin.MapPost("/materialize", Materialize).AddEndpointFilter<AntiforgeryFilter>();
        admin.MapPost("/charges/{id:guid}/pay", Pay).AddEndpointFilter<AntiforgeryFilter>();
        admin.MapPost("/charges/{id:guid}/adjust", Adjust).AddEndpointFilter<AntiforgeryFilter>();
        admin.MapPost("/charges/{id:guid}/cancel", Cancel).AddEndpointFilter<AntiforgeryFilter>();
        admin.MapGet("/summary", Summary);

        var professional = endpoints.MapGroup("/api/professional/finance").RequireAuthorization("Professional");
        professional.MapGet("/charges", ListProfessional);
        professional.MapGet("/charges/{id:guid}", DetailProfessional);
        return endpoints;
    }

    private static Task<IResult> ListAdmin(int? page, int? pageSize, string? status, Guid? tenantId,
        Guid? professionalId, Guid? leaseId, DateOnly? referenceFrom, DateOnly? referenceTo,
        DateOnly? dueFrom, DateOnly? dueTo, ApplicationDbContext db, TimeProvider clock,
        TimeZoneInfo zone, CancellationToken ct) => List(page, pageSize, status, tenantId, professionalId,
        leaseId, referenceFrom, referenceTo, dueFrom, dueTo, null, db, clock, zone, ct);

    private static async Task<IResult> ListProfessional(int? page, int? pageSize, string? status,
        DateOnly? referenceFrom, DateOnly? referenceTo, HttpContext context, ApplicationDbContext db,
        TimeProvider clock, TimeZoneInfo zone, CancellationToken ct)
    {
        var id = await ResolveProfessional(db, context, ct);
        return id is null ? Results.NotFound() : await List(page, pageSize, status, null, id,
            null, referenceFrom, referenceTo, null, null, id, db, clock, zone, ct);
    }

    private static async Task<IResult> List(int? page, int? pageSize, string? status, Guid? tenantId,
        Guid? professionalId, Guid? leaseId, DateOnly? referenceFrom, DateOnly? referenceTo,
        DateOnly? dueFrom, DateOnly? dueTo, Guid? ownerProfessionalId, ApplicationDbContext db,
        TimeProvider clock, TimeZoneInfo zone, CancellationToken ct)
    {
        var actualPage = page ?? 1; var actualSize = pageSize ?? 20;
        if (actualPage < 1) return Bad("INVALID_PAGE", "A página deve ser maior ou igual a 1.");
        if (actualSize is < 1 or > 100) return Bad("INVALID_PAGE_SIZE", "O tamanho da página deve estar entre 1 e 100.");
        if (tenantId == Guid.Empty || professionalId == Guid.Empty || leaseId == Guid.Empty) return Bad("INVALID_FILTER", "O filtro informado é inválido.");
        if (referenceFrom > referenceTo || dueFrom > dueTo) return Bad("INVALID_PERIOD", "O período informado é inválido.");
        if (!TryStatus(status, out var parsed)) return Bad("INVALID_STATUS", "O status informado é inválido.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var query = from charge in db.FinancialCharges.AsNoTracking()
                    join professional in db.Professionals.AsNoTracking() on charge.ProfessionalId equals professional.Id
                    join tenant in db.Tenants.AsNoTracking() on charge.TenantId equals tenant.Id
                    select new { Charge = charge, ProfessionalName = professional.Name, TenantName = tenant.Name };
        if (ownerProfessionalId is not null) query = query.Where(x => x.Charge.ProfessionalId == ownerProfessionalId);
        if (tenantId is not null) query = query.Where(x => x.Charge.TenantId == tenantId);
        if (professionalId is not null) query = query.Where(x => x.Charge.ProfessionalId == professionalId);
        if (leaseId is not null) query = query.Where(x => x.Charge.LeaseId == leaseId);
        if (referenceFrom is not null) query = query.Where(x => x.Charge.ReferencePeriodStart >= referenceFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (referenceTo is not null) query = query.Where(x => x.Charge.ReferencePeriodStart < referenceTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (dueFrom is not null) query = query.Where(x => x.Charge.DueDate >= dueFrom);
        if (dueTo is not null) query = query.Where(x => x.Charge.DueDate <= dueTo);
        if (parsed == "PENDING") query = query.Where(x => x.Charge.Status == FinancialChargeStatus.Pending && x.Charge.DueDate >= today);
        else if (parsed == "OVERDUE") query = query.Where(x => x.Charge.Status == FinancialChargeStatus.Pending && x.Charge.DueDate < today);
        else if (parsed == "PAID") query = query.Where(x => x.Charge.Status == FinancialChargeStatus.Paid);
        else if (parsed == "CANCELLED") query = query.Where(x => x.Charge.Status == FinancialChargeStatus.Cancelled);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Charge.DueDate).ThenBy(x => x.Charge.ReferencePeriodStart).ThenBy(x => x.Charge.Id)
            .Skip((actualPage - 1) * actualSize).Take(actualSize).ToListAsync(ct);
        return Results.Ok(new PagedResponse<FinancialChargeResponse>(rows.Select(x => Map(x.Charge, x.ProfessionalName, x.TenantName, today)).ToArray(), actualPage, actualSize, total));
    }

    private static async Task<IResult> DetailAdmin(Guid id, ApplicationDbContext db, TimeProvider clock, TimeZoneInfo zone, CancellationToken ct)
    {
        var row = await (from charge in db.FinancialCharges.AsNoTracking().Where(x => x.Id == id)
                         join professional in db.Professionals.AsNoTracking() on charge.ProfessionalId equals professional.Id
                         join tenant in db.Tenants.AsNoTracking() on charge.TenantId equals tenant.Id
                         select new { Charge = charge, ProfessionalName = professional.Name, TenantName = tenant.Name }).SingleOrDefaultAsync(ct);
        if (row is null) return Results.NotFound();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        return Results.Ok(Map(row.Charge, row.ProfessionalName, row.TenantName, today));
    }

    private static async Task<IResult> DetailProfessional(Guid id, HttpContext context, ApplicationDbContext db,
        TimeProvider clock, TimeZoneInfo zone, CancellationToken ct)
    {
        var professionalId = await ResolveProfessional(db, context, ct);
        if (professionalId is null) return Results.NotFound();
        var row = await (from charge in db.FinancialCharges.AsNoTracking().Where(x => x.Id == id && x.ProfessionalId == professionalId)
                         join professional in db.Professionals.AsNoTracking() on charge.ProfessionalId equals professional.Id
                         join tenant in db.Tenants.AsNoTracking() on charge.TenantId equals tenant.Id
                         select new { Charge = charge, ProfessionalName = professional.Name, TenantName = tenant.Name }).SingleOrDefaultAsync(ct);
        if (row is null) return Results.NotFound();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        return Results.Ok(Map(row.Charge, row.ProfessionalName, row.TenantName, today));
    }

    private static async Task<IResult> Materialize(MaterializeFinanceRequest request, HttpContext context,
        IFinancialChargeMaterializer materializer, TimeProvider clock, TimeZoneInfo zone, CancellationToken ct)
    {
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var throughDate = request.ThroughDate ?? localToday;
        var throughAt = GestaoPredio.Application.Scheduling.OperationalTimeZone.GetCivilDayInterval(throughDate, zone).EndAt;
        var result = await materializer.MaterializeAsync(throughAt, clock.GetUtcNow(), Actor(context), context.TraceIdentifier,
            context.Connection.RemoteIpAddress?.ToString(), ct);
        return Results.Ok(new FinancialMaterializationResponse(throughDate, result.CreatedCount));
    }

    private static Task<IResult> Pay(Guid id, ChargeConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider clock, TimeZoneInfo zone, CancellationToken ct) =>
        Mutate(id, request.ConcurrencyToken, FinancialMutation.Pay, null, context, db, clock, zone, ct);

    private static Task<IResult> Adjust(Guid id, AdjustChargeRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider clock, TimeZoneInfo zone, CancellationToken ct) =>
        Mutate(id, request.ConcurrencyToken, FinancialMutation.Adjust, (request.FinalAmount, request.AdjustmentReason), context, db, clock, zone, ct);

    private static Task<IResult> Cancel(Guid id, CancelChargeRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider clock, TimeZoneInfo zone, CancellationToken ct) =>
        Mutate(id, request.ConcurrencyToken, FinancialMutation.Cancel, request.Reason, context, db, clock, zone, ct);

    private static async Task<IResult> Mutate(Guid id, string? token, FinancialMutation mutation, object? value,
        HttpContext context, ApplicationDbContext db, TimeProvider clock, TimeZoneInfo zone, CancellationToken ct)
    {
        if (!ConcurrencyToken.TryDecode(token, out var version)) return Bad("INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido.");
        if (mutation == FinancialMutation.Adjust && value is ValueTuple<decimal, string?> adjustmentValue && !GestaoPredio.Domain.Rooms.RoomRate.IsValid(adjustmentValue.Item1))
            return Bad("INVALID_FINANCIAL_AMOUNT", "O valor financeiro informado é inválido.");
        if (mutation == FinancialMutation.Cancel && (value is not string || string.IsNullOrWhiteSpace(value as string)))
            return Bad("JUSTIFICATION_REQUIRED", "A justificativa é obrigatória.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var charge = await db.FinancialCharges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (charge is null) return Results.NotFound();
        if (charge.Version != version) return Modified();
        var now = clock.GetUtcNow();
        var action = mutation switch { FinancialMutation.Pay => AuditActions.FinancialChargePaid, FinancialMutation.Adjust => AuditActions.FinancialChargeAdjusted, _ => AuditActions.FinancialChargeCancelled };
        try
        {
            switch (mutation)
            {
                case FinancialMutation.Pay: charge.MarkPaid(now); break;
                case FinancialMutation.Adjust:
                    var adjustment = ((decimal, string?))value!;
                    if (adjustment.Item2 is null || string.IsNullOrWhiteSpace(adjustment.Item2)) return Bad("JUSTIFICATION_REQUIRED", "A justificativa é obrigatória.");
                    charge.Adjust(adjustment.Item1, adjustment.Item2, now); break;
                case FinancialMutation.Cancel: charge.Cancel((string)value!, now); break;
            }
        }
        catch (ArgumentException) { return Bad("INVALID_FINANCIAL_OPERATION", "A operação financeira é inválida."); }
        catch (InvalidOperationException) { return Results.Json(new ApiError("INVALID_FINANCIAL_STATE", "A cobrança não permite esta operação no estado atual."), statusCode: 409); }
        db.Entry(charge).Property(x => x.Version).OriginalValue = version;
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(), TargetEntityType = AuditTargetTypes.FinancialCharge, TargetEntityId = charge.Id,
            Action = action, Result = "SUCCEEDED", OccurredAt = now, CorrelationId = context.TraceIdentifier,
            ActorUserId = Actor(context), IpAddress = context.Connection.RemoteIpAddress?.ToString()
        });
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(ct); return Modified(); }
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        return Results.Ok(Map(charge, null, null, today));
    }

    private static async Task<IResult> Summary(DateOnly? from, DateOnly? to, ApplicationDbContext db,
        TimeProvider clock, TimeZoneInfo zone, CancellationToken ct)
    {
        if (from > to) return Bad("INVALID_PERIOD", "O período informado é inválido.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var query = db.FinancialCharges.AsNoTracking();
        if (from is not null) query = query.Where(x => x.DueDate >= from);
        if (to is not null) query = query.Where(x => x.DueDate <= to);
        var pending = query.Where(x => x.Status == FinancialChargeStatus.Pending && x.DueDate >= today);
        var overdue = query.Where(x => x.Status == FinancialChargeStatus.Pending && x.DueDate < today);
        var paid = query.Where(x => x.Status == FinancialChargeStatus.Paid);
        return Results.Ok(new FinancialSummaryResponse(await pending.SumAsync(x => (decimal?)x.FinalAmount, ct) ?? 0m,
            await overdue.SumAsync(x => (decimal?)x.FinalAmount, ct) ?? 0m,
            await paid.SumAsync(x => (decimal?)x.FinalAmount, ct) ?? 0m,
            await pending.CountAsync(ct), await overdue.CountAsync(ct), await paid.CountAsync(ct)));
    }

    private static FinancialChargeResponse Map(FinancialCharge charge, string? professionalName, string? tenantName, DateOnly today) =>
        new(charge.Id, charge.LeaseId, charge.ProfessionalId, charge.TenantId, charge.ReferencePeriodStart,
            charge.ReferencePeriodEnd, charge.DueDate, charge.CalculatedAmount, charge.FinalAmount,
            charge.Status == FinancialChargeStatus.Pending && charge.IsOverdue(today) ? "OVERDUE" : charge.Status.ToString().ToUpperInvariant(),
            charge.CalculationDetails, charge.AdjustmentReason, charge.CancellationReason, charge.PaidAt,
            charge.CreatedAt, charge.UpdatedAt, ConcurrencyToken.Encode(charge.Version), professionalName, tenantName);

    private static async Task<Guid?> ResolveProfessional(ApplicationDbContext db, HttpContext context, CancellationToken ct)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId) ? null : await db.Professionals.AsNoTracking()
            .Where(x => x.ApplicationUserId == userId && x.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
    }

    private static bool TryStatus(string? value, out string? status)
    {
        status = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        return status is null or "ALL" or "PENDING" or "OVERDUE" or "PAID" or "CANCELLED";
    }
    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static IResult Bad(string code, string message) => Results.BadRequest(new ApiError(code, message));
    private static IResult Modified() => Results.Json(new ApiError("RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."), statusCode: 409);
    private enum FinancialMutation { Pay, Adjust, Cancel }
}
