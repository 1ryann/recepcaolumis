using GestaoPredio.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

/// <summary>The stored (and API) spelling of the opt-in values.</summary>
public static class WhatsAppOptInStorage
{
    public static string Status(WhatsAppOptInStatus status) => WhatsAppOptInMapping.StatusStorage[status];
    public static string Source(WhatsAppOptInSource source) => WhatsAppOptInMapping.SourceStorage[source];
}

/// <summary>
/// The four WhatsApp opt-in columns shared by Customers and Professionals (see docs/operations/whatsapp-consent.md).
/// Existing rows get NOT_RECORDED: no opt-in is ever assumed.
/// </summary>
internal static class WhatsAppOptInMapping
{
    public static readonly IReadOnlyDictionary<WhatsAppOptInStatus, string> StatusStorage =
        new Dictionary<WhatsAppOptInStatus, string>
        {
            [WhatsAppOptInStatus.NotRecorded] = "NOT_RECORDED",
            [WhatsAppOptInStatus.Granted] = "GRANTED",
            [WhatsAppOptInStatus.Revoked] = "REVOKED"
        };

    public static readonly IReadOnlyDictionary<WhatsAppOptInSource, string> SourceStorage =
        new Dictionary<WhatsAppOptInSource, string>
        {
            [WhatsAppOptInSource.CustomerRegistration] = "CUSTOMER_REGISTRATION",
            [WhatsAppOptInSource.CustomerPortal] = "CUSTOMER_PORTAL",
            [WhatsAppOptInSource.Totem] = "TOTEM",
            [WhatsAppOptInSource.Reception] = "RECEPTION",
            [WhatsAppOptInSource.ProfessionalPortal] = "PROFESSIONAL_PORTAL"
        };

    public static void MapWhatsAppOptIn<TEntity>(this EntityTypeBuilder<TEntity> entity, string table) where TEntity : class
    {
        entity.ToTable(table, builder =>
        {
            builder.HasCheckConstraint($"CK_{table}_WhatsAppOptInStatus",
                $"\"WhatsAppOptInStatus\" IN ({Quoted(StatusStorage.Values)})");
            builder.HasCheckConstraint($"CK_{table}_WhatsAppOptInSource",
                $"\"WhatsAppOptInSource\" IS NULL OR \"WhatsAppOptInSource\" IN ({Quoted(SourceStorage.Values)})");
            // A decision always says when and where it was captured; a grant also says which wording was shown.
            builder.HasCheckConstraint($"CK_{table}_WhatsAppOptInDecision",
                "\"WhatsAppOptInStatus\" = 'NOT_RECORDED' OR " +
                "(\"WhatsAppOptInChangedAt\" IS NOT NULL AND \"WhatsAppOptInSource\" IS NOT NULL AND " +
                "(\"WhatsAppOptInStatus\" <> 'GRANTED' OR \"WhatsAppOptInTextVersion\" IS NOT NULL))");
        });
        entity.Property<WhatsAppOptInStatus>("WhatsAppOptInStatus").HasConversion(Converter(StatusStorage))
            .HasMaxLength(16).IsRequired();
        entity.Property<DateTimeOffset?>("WhatsAppOptInChangedAt").HasColumnType("timestamp with time zone");
        entity.Property<WhatsAppOptInSource?>("WhatsAppOptInSource").HasConversion(Converter(SourceStorage))
            .HasMaxLength(32);
        entity.Property<string?>("WhatsAppOptInTextVersion").HasMaxLength(WhatsAppOptInState.TextVersionMaxLength);
    }

    private static ValueConverter<TEnum, string> Converter<TEnum>(IReadOnlyDictionary<TEnum, string> storage)
        where TEnum : struct, Enum
    {
        var reverse = storage.ToDictionary(x => x.Value, x => x.Key);
        return new ValueConverter<TEnum, string>(value => storage[value], value => reverse[value]);
    }

    private static string Quoted(IEnumerable<string> values) => string.Join(", ", values.Select(x => $"'{x}'"));
}
