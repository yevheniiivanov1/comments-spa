-- Comments — database schema for MySQL Workbench.
--
-- The application runs on SQL Server (EF Core migrations, see schema.sqlserver.sql for the exact DDL).
-- This file is the same model in MySQL syntax so it can be opened in MySQL Workbench:
--   File → Open SQL Script…                                         — read it as a script;
--   File → Import → Reverse Engineer MySQL Create Script… → Execute  — get the EER diagram (tables, keys, relations).
--
-- Differences forced by MySQL, marked "SQL Server:" below: SQL Server uses filtered indexes (WHERE ParentId IS NULL),
-- datetimeoffset and uniqueidentifier; MySQL gets plain indexes, DATETIME(3) in UTC and BINARY(16).

CREATE SCHEMA IF NOT EXISTS `comments` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE `comments`;

-- A person who posts comments, identified by e-mail (case-insensitive unique).
-- Name and home page follow the latest comment; each comment keeps its own copy as posted.
CREATE TABLE `Users` (
  `Id`         INT           NOT NULL AUTO_INCREMENT,
  `UserName`   VARCHAR(50)   NOT NULL COMMENT 'Latin letters and digits',
  `Email`      VARCHAR(254)  NOT NULL,
  `HomePage`   VARCHAR(2048) NULL     COMMENT 'Absolute http(s) URL',
  `CreatedAt`  DATETIME(3)   NOT NULL COMMENT 'UTC; SQL Server: datetimeoffset(3)',
  `LastSeenAt` DATETIME(3)   NOT NULL COMMENT 'UTC',
  PRIMARY KEY (`Id`),
  UNIQUE INDEX `IX_Users_Email` (`Email`)
) ENGINE = InnoDB COMMENT = 'Clients who post comments';

