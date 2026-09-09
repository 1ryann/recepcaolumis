namespace GestaoPredio.IntegrationTests;

/// <summary>
/// Test clock for <see cref="ModulesApiFactory"/>. Defaults to the system clock so every
/// test behaves exactly as it did before; a test can pin it to a fixed instant to remove
/// wall-clock dependence (e.g. a booking window that could otherwise straddle civil midnight).
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset? _fixedUtcNow;

    public bool IsFrozen => _fixedUtcNow is not null;

    public void Freeze(DateTimeOffset value) => _fixedUtcNow = value.ToUniversalTime();

    public void UseSystemClock() => _fixedUtcNow = null;

    public override DateTimeOffset GetUtcNow() => _fixedUtcNow ?? DateTimeOffset.UtcNow;
}
