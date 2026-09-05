IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(128) NOT NULL,
        [ProviderKey] nvarchar(128) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(128) NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'00000000000000_CreateIdentitySchema', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904174759_InfrastructureFoundation'
)
BEGIN
    CREATE TABLE [AuditEntries] (
        [Id] uniqueidentifier NOT NULL,
        [ActorUserId] nvarchar(450) NULL,
        [Action] nvarchar(100) NOT NULL,
        [Result] nvarchar(50) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [CorrelationId] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_AuditEntries] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904174759_InfrastructureFoundation'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_OccurredAt] ON [AuditEntries] ([OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904174759_InfrastructureFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260904174759_InfrastructureFoundation', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904235115_AuthenticationAndProvisioning'
)
BEGIN
    ALTER TABLE [AuditEntries] ADD [IpAddress] nvarchar(45) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904235115_AuthenticationAndProvisioning'
)
BEGIN
    ALTER TABLE [AuditEntries] ADD [TargetUserId] nvarchar(450) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904235115_AuthenticationAndProvisioning'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [DisplayName] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904235115_AuthenticationAndProvisioning'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904235115_AuthenticationAndProvisioning'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904235115_AuthenticationAndProvisioning'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_Action_OccurredAt] ON [AuditEntries] ([Action], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904235115_AuthenticationAndProvisioning'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260904235115_AuthenticationAndProvisioning', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    ALTER TABLE [AuditEntries] ADD [ChangedFields] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    ALTER TABLE [AuditEntries] ADD [TargetEntityId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    ALTER TABLE [AuditEntries] ADD [TargetEntityType] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    CREATE TABLE [PrivateFiles] (
        [Id] uniqueidentifier NOT NULL,
        [StorageKey] varchar(64) NOT NULL,
        [MimeType] varchar(20) NOT NULL,
        [Length] bigint NOT NULL,
        [Purpose] varchar(50) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PrivateFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PrivateFiles_Length_Positive] CHECK ([Length] > 0),
        CONSTRAINT [CK_PrivateFiles_Purpose] CHECK ([Purpose] = 'PROFESSIONAL_PHOTO')
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    CREATE TABLE [Rooms] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [NormalizedName] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [HourlyRate] decimal(18,2) NOT NULL,
        [DailyRate] decimal(18,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Rooms] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Rooms_DailyRate_NonNegative] CHECK ([DailyRate] >= 0),
        CONSTRAINT [CK_Rooms_HourlyRate_NonNegative] CHECK ([HourlyRate] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    CREATE TABLE [Professionals] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [NormalizedName] nvarchar(400) NOT NULL,
        [Profession] nvarchar(150) NOT NULL,
        [NormalizedProfession] nvarchar(300) NOT NULL,
        [WhatsApp] varchar(16) NOT NULL,
        [PhotoFileId] uniqueidentifier NULL,
        [ApplicationUserId] nvarchar(450) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Professionals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Professionals_AspNetUsers_ApplicationUserId] FOREIGN KEY ([ApplicationUserId]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_Professionals_PrivateFiles_PhotoFileId] FOREIGN KEY ([PhotoFileId]) REFERENCES [PrivateFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_TargetEntity] ON [AuditEntries] ([TargetEntityType], [TargetEntityId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    CREATE UNIQUE INDEX [UX_PrivateFiles_StorageKey] ON [PrivateFiles] ([StorageKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Professionals_ApplicationUserId] ON [Professionals] ([ApplicationUserId]) WHERE [ApplicationUserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Professionals_PhotoFileId] ON [Professionals] ([PhotoFileId]) WHERE [PhotoFileId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Rooms_NormalizedName] ON [Rooms] ([NormalizedName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905052933_ProfessionalsAndRooms'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260905052933_ProfessionalsAndRooms', N'10.0.11');
END;

COMMIT;
GO

