using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Professionals;

public sealed class ProfessionalPresence
{
    private ProfessionalPresence() { }

    public Guid Id { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public PresenceEndReason? EndReason { get; private set; }
    public PresenceSource Source { get; private set; }
    public string? CreatedByUserId { get; private set; }
    public uint Version { get; private set; }

    public bool IsOpen => EndedAt is null;

    public static ProfessionalPresence StartByQr(Guid professionalId, DateTimeOffset now)
        => Start(professionalId, PresenceSource.QrSelfScan, null, now);

    public static ProfessionalPresence StartByManager(Guid professionalId, string managerUserId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(managerUserId) || managerUserId.Length > 450)
            throw new ArgumentException("O responsável deve ser informado.", nameof(managerUserId));
        return Start(professionalId, PresenceSource.ManagerManual, managerUserId, now);
    }

    public void EndManually(string managerUserId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(managerUserId) || managerUserId.Length > 450)
            throw new ArgumentException("O responsável deve ser informado.", nameof(managerUserId));
        Close(PresenceEndReason.ManagerManual, now);
    }

    public void EndForIncident(bool restOfDay, DateTimeOffset now)
        => Close(restOfDay ? PresenceEndReason.IncidentRestOfDay : PresenceEndReason.IncidentUntilTime, now);

    public void MaterialiseOperatingHoursEnd(DateTimeOffset closeInstant)
        => Close(PresenceEndReason.OperatingHoursElapsed, closeInstant);

    private static ProfessionalPresence Start(Guid professionalId, PresenceSource source,
        string? createdByUserId, DateTimeOffset now)
    {
        if (professionalId == Guid.Empty)
            throw new ArgumentException("O profissional deve ser informado.", nameof(professionalId));
        return new ProfessionalPresence
        {
            Id = Guid.NewGuid(),
            ProfessionalId = professionalId,
            Source = source,
            CreatedByUserId = createdByUserId,
            StartedAt = TimestampNormalizer.ToUtcMicroseconds(now)
        };
    }

    private void Close(PresenceEndReason reason, DateTimeOffset at)
    {
        if (EndedAt is not null)
            throw new InvalidOperationException("A presença já foi encerrada.");
        EndedAt = TimestampNormalizer.ToUtcMicroseconds(at);
        EndReason = reason;
    }
}
