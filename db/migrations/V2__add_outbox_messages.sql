-- Adds the transactional outbox table behind Order/Product → Kafka publishing.
-- ConvertDomainEventsToOutboxInterceptor writes a row here in the SAME transaction
-- as the order/product that raised the event (no partial success); the
-- OutboxProducerWorker later drains unprocessed rows to Kafka.
-- Mirrors OutboxMessageConfiguration in
-- src/CleanArchitecture.Infrastructure/Persistence/Configurations.
-- SQL-first: keep this in sync with that Configuration (EF Core generates no DDL).

SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE [dbo].[OutboxMessages] (
    [Id]             UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_OutboxMessages] PRIMARY KEY,
    [AggregateId]    UNIQUEIDENTIFIER NOT NULL,
    [Type]           NVARCHAR(200)    NOT NULL,
    [Content]        NVARCHAR(MAX)    NOT NULL,
    [OccurredOnUtc]  DATETIME2        NOT NULL,
    [ProcessedOnUtc] DATETIME2        NULL,
    [Error]          NVARCHAR(MAX)    NULL
);

-- Worker poll path: oldest unprocessed first. Filtered to the pending rows only, so the
-- index stays small and the scan cheap no matter how large the processed history grows.
CREATE INDEX [IX_OutboxMessages_Unprocessed]
    ON [dbo].[OutboxMessages] ([OccurredOnUtc])
    WHERE [ProcessedOnUtc] IS NULL;

COMMIT TRANSACTION;