-- A comment or a reply. ParentId is the comment answered, RootId the top-level comment of the thread
-- (both NULL for a top-level comment), Depth the nesting level. Author fields are a snapshot so the
-- top-level list sorts by them without a join.
CREATE TABLE `Comments` (
  `Id`         BIGINT        NOT NULL AUTO_INCREMENT,
  `ParentId`   BIGINT        NULL     COMMENT 'Comment this one replies to',
  `RootId`     BIGINT        NULL     COMMENT 'Top-level comment of the thread',
  `Depth`      INT           NOT NULL COMMENT '0 for top-level comments',
  `UserId`     INT           NOT NULL,
  `UserName`   VARCHAR(50)   NOT NULL COMMENT 'As posted',
  `Email`      VARCHAR(254)  NOT NULL COMMENT 'As posted',
  `HomePage`   VARCHAR(2048) NULL     COMMENT 'As posted',
  `Html`       MEDIUMTEXT    NOT NULL COMMENT 'Validated XHTML: only a, code, i, strong',
  `CreatedAt`  DATETIME(3)   NOT NULL COMMENT 'UTC; SQL Server: datetimeoffset(3)',
  `ReplyCount` INT           NOT NULL DEFAULT 0 COMMENT 'Replies in the whole thread (top-level comments only)',
  `IpAddress`  VARCHAR(45)   NOT NULL COMMENT 'Client IP (behind the reverse proxy: X-Forwarded-For)',
  `UserAgent`  VARCHAR(512)  NULL,
  PRIMARY KEY (`Id`),
  -- SQL Server: the next three are filtered indexes WHERE ParentId IS NULL (top-level comments only), one per sort.
  INDEX `IX_Comments_TopLevel_CreatedAt` (`CreatedAt`, `Id`),
  INDEX `IX_Comments_TopLevel_UserName` (`UserName`, `Id`),
  INDEX `IX_Comments_TopLevel_Email` (`Email`, `Id`),
  -- SQL Server: filtered WHERE RootId IS NOT NULL. Serves the thread read: WHERE RootId = ? AND Id > ? ORDER BY Id.
  INDEX `IX_Comments_RootId_Id` (`RootId`, `Id`),
  INDEX `IX_Comments_ParentId` (`ParentId`),
  INDEX `IX_Comments_UserId` (`UserId`),
  CONSTRAINT `CK_Comments_Tree` CHECK (
    (`ParentId` IS NULL AND `RootId` IS NULL AND `Depth` = 0) OR
    (`ParentId` IS NOT NULL AND `RootId` IS NOT NULL AND `Depth` > 0)),
  CONSTRAINT `FK_Comments_Comments_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `Comments` (`Id`),
  CONSTRAINT `FK_Comments_Comments_RootId` FOREIGN KEY (`RootId`) REFERENCES `Comments` (`Id`),
  CONSTRAINT `FK_Comments_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`)
) ENGINE = InnoDB COMMENT = 'Comments and replies (a tree of any depth)';

-- A file attached to a comment (at most one). One table for both kinds (table-per-hierarchy): Type is the
-- discriminator, Width/Height/WasResized apply to images only. The bytes are in blob storage under StorageKey.
CREATE TABLE `Attachments` (
  `Id`          BINARY(16)   NOT NULL COMMENT 'UUID v7; SQL Server: uniqueidentifier',
  `CommentId`   BIGINT       NOT NULL,
  `Type`        VARCHAR(10)  NOT NULL COMMENT 'image | text',
  `FileName`    VARCHAR(255) NOT NULL COMMENT 'Original name, display only',
  `StorageKey`  VARCHAR(100) NOT NULL COMMENT 'images/{guid}.png, texts/{guid}.txt',
  `ContentType` VARCHAR(100) NOT NULL,
  `SizeBytes`   BIGINT       NOT NULL,
  `Width`       INT          NULL     COMMENT 'Image: at most 320',
  `Height`      INT          NULL     COMMENT 'Image: at most 240',
  `WasResized`  TINYINT(1)   NULL     COMMENT 'Image: scaled down on upload',
  PRIMARY KEY (`Id`),
  UNIQUE INDEX `IX_Attachments_CommentId` (`CommentId`),
  UNIQUE INDEX `IX_Attachments_StorageKey` (`StorageKey`),
  CONSTRAINT `FK_Attachments_Comments_CommentId` FOREIGN KEY (`CommentId`) REFERENCES `Comments` (`Id`) ON DELETE CASCADE
) ENGINE = InnoDB COMMENT = 'Images (JPG, GIF, PNG up to 320x240) and text files (TXT up to 100 KB)';

-- Transactional outbox: integration events written in the same transaction as the comment and published to
-- RabbitMQ by the worker; ProcessedAt is set after the broker confirms. Published rows are deleted after 7 days.
CREATE TABLE `OutboxMessages` (
  `Id`          BIGINT        NOT NULL AUTO_INCREMENT,
  `Type`        VARCHAR(100)  NOT NULL COMMENT 'Routing key, e.g. comment.posted',
  `Payload`     LONGTEXT      NOT NULL COMMENT 'JSON',
  `OccurredAt`  DATETIME(3)   NOT NULL COMMENT 'UTC',
  `ProcessedAt` DATETIME(3)   NULL     COMMENT 'Set once published',
  `Attempts`    INT           NOT NULL,
  `LastError`   VARCHAR(2000) NULL,
  `NextAttemptAt` DATETIME(3) NULL     COMMENT 'Not before this time; set after a failed attempt (backoff)',
  `ParkedAt`    DATETIME(3)   NULL     COMMENT 'Set after the last attempt; released by the worker''s replay command',
  PRIMARY KEY (`Id`),
  -- SQL Server: filtered WHERE ProcessedAt IS NULL AND ParkedAt IS NULL, so the index holds only messages to publish.
  INDEX `IX_OutboxMessages_Pending` (`ProcessedAt`, `ParkedAt`, `Id`),
  -- SQL Server: filtered WHERE ProcessedAt IS NOT NULL; serves deleting published messages after the retention period.
  INDEX `IX_OutboxMessages_Processed` (`ProcessedAt`)
) ENGINE = InnoDB COMMENT = 'Events waiting to be published (transactional outbox)';
