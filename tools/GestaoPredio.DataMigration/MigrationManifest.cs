namespace GestaoPredio.DataMigration;

public sealed record MigrationTable(string Name, IReadOnlyList<string> Columns, IReadOnlyList<string> KeyColumns,
    bool HasGeneratedIntegerKey = false);

public static class MigrationManifest
{
    public static IReadOnlyList<MigrationTable> Tables { get; } =
    [
        Table("AspNetRoles", "Id", "Name", "NormalizedName", "ConcurrencyStamp"),
        Table("AspNetUsers", "Id", "DisplayName", "IsActive", "MustChangePassword", "UserName",
            "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed", "PasswordHash", "SecurityStamp",
            "ConcurrencyStamp", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnd",
            "LockoutEnabled", "AccessFailedCount"),
        TableWithGeneratedKey("AspNetRoleClaims", "Id", "RoleId", "ClaimType", "ClaimValue"),
        TableWithGeneratedKey("AspNetUserClaims", "Id", "UserId", "ClaimType", "ClaimValue"),
        CompositeTable("AspNetUserLogins", ["LoginProvider", "ProviderKey"],
            "LoginProvider", "ProviderKey", "ProviderDisplayName", "UserId"),
        CompositeTable("AspNetUserRoles", ["UserId", "RoleId"], "UserId", "RoleId"),
        CompositeTable("AspNetUserTokens", ["UserId", "LoginProvider", "Name"],
            "UserId", "LoginProvider", "Name", "Value"),
        Table("AuditEntries", "Id", "ActorUserId", "TargetUserId", "IpAddress", "Action", "Result",
            "OccurredAt", "CorrelationId", "TargetEntityType", "TargetEntityId", "ChangedFields"),
        Table("PrivateFiles", "Id", "StorageKey", "MimeType", "Length", "Purpose", "CreatedAt"),
        Table("Rooms", "Id", "Name", "NormalizedName", "Description", "HourlyRate", "DailyRate", "IsActive",
            "CreatedAt", "UpdatedAt"),
        Table("Professionals", "Id", "Name", "NormalizedName", "Profession", "NormalizedProfession", "WhatsApp",
            "PhotoFileId", "ApplicationUserId", "IsActive", "CreatedAt", "UpdatedAt")
    ];

    private static MigrationTable Table(string name, params string[] columns) => new(name, columns, [columns[0]]);
    private static MigrationTable TableWithGeneratedKey(string name, params string[] columns) =>
        new(name, columns, [columns[0]], true);
    private static MigrationTable CompositeTable(string name, string[] keys, params string[] columns) =>
        new(name, columns, keys);
}
