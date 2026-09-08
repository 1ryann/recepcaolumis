namespace GestaoPredio.Application.Notifications;

public static class NotificationBodies
{
    // Customer message for a reservation cancelled by a professional incident (spec §11/§19).
    // Never contains the internal reason, the professional's phone/email, or any customer PII.
    public static string CustomerReschedule(string professionalFirstName, string rescheduleUrl)
    {
        var name = string.IsNullOrWhiteSpace(professionalFirstName) ? "seu profissional" : professionalFirstName.Trim();
        return $"Seu atendimento com {name} precisou ser cancelado por um imprevisto do profissional. " +
               "Você pode escolher um novo horário pelo link abaixo. " +
               "Este link ficará disponível por 48 horas. " +
               rescheduleUrl;
    }

    public static string FirstName(string value)
    {
        var first = value?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "profissional" : first.Length > 80 ? first[..80] : first;
    }
}
