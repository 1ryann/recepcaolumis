using GestaoPredio.Application.Availability;
using GestaoPredio.Application.OperationalAlerts;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.OperationalAlerts;

public sealed class PostgreSqlOperationalAlertReader(ApplicationDbContext db, TimeZoneInfo timeZone)
    : IOperationalAlertReader
{
    private static readonly TimeSpan SoonWindow = TimeSpan.FromHours(1);

    public async Task<IReadOnlyList<OperationalAlert>> ReadAsync(
        OperationalAlertFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var current = now.ToUniversalTime();
        var alerts = new List<OperationalAlert>();

        if (Includes(filter, OperationalAlertType.ReservationEndedVisitWaiting, OperationalAlertSeverity.Warning))
            alerts.AddRange(await ReadEndedReservationVisitsAsync(
                filter, current, VisitStatus.Waiting, OperationalAlertType.ReservationEndedVisitWaiting,
                OperationalAlertSeverity.Warning, cancellationToken));

        if (Includes(filter, OperationalAlertType.ReservationEndedVisitInService, OperationalAlertSeverity.Critical))
            alerts.AddRange(await ReadEndedReservationVisitsAsync(
                filter, current, VisitStatus.InService, OperationalAlertType.ReservationEndedVisitInService,
                OperationalAlertSeverity.Critical, cancellationToken));

        if (Includes(filter, OperationalAlertType.NextReservationSoon, OperationalAlertSeverity.Warning))
        {
            alerts.AddRange(await ReadNextReservationAsync(
                filter, current, conflict: false, cancellationToken));
            alerts.AddRange(await ReadNextReservationDuringLeaseOccurrenceAsync(
                filter, current, conflict: false, cancellationToken));
        }

        if (Includes(filter, OperationalAlertType.NextReservationConflict, OperationalAlertSeverity.Critical))
        {
            alerts.AddRange(await ReadNextReservationAsync(
                filter, current, conflict: true, cancellationToken));
            alerts.AddRange(await ReadNextReservationDuringLeaseOccurrenceAsync(
                filter, current, conflict: true, cancellationToken));
        }

        if (filter.Type is null or OperationalAlertType.LeaseEndingWithActiveVisit)
            alerts.AddRange(await ReadEndingLeasesAsync(filter, current, cancellationToken));

        if (Includes(filter, OperationalAlertType.CustomerWaitingProfessionalAbsent, OperationalAlertSeverity.Critical))
            alerts.AddRange(await ReadWaitingVisitAbsentProfessionalAsync(filter, current, cancellationToken));

        if (Includes(filter, OperationalAlertType.OpenVisitAffectedByIncident, OperationalAlertSeverity.Critical))
            alerts.AddRange(await ReadOpenVisitAffectedByIncidentAsync(filter, current, cancellationToken));

        return alerts
            .OrderByDescending(alert => alert.Severity)
            .ThenBy(alert => alert.ConditionAt)
            .ThenBy(alert => alert.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<OperationalAlert>> ReadEndedReservationVisitsAsync(
        OperationalAlertFilter filter,
        DateTimeOffset now,
        VisitStatus status,
        OperationalAlertType type,
        OperationalAlertSeverity severity,
        CancellationToken cancellationToken)
    {
        var visits = db.Visits.AsNoTracking().Where(visit => visit.Status == status);
        var reservations = db.Reservations.AsNoTracking().Where(reservation =>
            reservation.Status == ReservationStatus.Approved &&
            reservation.Kind != ReservationKind.Cancellation && reservation.EndAt <= now);
        if (filter.ProfessionalId is { } professionalId)
            visits = visits.Where(visit => visit.ProfessionalId == professionalId);
        if (filter.RoomId is { } roomId)
            reservations = reservations.Where(reservation => reservation.RoomId == roomId);
        var query =
            from visit in visits
            join reservation in reservations on visit.ReservationId equals reservation.Id
            join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
            join professional in db.Professionals.AsNoTracking() on visit.ProfessionalId equals professional.Id
            select new AlertRow(room.Id, room.Name, professional.Id, professional.Name,
                reservation.Id, visit.Id, null, reservation.EndAt);
        var rows = await query.ToListAsync(cancellationToken);
        return rows.Select(row => Create(type, severity, row)).ToArray();
    }

    private async Task<IReadOnlyList<OperationalAlert>> ReadNextReservationAsync(
        OperationalAlertFilter filter,
        DateTimeOffset now,
        bool conflict,
        CancellationToken cancellationToken)
    {
        var windowEnd = now.Add(SoonWindow);
        var visits = db.Visits.AsNoTracking().Where(visit => visit.Status == VisitStatus.InService);
        var reservations = db.Reservations.AsNoTracking().Where(reservation =>
            reservation.Status == ReservationStatus.Approved &&
            reservation.Kind != ReservationKind.Cancellation);
        if (filter.ProfessionalId is { } professionalId)
            visits = visits.Where(visit => visit.ProfessionalId == professionalId);
        if (filter.RoomId is { } roomId)
        {
            visits = visits.Where(visit => visit.RoomId == roomId);
            reservations = reservations.Where(reservation => reservation.RoomId == roomId);
        }
        var query =
            from visit in visits
            join reservation in reservations on visit.RoomId equals reservation.RoomId
            join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
            join professional in db.Professionals.AsNoTracking() on visit.ProfessionalId equals professional.Id
            where visit.ReservationId != reservation.Id &&
                  (conflict
                      ? reservation.StartAt <= now && reservation.EndAt > now && visit.ArrivedAt < reservation.StartAt
                      : reservation.StartAt > now && reservation.StartAt <= windowEnd)
            select new AlertRow(room.Id, room.Name, professional.Id, professional.Name,
                reservation.Id, visit.Id, null, reservation.StartAt);
        var rows = await query.ToListAsync(cancellationToken);
        var type = conflict ? OperationalAlertType.NextReservationConflict : OperationalAlertType.NextReservationSoon;
        var severity = conflict ? OperationalAlertSeverity.Critical : OperationalAlertSeverity.Warning;
        return rows.Select(row => Create(type, severity, row)).ToArray();
    }

    private async Task<IReadOnlyList<OperationalAlert>> ReadEndingLeasesAsync(
        OperationalAlertFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var windowEnd = now.Add(SoonWindow);
        var visits = db.Visits.AsNoTracking().Where(visit => visit.RoomId != null &&
            (visit.Status == VisitStatus.Waiting || visit.Status == VisitStatus.InService));
        var leases = db.Leases.AsNoTracking().Where(lease =>
            lease.LifecycleState != LeaseLifecycleState.Cancelled && lease.OccupancyStartAt <= now &&
            lease.OccupancyEndAt != null && lease.OccupancyEndAt <= windowEnd);
        if (filter.ProfessionalId is { } professionalId)
        {
            visits = visits.Where(visit => visit.ProfessionalId == professionalId);
            leases = leases.Where(lease => lease.ProfessionalId == professionalId);
        }
        if (filter.RoomId is { } roomId)
        {
            visits = visits.Where(visit => visit.RoomId == roomId);
            leases = leases.Where(lease => lease.RoomId == roomId);
        }
        var query =
            from lease in leases
            join visit in visits
                on new { lease.RoomId, lease.ProfessionalId } equals new { RoomId = visit.RoomId!.Value, visit.ProfessionalId }
            join room in db.Rooms.AsNoTracking() on lease.RoomId equals room.Id
            join professional in db.Professionals.AsNoTracking() on lease.ProfessionalId equals professional.Id
            select new AlertRow(room.Id, room.Name, professional.Id, professional.Name,
                null, visit.Id, lease.Id, lease.OccupancyEndAt!.Value);
        var rows = await query.ToListAsync(cancellationToken);
        return rows
            .Select(row => Create(OperationalAlertType.LeaseEndingWithActiveVisit,
                row.ConditionAt <= now ? OperationalAlertSeverity.Critical : OperationalAlertSeverity.Warning, row))
            .Where(alert => filter.Severity is null || alert.Severity == filter.Severity)
            .ToArray();
    }

    private async Task<IReadOnlyList<OperationalAlert>> ReadNextReservationDuringLeaseOccurrenceAsync(
        OperationalAlertFilter filter,
        DateTimeOffset now,
        bool conflict,
        CancellationToken cancellationToken)
    {
        var windowEnd = now.Add(SoonWindow);
        var occurrences = db.LeaseOccurrences.AsNoTracking().Where(occurrence =>
            occurrence.State == LeaseOccurrenceState.Planned &&
            occurrence.StartAt <= now && occurrence.EndAt > now);
        var leases = db.Leases.AsNoTracking().Where(lease =>
            lease.LifecycleState != LeaseLifecycleState.Cancelled &&
            lease.LifecycleState != LeaseLifecycleState.Ended);
        var reservations = db.Reservations.AsNoTracking().Where(reservation =>
            reservation.Status == ReservationStatus.Approved &&
            reservation.Kind != ReservationKind.Cancellation);
        if (filter.ProfessionalId is { } professionalId)
            leases = leases.Where(lease => lease.ProfessionalId == professionalId);
        if (filter.RoomId is { } roomId)
        {
            leases = leases.Where(lease => lease.RoomId == roomId);
            reservations = reservations.Where(reservation => reservation.RoomId == roomId);
        }

        var query =
            from occurrence in occurrences
            join lease in leases on occurrence.LeaseId equals lease.Id
            join reservation in reservations on lease.RoomId equals reservation.RoomId
            join room in db.Rooms.AsNoTracking() on lease.RoomId equals room.Id
            join professional in db.Professionals.AsNoTracking() on lease.ProfessionalId equals professional.Id
            where (conflict
                      ? reservation.StartAt <= now && reservation.EndAt > now && occurrence.StartAt < reservation.StartAt
                      : reservation.StartAt > now && reservation.StartAt <= windowEnd) &&
                  !db.Visits.Any(visit => visit.RoomId == lease.RoomId &&
                      visit.Status == VisitStatus.InService && visit.ReservationId != reservation.Id)
            select new AlertRow(room.Id, room.Name, professional.Id, professional.Name,
                reservation.Id, null, lease.Id, reservation.StartAt);
        var rows = await query.ToListAsync(cancellationToken);
        var type = conflict ? OperationalAlertType.NextReservationConflict : OperationalAlertType.NextReservationSoon;
        var severity = conflict ? OperationalAlertSeverity.Critical : OperationalAlertSeverity.Warning;
        return rows.Select(row => Create(type, severity, row)).ToArray();
    }

    private async Task<IReadOnlyList<OperationalAlert>> ReadWaitingVisitAbsentProfessionalAsync(
        OperationalAlertFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var visits = db.Visits.AsNoTracking().Where(visit => visit.Status == VisitStatus.Waiting);
        if (filter.ProfessionalId is { } professionalFilter)
            visits = visits.Where(visit => visit.ProfessionalId == professionalFilter);
        if (filter.RoomId is { } roomFilter)
            visits = visits.Where(visit => visit.RoomId == roomFilter);

        var candidates = await (
            from visit in visits
            join professional in db.Professionals.AsNoTracking() on visit.ProfessionalId equals professional.Id
            join room in db.Rooms.AsNoTracking() on visit.RoomId equals room.Id into roomJoin
            from room in roomJoin.DefaultIfEmpty()
            select new AlertRow((Guid?)room.Id, room.Name, professional.Id, professional.Name,
                visit.ReservationId, visit.Id, null, visit.ArrivedAt)).ToListAsync(cancellationToken);
        if (candidates.Count == 0) return [];

        var professionalIds = candidates.Select(row => row.ProfessionalId).Distinct().ToArray();
        var openPresences = await db.ProfessionalPresences.AsNoTracking()
            .Where(presence => professionalIds.Contains(presence.ProfessionalId) && presence.EndedAt == null)
            .ToListAsync(cancellationToken);
        var presenceByProfessional = openPresences
            .GroupBy(presence => presence.ProfessionalId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.StartedAt).First());
        var operatingHours = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(cancellationToken);

        var alerts = new List<OperationalAlert>();
        foreach (var row in candidates)
        {
            presenceByProfessional.TryGetValue(row.ProfessionalId, out var presence);
            if (PresenceEvaluator.IsEffective(presence, operatingHours, now, timeZone)) continue;
            alerts.Add(Create(OperationalAlertType.CustomerWaitingProfessionalAbsent,
                OperationalAlertSeverity.Critical, row));
        }
        return alerts;
    }

    private async Task<IReadOnlyList<OperationalAlert>> ReadOpenVisitAffectedByIncidentAsync(
        OperationalAlertFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        var localDate = DateOnly.FromDateTime(localNow);
        var localTime = TimeOnly.FromDateTime(localNow);

        var visits = db.Visits.AsNoTracking().Where(visit =>
            visit.Status == VisitStatus.Waiting || visit.Status == VisitStatus.InService);
        if (filter.ProfessionalId is { } professionalFilter)
            visits = visits.Where(visit => visit.ProfessionalId == professionalFilter);
        if (filter.RoomId is { } roomFilter)
            visits = visits.Where(visit => visit.RoomId == roomFilter);

        var candidates = await (
            from visit in visits
            join professional in db.Professionals.AsNoTracking() on visit.ProfessionalId equals professional.Id
            join room in db.Rooms.AsNoTracking() on visit.RoomId equals room.Id into roomJoin
            from room in roomJoin.DefaultIfEmpty()
            select new AlertRow((Guid?)room.Id, room.Name, professional.Id, professional.Name,
                visit.ReservationId, visit.Id, null, visit.ArrivedAt)).ToListAsync(cancellationToken);
        if (candidates.Count == 0) return [];

        var professionalIds = candidates.Select(row => row.ProfessionalId).Distinct().ToArray();
        var incidentProfessionalIds = (await db.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(exception => professionalIds.Contains(exception.ProfessionalId) &&
                exception.Origin == ProfessionalAvailabilityExceptionOrigin.Incident &&
                !exception.AllDay && exception.Date == localDate &&
                exception.StartTime <= localTime && exception.EndTime > localTime)
            .Select(exception => exception.ProfessionalId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        var reservationIds = candidates
            .Where(row => row.ReservationId is not null)
            .Select(row => row.ReservationId!.Value)
            .Distinct()
            .ToArray();
        var affectedReservationIds = reservationIds.Length == 0
            ? new HashSet<Guid>()
            : (await db.Reservations.AsNoTracking()
                .Where(reservation => reservationIds.Contains(reservation.Id) &&
                    reservation.Status == ReservationStatus.Cancelled &&
                    reservation.CancellationReason == ReservationCancellationReason.ProfessionalUnavailable)
                .Select(reservation => reservation.Id)
                .ToListAsync(cancellationToken)).ToHashSet();

        return candidates
            .Where(row => incidentProfessionalIds.Contains(row.ProfessionalId) ||
                (row.ReservationId is { } reservationId && affectedReservationIds.Contains(reservationId)))
            .Select(row => Create(OperationalAlertType.OpenVisitAffectedByIncident,
                OperationalAlertSeverity.Critical, row))
            .ToArray();
    }

    private static bool Includes(
        OperationalAlertFilter filter,
        OperationalAlertType type,
        OperationalAlertSeverity severity) =>
        (filter.Type is null || filter.Type == type) &&
        (filter.Severity is null || filter.Severity == severity);

    private static OperationalAlert Create(
        OperationalAlertType type,
        OperationalAlertSeverity severity,
        AlertRow row)
    {
        var (title, message) = type switch
        {
            OperationalAlertType.ReservationEndedVisitWaiting =>
                ("Reserva encerrada com visita aguardando", "A reserva terminou e a visita vinculada ainda está aguardando atendimento."),
            OperationalAlertType.ReservationEndedVisitInService =>
                ("Reserva encerrada com atendimento ativo", "A reserva terminou e a visita vinculada continua em atendimento."),
            OperationalAlertType.NextReservationSoon =>
                ("Próxima reserva em até uma hora", "Há atendimento ativo na sala e outra reserva começará em até uma hora."),
            OperationalAlertType.NextReservationConflict =>
                ("Próxima reserva em conflito", "A próxima reserva já começou e a sala ainda possui atendimento anterior ativo."),
            OperationalAlertType.LeaseEndingWithActiveVisit =>
                ("Locação encerrando com visita aberta", "A locação está encerrando ou encerrou e ainda existe uma visita aberta."),
            OperationalAlertType.CustomerWaitingProfessionalAbsent =>
                ("Cliente aguardando com profissional ausente", "Há uma visita aguardando atendimento e o profissional não está presente no momento."),
            OperationalAlertType.OpenVisitAffectedByIncident =>
                ("Visita aberta afetada por imprevisto", "Um imprevisto do profissional afeta uma visita que ainda está aberta."),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        var id = string.Join(':', Contract(type), row.VisitId, row.ReservationId, row.LeaseId);
        return new OperationalAlert(id, type, severity, title, message, row.ConditionAt,
            row.RoomId, row.RoomName, row.ProfessionalId, row.ProfessionalName,
            row.ReservationId, row.VisitId, row.LeaseId);
    }

    internal static string Contract(OperationalAlertType type) => type switch
    {
        OperationalAlertType.ReservationEndedVisitWaiting => "RESERVATION_ENDED_VISIT_WAITING",
        OperationalAlertType.ReservationEndedVisitInService => "RESERVATION_ENDED_VISIT_IN_SERVICE",
        OperationalAlertType.NextReservationSoon => "NEXT_RESERVATION_SOON",
        OperationalAlertType.NextReservationConflict => "NEXT_RESERVATION_CONFLICT",
        OperationalAlertType.LeaseEndingWithActiveVisit => "LEASE_ENDING_WITH_ACTIVE_VISIT",
        OperationalAlertType.CustomerWaitingProfessionalAbsent => "CUSTOMER_WAITING_PROFESSIONAL_ABSENT",
        OperationalAlertType.OpenVisitAffectedByIncident => "OPEN_VISIT_AFFECTED_BY_INCIDENT",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private sealed record AlertRow(
        Guid? RoomId,
        string? RoomName,
        Guid ProfessionalId,
        string ProfessionalName,
        Guid? ReservationId,
        Guid? VisitId,
        Guid? LeaseId,
        DateTimeOffset ConditionAt);
}
