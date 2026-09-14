using GestaoPredio.Domain.Professionals;

namespace recepcaototem.Features.Rooms;

public static class WhatsappLinkBuilder
{
    public static bool TryBuild(string configuredPhone, string message, out string url)
    {
        url = string.Empty;
        if (!WhatsAppNormalizer.TryNormalize(configuredPhone, out var normalized)) return false;

        url = $"https://wa.me/{normalized[1..]}?text={Uri.EscapeDataString(message)}";
        return true;
    }
}
