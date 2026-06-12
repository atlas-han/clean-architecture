-- Adds poison-message handling to the transactional outbox: a per-message attempt counter and a
-- dead-letter timestamp. OutboxProducerWorker increments [Attempts] on each failed publish and,
-- once it reaches Outbox:MaxRetries, stamps [DeadLetteredOnUtc] to quarantine the row so it is no
-- longer retried (the row stays in the table, with its [Error] + [Attempts], as a dead-letter
-- record for inspection / manual replay).
-- Mirrors OutboxMessage / OutboxMessageConfiguration in
-- src/CleanArchitecture.Infrastructure. SQL-first: keep this in sync (EF Core generates no DDL).

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE [dbo].[OutboxMessages]
    ADD [Attempts]          INT       NOT NULL CONSTRAINT [DF_OutboxMessages_Attempts] DEFAULT 0,
        [DeadLetteredOnUtc] DATETIME2 NULL;

-- Realign the pending-rows filtered index with the worker's drain predicate, which now also
-- excludes dead-lettered rows, so quarantined poison messages drop out of the index entirely. The
-- CREATE is wrapped in EXEC so the newly added [DeadLetteredOnUtc] column resolves at execution
-- time (it is not visible to the parser earlier in this same batch).
DROP INDEX [IX_OutboxMessages_Unprocessed] ON [dbo].[OutboxMessages];

EXEC('CREATE INDEX [IX_OutboxMessages_Unprocessed]
    ON [dbo].[OutboxMessages] ([OccurredOnUtc])
    WHERE [ProcessedOnUtc] IS NULL AND [DeadLetteredOnUtc] IS NULL;');

COMMIT TRANSACTION;
