namespace GestaoPredio.Application.Customers;

/// <summary>
/// Options for <see cref="IManualCheckInCodeHasher"/>. Binds the section
/// <c>CheckIn</c> — key <c>CheckIn:ManualCodeHmacKey</c> / env
/// <c>CheckIn__ManualCodeHmacKey</c>. The secret is never committed, logged or audited.
/// </summary>
public sealed class ManualCheckInCodeHashingOptions
{
    public const string SectionName = "CheckIn";

    public string ManualCodeHmacKey { get; set; } = "";
}
