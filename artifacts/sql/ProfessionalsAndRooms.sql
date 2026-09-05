BEGIN TRANSACTION;
ALTER TABLE [AuditEntries] ADD [ChangedFields] nvarchar(500) NULL;

ALTER TABLE [AuditEntries] ADD [TargetEntityId] uniqueidentifier NULL;

ALTER TABLE [AuditEntries] ADD [TargetEntityType] nvarchar(50) NULL;

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

CREATE INDEX [IX_AuditEntries_TargetEntity] ON [AuditEntries] ([TargetEntityType], [TargetEntityId], [OccurredAt]);

CREATE UNIQUE INDEX [UX_PrivateFiles_StorageKey] ON [PrivateFiles] ([StorageKey]);

CREATE UNIQUE INDEX [UX_Professionals_ApplicationUserId] ON [Professionals] ([ApplicationUserId]) WHERE [ApplicationUserId] IS NOT NULL;

CREATE UNIQUE INDEX [UX_Professionals_PhotoFileId] ON [Professionals] ([PhotoFileId]) WHERE [PhotoFileId] IS NOT NULL;

CREATE UNIQUE INDEX [UX_Rooms_NormalizedName] ON [Rooms] ([NormalizedName]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260905052933_ProfessionalsAndRooms', N'10.0.11');

COMMIT;
GO

