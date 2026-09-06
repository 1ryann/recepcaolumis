using System.Security.Claims;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Leases;

public static partial class LeaseEndpoints
{
    private static async Task<IResult> Update(
        Guid id,
        UpdateLeaseRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        ILeaseConflictDetector conflictDetector,
        ILeaseOccurrencePlanner planner,
        TimeZoneInfo timeZone,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var createRequest = new CreateLeaseRequest(request.TenantId, request.ProfessionalId, request.RoomId,
            request.Mode, request.ContractedRate, request.BillingStartAt, request.BillingDueDay,
            request.OccupancyStartAt, request.OccupancyEndAt);
        if (!TryBuildContract(createRequest, timeZone, out var contract)) return Invalid();

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lease = await db.Leases.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lease is null) return Results.NotFound();
        if (lease.Version != version) return Modified();
        if (lease.GetOperationalStatus(now) != LeaseOperationalStatus.Scheduled) return InvalidTransition();

        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [lease.TenantId, request.TenantId], [lease.RoomId, request.RoomId],
            [lease.ProfessionalId, request.ProfessionalId]), cancellationToken);
        if (!await ResourcesAreActive(db, request.TenantId, request.ProfessionalId, request.RoomId, cancellationToken))
            return InvalidResource();
        var conflict = await conflictDetector.FindConflictAsync(request.RoomId, request.ProfessionalId,
            contract!.OccupancyStartAt, contract.OccupancyEndAt, lease.Id, cancellationToken);
        if (conflict.Any) return Conflict();

        var changedFields = ChangedFields(lease, request, contract);
        if (changedFields.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var currentNames = await LoadNames(db, lease, cancellationToken);
            return Results.Ok(lease.ToResponse(currentNames.Tenant, currentNames.Professional, currentNames.Room, now));
        }

        db.Entry(lease).Property(x => x.Version).OriginalValue = version;
        lease.UpdateScheduled(request.TenantId, request.ProfessionalId, request.RoomId, contract.Mode,
            request.ContractedRate, request.BillingStartAt, request.BillingDueDay,
            contract.OccupancyStartAt, contract.OccupancyEndAt, contract.MonthlyAnchorDay, now);
        var occurrences = await db.LeaseOccurrences.Where(x => x.LeaseId == lease.Id).ToListAsync(cancellationToken);
        ApplyPlan(db, lease, occurrences, planner.Plan(lease, now, occurrences), now);
        db.AuditEntries.Add(LeaseAudit.CreateSucceeded(lease.Id, AuditActions.LeaseUpdated, now,
            context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString(), changedFields));
        return await SaveMutation(db, transaction, lease, version, now, cancellationToken);
    }

    private static async Task<IResult> Postpone(
        Guid id,
        PostponeLeaseRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        ILeaseConflictDetector conflictDetector,
        ILeaseOccurrencePlanner planner,
        TimeZoneInfo timeZone,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lease = await db.Leases.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lease is null) return Results.NotFound();
        if (lease.Version != version) return Modified();
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([lease.TenantId], [lease.RoomId], [lease.ProfessionalId]), cancellationToken);
        if (!await ResourcesAreActive(db, lease.TenantId, lease.ProfessionalId, lease.RoomId, cancellationToken))
            return InvalidResource();

        var newStart = request.OccupancyStartAt;
        int? anchor = null;
        if (lease.Mode == LeaseMode.Daily)
        {
            var local = TimeZoneInfo.ConvertTime(newStart, timeZone);
            newStart = OperationalTimeZone.GetCivilDayInterval(DateOnly.FromDateTime(local.DateTime), timeZone).StartAt;
        }
        else if (lease.Mode == LeaseMode.Monthly)
            anchor = TimeZoneInfo.ConvertTime(newStart, timeZone).Day;

        var conflict = await conflictDetector.FindConflictAsync(lease.RoomId, lease.ProfessionalId,
            newStart, lease.OccupancyEndAt, lease.Id, cancellationToken);
        if (conflict.Any) return Conflict();
        db.Entry(lease).Property(x => x.Version).OriginalValue = version;
        try { lease.PostponeOccupancy(newStart, anchor, now); }
        catch (InvalidOperationException) { return InvalidTransition(); }
        catch (ArgumentException) { return Invalid(); }
        var occurrences = await db.LeaseOccurrences.Where(x => x.LeaseId == id).ToListAsync(cancellationToken);
        ApplyPlan(db, lease, occurrences, planner.Plan(lease, now, occurrences), now);
        db.AuditEntries.Add(LeaseAudit.CreateSucceeded(id, AuditActions.LeaseOccupancyPostponed, now,
            context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        return await SaveMutation(db, transaction, lease, version, now, cancellationToken);
    }

    private static async Task<IResult> Cancel(
        Guid id,
        LeaseConcurrencyRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lease = await db.Leases.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lease is null) return Results.NotFound();
        if (lease.Version != version) return Modified();
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([lease.TenantId], [lease.RoomId], [lease.ProfessionalId]), cancellationToken);
        db.Entry(lease).Property(x => x.Version).OriginalValue = version;
        try { lease.Cancel(now); }
        catch (InvalidOperationException) { return InvalidTransition(); }
        var occurrences = await db.LeaseOccurrences.Where(x => x.LeaseId == id).ToListAsync(cancellationToken);
        foreach (var occurrence in occurrences.Where(x => x.State == LeaseOccurrenceState.Planned && x.StartAt >= now))
            occurrence.Cancel(now);
        db.AuditEntries.Add(LeaseAudit.CreateSucceeded(id, AuditActions.LeaseCancelled, now,
            context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        return await SaveMutation(db, transaction, lease, version, now, cancellationToken);
    }

    private static async Task<IResult> End(
        Guid id,
        EndLeaseRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        ILeaseOccurrencePlanner planner,
        ILeaseLifecycleCoordinator lifecycle,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lease = await db.Leases.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lease is null) return Results.NotFound();
        if (lease.Version != version) return Modified();
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([lease.TenantId], [lease.RoomId], [lease.ProfessionalId]), cancellationToken);
        db.Entry(lease).Property(x => x.Version).OriginalValue = version;
        var occurrences = await db.LeaseOccurrences.Where(x => x.LeaseId == id).ToListAsync(cancellationToken);
        string action;
        try
        {
            if (request.EndAt is { } endAt && endAt > now)
            {
                if (lease.OccupancyEndAt == endAt)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    var currentNames = await LoadNames(db, lease, cancellationToken);
                    return Results.Ok(lease.ToResponse(currentNames.Tenant, currentNames.Professional, currentNames.Room, now));
                }
                lease.ScheduleEnd(endAt, now);
                ApplyPlan(db, lease, occurrences, planner.Plan(lease, now, occurrences), now);
                action = AuditActions.LeaseEndScheduled;
            }
            else
            {
                lease.EndImmediately(now);
                var reconciliation = await lifecycle.ReconcileAsync(lease, occurrences, now, cancellationToken);
                action = reconciliation == LeaseLifecycleReconciliation.EndingPending
                    ? AuditActions.LeaseEndingPending
                    : AuditActions.LeaseEnded;
            }
        }
        catch (InvalidOperationException) { return InvalidTransition(); }
        catch (ArgumentException) { return Invalid(); }

        db.AuditEntries.Add(LeaseAudit.CreateSucceeded(id, action, now,
            context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        return await SaveMutation(db, transaction, lease, version, now, cancellationToken);
    }

    private static async Task<IResult> SaveMutation(
        ApplicationDbContext db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Lease lease,
        uint version,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Modified();
        }
        var names = await LoadNames(db, lease, cancellationToken);
        return Results.Ok(lease.ToResponse(names.Tenant, names.Professional, names.Room, now));
    }

    private static void ApplyPlan(ApplicationDbContext db, Lease lease, IReadOnlyCollection<LeaseOccurrence> occurrences,
        LeaseOccurrencePlan plan, DateTimeOffset now)
    {
        var cancelled = plan.ToCancel.ToHashSet();
        foreach (var period in plan.ToCreate)
        {
            var existing = occurrences.SingleOrDefault(x => x.State == LeaseOccurrenceState.Planned && x.StartAt == period.StartAt);
            if (existing is not null)
            {
                cancelled.Remove(existing.Id);
                if (existing.EndAt != period.EndAt) existing.RescheduleEnd(period.EndAt, now);
            }
            else
                db.LeaseOccurrences.Add(LeaseOccurrence.Create(lease.Id, period.StartAt, period.EndAt, now));
        }
        foreach (var occurrence in occurrences.Where(x => cancelled.Contains(x.Id))) occurrence.Cancel(now);
        lease.SetMaterializedThrough(plan.MaterializedThroughAt);
    }

    private static async Task<bool> ResourcesAreActive(ApplicationDbContext db, Guid tenantId, Guid professionalId,
        Guid roomId, CancellationToken cancellationToken) =>
        await db.Tenants.AnyAsync(x => x.Id == tenantId && x.IsActive, cancellationToken) &&
        await db.Professionals.AnyAsync(x => x.Id == professionalId && x.IsActive, cancellationToken) &&
        await db.Rooms.AnyAsync(x => x.Id == roomId && x.IsActive, cancellationToken);

    private static List<string> ChangedFields(Lease lease, UpdateLeaseRequest request, LeaseContract contract)
    {
        var fields = new List<string>();
        if (lease.TenantId != request.TenantId) fields.Add(AuditFields.TenantId);
        if (lease.ProfessionalId != request.ProfessionalId) fields.Add(AuditFields.ProfessionalId);
        if (lease.RoomId != request.RoomId) fields.Add(AuditFields.RoomId);
        if (lease.Mode != contract.Mode) fields.Add(AuditFields.Mode);
        if (lease.ContractedRate != request.ContractedRate) fields.Add(AuditFields.ContractedRate);
        if (lease.BillingStartAt != NormalizeTimestamp(request.BillingStartAt)) fields.Add(AuditFields.BillingStartAt);
        if (lease.BillingDueDay != request.BillingDueDay) fields.Add(AuditFields.BillingDueDay);
        if (lease.OccupancyStartAt != NormalizeTimestamp(contract.OccupancyStartAt)) fields.Add(AuditFields.OccupancyStartAt);
        DateTimeOffset? end = contract.OccupancyEndAt is null ? null : NormalizeTimestamp(contract.OccupancyEndAt.Value);
        if (lease.OccupancyEndAt != end) fields.Add(AuditFields.OccupancyEndAt);
        return fields;
    }

    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }
    private static IResult InvalidToken() => Results.BadRequest(new ApiError(
        "INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError(
        "RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."),
        statusCode: StatusCodes.Status409Conflict);
    private static IResult InvalidTransition() => Results.Json(new ApiError(
        "INVALID_LEASE_TRANSITION", "A locação não permite esta operação no estado atual."),
        statusCode: StatusCodes.Status409Conflict);
    private static IResult InvalidResource() => Results.BadRequest(new ApiError(
        "INVALID_LEASE_RESOURCE", "Os recursos informados para a locação são inválidos."));
    private static IResult Conflict() => Results.Json(new ApiError(
        "LEASE_RESOURCE_CONFLICT", "A sala ou o profissional já possui ocupação conflitante."),
        statusCode: StatusCodes.Status409Conflict);
}
