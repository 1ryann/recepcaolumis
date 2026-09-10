namespace recepcaototem.Features.Totem;

/// <summary>
/// Pure mapping of the two real signals — an in-service visit and effective
/// physical presence (see <c>PresenceEvaluator</c>) — to the Totem's 3 states.
/// IN_SERVICE has precedence. No room-availability check here: the carousel is
/// about "working today, worth booking", not "can be seen right now".
/// </summary>
public static class TotemProfessionalStatus
{
    public static string Resolve(bool inService, bool effectivePresence) =>
        inService ? "IN_SERVICE"
        : effectivePresence ? "AVAILABLE"
        : "UNAVAILABLE";
}
