-- Initial schema matching the EF Core model in
-- src/CleanArchitecture.Infrastructure/Persistence/Configurations.
-- This is the SQL source of truth — when a Domain entity or its
-- Configuration changes, add a new V{N}__*.sql migration AND keep
-- the C# side in sync. EF Core does not generate this for us.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE [dbo].[Products] (
    [Id]          UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_Products] PRIMARY KEY,
    [Name]        NVARCHAR(200)    NOT NULL,
    [Description] NVARCHAR(2000)   NOT NULL,
    [Price]       DECIMAL(18, 2)   NOT NULL,
    [Stock]       INT              NOT NULL,
    [CreatedAt]   DATETIME2        NOT NULL,
    [UpdatedAt]   DATETIME2        NULL
);

CREATE TABLE [dbo].[Orders] (
    [Id]           UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_Orders] PRIMARY KEY,
    [CustomerName] NVARCHAR(200)    NOT NULL,
    [Status]       INT              NOT NULL,
    [CreatedAt]    DATETIME2        NOT NULL,
    [UpdatedAt]    DATETIME2        NULL
);

CREATE TABLE [dbo].[OrderItems] (
    [Id]          UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_OrderItems] PRIMARY KEY,
    [OrderId]     UNIQUEIDENTIFIER NOT NULL,
    [ProductId]   UNIQUEIDENTIFIER NOT NULL,
    [ProductName] NVARCHAR(200)    NOT NULL,
    [UnitPrice]   DECIMAL(18, 2)   NOT NULL,
    [Quantity]    INT              NOT NULL,
    [CreatedAt]   DATETIME2        NOT NULL,
    [UpdatedAt]   DATETIME2        NULL,
    CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId])
        REFERENCES [dbo].[Orders] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_OrderItems_OrderId] ON [dbo].[OrderItems] ([OrderId]);

COMMIT TRANSACTION;
