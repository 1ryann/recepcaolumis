namespace GestaoPredio.Domain.Professionals;

public enum PresenceEndReason : short
{
    ManagerManual = 0,
    IncidentUntilTime = 1,
    IncidentRestOfDay = 2,
    OperatingHoursElapsed = 3
}
