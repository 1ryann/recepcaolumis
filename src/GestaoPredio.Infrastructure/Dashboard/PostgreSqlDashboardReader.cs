using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Dashboard;
using GestaoPredio.Application.Finance;
using GestaoPredio.Application.OperationalAlerts;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Dashboard;

public sealed class PostgreSqlDashboardReader(
    ApplicationDbContext db,
    IOperationalAlertReader alertReader,
    IFinancialSummaryReader financialSummaryReader,
    IRoomAvailabilityService roomAvailability,
    TimeZoneInfo operationalTimeZone) : IDashboardReader
{
    public async Task<DashboardSnapshot> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = now.ToUniversalTime();
        var localNow = TimeZoneInfo.ConvertTime(current, operationalTimeZone);
        var operationalDate = DateOnly.FromDateTime(localNow.DateTime);
        var day = OperationalTimeZone.GetCivilDayInterval(operationalDate, operationalTimeZone);

        var activeProfessionals = await db.Professionals.AsNoTracking().CountAsync(x => x.IsActive, cancellationToken);
        var activeRooms = await db.Rooms.AsNoTracking().CountAsync(x => x.IsActive, cancellationToken);
        var activeLeases = await db.Leases.AsNoTracking().CountAsync(x =>
            (x.LifecycleState == LeaseLifecycleState.Open || x.LifecycleState == LeaseLifecycleState.EndingPending) &&
            x.OccupancyStartAt <= current && (x.OccupancyEndAt == null || x.OccupancyEndAt > current), cancellationToken);
        var scheduledLeases = await db.Leases.AsNoTracking().CountAsync(x =>
            x.LifecycleState == LeaseLifecycleState.Open && x.OccupancyStartAt > current, cancellationToken);
        var pendingReservations = await db.Reservations.AsNoTracking().CountAsync(x =>
            x.Status == ReservationStatus.Pending, cancellationToken);
        var todayReservations = await db.Reservations.AsNoTracking().CountAsync(x =>
            x.Status == ReservationStatus.Approved && x.Kind != ReservationKind.Cancellation &&
            x.StartAt < day.EndAt && x.EndAt > day.StartAt, cancellationToken);
        var waitingVisits = await db.Visits.AsNoTracking().CountAsync(x => x.Status == VisitStatus.Waiting, cancellationToken);
        var inServiceVisits = await db.Visits.AsNoTracking().CountAsync(x => x.Status == VisitStatus.InService, cancellationToken);

        var agenda = await (from reservation in db.Reservations.AsNoTracking()
                            join professional in db.Professionals.AsNoTracking() on reservation.ProfessionalId equals professional.Id
                            join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
                            where reservation.Status == ReservationStatus.Approved &&
                                  reservation.Kind != ReservationKind.Cancellation &&
                                  reservation.StartAt < day.EndAt && reservation.EndAt > day.StartAt
                            orderby reservation.StartAt, reservation.Id
                            select new DashboardAgendaItem(reservation.Id, professional.Id, professional.Name,
                                room.Id, room.Name, reservation.StartAt, reservation.EndAt))
            .ToListAsync(cancellationToken);

        var visitRows = await (from visit in db.Visits.AsNoTracking()
                               join professional in db.Professionals.AsNoTracking() on visit.ProfessionalId equals professional.Id
                               join room in db.Rooms.AsNoTracking() on visit.RoomId equals room.Id into roomGroup
                               from room in roomGroup.DefaultIfEmpty()
                               where visit.Status == VisitStatus.Waiting || visit.Status == VisitStatus.InService
                               orderby visit.ArrivedAt, visit.Id
                               select new
                               {
                                   visit.Id,
                                   visit.VisitorName,
                                   visit.Status,
                                   ProfessionalId = professional.Id,
                                   ProfessionalName = professional.Name,
                                   RoomId = (Guid?)room.Id,
                                   RoomName = room.Name,
                                   visit.ArrivedAt,
                                   visit.ServiceStartedAt
                               }).ToListAsync(cancellationToken);
        var currentVisits = visitRows.Select(x => new DashboardCurrentVisit(
            x.Id, x.VisitorName, x.Status.ToString().ToUpperInvariant(), x.ProfessionalId,
            x.ProfessionalName, x.RoomId, x.RoomName, x.ArrivedAt, x.ServiceStartedAt,
            Math.Max(0, (int)Math.Floor((current - (x.ServiceStartedAt ?? x.ArrivedAt)).TotalMinutes)))).ToArray();

        var alerts = await alertReader.ReadAsync(new OperationalAlertFilter(null, null, null, null), current, cancellationToken);
        var alertSummary = new DashboardAlertSummary(
            alerts.Count,
            alerts.Count(x => x.Severity == OperationalAlertSeverity.Warning),
            alerts.Count(x => x.Severity == OperationalAlertSeverity.Critical),
            alerts.Take(10).Select(x => new DashboardAlertItem(
                x.Id, AlertTypeContract(x.Type), x.Severity.ToString().ToUpperInvariant(), x.Title,
                x.Message, x.ConditionAt, x.RoomId, x.ProfessionalId, x.ReservationId, x.VisitId, x.LeaseId)).ToArray());

        var financial = await financialSummaryReader.ReadAsync(
            new DateOnly(operationalDate.Year, operationalDate.Month, 1), operationalDate, operationalDate, cancellationToken);
        var rooms = (await roomAvailability.ReadRoomOperationalStatusAsync(current, cancellationToken))
            .Select(x => new DashboardRoomStatus(x.RoomId, x.RoomName, x.Status, x.NextCommitmentAt)).ToArray();

        var counts = new DashboardCounts(
            activeProfessionals, activeRooms,
            rooms.Count(x => x.Status == "OCCUPIED"),
            rooms.Count(x => x.Status == "RESERVED"),
            activeLeases, scheduledLeases, pendingReservations, todayReservations,
            waitingVisits, inServiceVisits);
        return new DashboardSnapshot(operationalDate, counts, financial, alertSummary, agenda, currentVisits, rooms);
    }

    private static string AlertTypeContract(OperationalAlertType type) => type switch
    {
        OperationalAlertType.ReservationEndedVisitWaiting => "RESERVATION_ENDED_VISIT_WAITING",
        OperationalAlertType.ReservationEndedVisitInService => "RESERVATION_ENDED_VISIT_IN_SERVICE",
        OperationalAlertType.NextReservationSoon => "NEXT_RESERVATION_SOON",
        OperationalAlertType.NextReservationConflict => "NEXT_RESERVATION_CONFLICT",
        OperationalAlertType.LeaseEndingWithActiveVisit => "LEASE_ENDING_WITH_ACTIVE_VISIT",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
