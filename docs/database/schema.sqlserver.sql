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
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    CREATE TABLE [OutboxMessages] (
        [Id] bigint NOT NULL IDENTITY,
        [Type] varchar(100) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [OccurredAt] datetimeoffset(3) NOT NULL,
        [ProcessedAt] datetimeoffset(3) NULL,
        [Attempts] int NOT NULL,
        [LastError] nvarchar(2000) NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [UserName] varchar(50) NOT NULL,
        [Email] varchar(254) NOT NULL,
        [HomePage] nvarchar(2048) NULL,
        [CreatedAt] datetimeoffset(3) NOT NULL,
        [LastSeenAt] datetimeoffset(3) NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    CREATE TABLE [Comments] (
        [Id] bigint NOT NULL IDENTITY,
        [ParentId] bigint NULL,
        [RootId] bigint NULL,
        [Depth] int NOT NULL,
        [UserId] int NOT NULL,
        [UserName] varchar(50) NOT NULL,
        [Email] varchar(254) NOT NULL,
        [HomePage] nvarchar(2048) NULL,
        [Html] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset(3) NOT NULL,
        [ReplyCount] int NOT NULL DEFAULT 0,
        [IpAddress] varchar(45) NOT NULL,
        [UserAgent] nvarchar(512) NULL,
        CONSTRAINT [PK_Comments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Comments_Tree] CHECK (([ParentId] IS NULL AND [RootId] IS NULL AND [Depth] = 0) OR ([ParentId] IS NOT NULL AND [RootId] IS NOT NULL AND [Depth] > 0)),
        CONSTRAINT [FK_Comments_Comments_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [Comments] ([Id]),
        CONSTRAINT [FK_Comments_Comments_RootId] FOREIGN KEY ([RootId]) REFERENCES [Comments] ([Id]),
        CONSTRAINT [FK_Comments_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    CREATE TABLE [Attachments] (
        [Id] uniqueidentifier NOT NULL,
        [CommentId] bigint NOT NULL,
        [FileName] nvarchar(255) NOT NULL,
        [StorageKey] varchar(100) NOT NULL,
        [ContentType] varchar(100) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Type] varchar(10) NOT NULL,
        [Width] int NULL,
        [Height] int NULL,
        [WasResized] bit NULL,
        CONSTRAINT [PK_Attachments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Attachments_Comments_CommentId] FOREIGN KEY ([CommentId]) REFERENCES [Comments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Attachments_CommentId] ON [Attachments] ([CommentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Attachments_StorageKey] ON [Attachments] ([StorageKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Comments_ParentId] ON [Comments] ([ParentId]) WHERE [ParentId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Comments_RootId_Id] ON [Comments] ([RootId], [Id]) WHERE [RootId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Comments_TopLevel_CreatedAt] ON [Comments] ([CreatedAt], [Id]) WHERE [ParentId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Comments_TopLevel_Email] ON [Comments] ([Email], [Id]) WHERE [ParentId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Comments_TopLevel_UserName] ON [Comments] ([UserName], [Id]) WHERE [ParentId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    CREATE INDEX [IX_Comments_UserId] ON [Comments] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_OutboxMessages_Pending] ON [OutboxMessages] ([Id]) WHERE [ProcessedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109164358_Initial'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109164358_Initial', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109174339_IncludeFilterColumnsInFilteredIndexes'
)
BEGIN
    DROP INDEX [IX_OutboxMessages_Pending] ON [OutboxMessages];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109174339_IncludeFilterColumnsInFilteredIndexes'
)
BEGIN
    DROP INDEX [IX_Comments_TopLevel_CreatedAt] ON [Comments];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109174339_IncludeFilterColumnsInFilteredIndexes'
)
BEGIN
    DROP INDEX [IX_Comments_TopLevel_Email] ON [Comments];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109174339_IncludeFilterColumnsInFilteredIndexes'
)
BEGIN
    DROP INDEX [IX_Comments_TopLevel_UserName] ON [Comments];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109174339_IncludeFilterColumnsInFilteredIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_OutboxMessages_Pending] ON [OutboxMessages] ([Id]) INCLUDE ([ProcessedAt]) WHERE [ProcessedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109174339_IncludeFilterColumnsInFilteredIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Comments_TopLevel_CreatedAt] ON [Comments] ([CreatedAt], [Id]) INCLUDE ([ParentId]) WHERE [ParentId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109174339_IncludeFilterColumnsInFilteredIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Comments_TopLevel_Email] ON [Comments] ([Email], [Id]) INCLUDE ([ParentId]) WHERE [ParentId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109174339_IncludeFilterColumnsInFilteredIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Comments_TopLevel_UserName] ON [Comments] ([UserName], [Id]) INCLUDE ([ParentId]) WHERE [ParentId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109174339_IncludeFilterColumnsInFilteredIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109174339_IncludeFilterColumnsInFilteredIndexes', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109214845_OutboxRetriesAndCleanup'
)
BEGIN
    DROP INDEX [IX_OutboxMessages_Pending] ON [OutboxMessages];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109214845_OutboxRetriesAndCleanup'
)
BEGIN
    ALTER TABLE [OutboxMessages] ADD [NextAttemptAt] datetimeoffset(3) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109214845_OutboxRetriesAndCleanup'
)
BEGIN
    ALTER TABLE [OutboxMessages] ADD [ParkedAt] datetimeoffset(3) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109214845_OutboxRetriesAndCleanup'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_OutboxMessages_Pending] ON [OutboxMessages] ([Id]) INCLUDE ([ProcessedAt], [ParkedAt], [NextAttemptAt]) WHERE [ProcessedAt] IS NULL AND [ParkedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109214845_OutboxRetriesAndCleanup'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_OutboxMessages_Processed] ON [OutboxMessages] ([ProcessedAt]) WHERE [ProcessedAt] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261109214845_OutboxRetriesAndCleanup'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261109214845_OutboxRetriesAndCleanup', N'10.0.14');
END;

COMMIT;
GO

