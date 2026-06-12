using CleanArchitecture.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Persistence.Configurations
{
    public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("OutboxMessages");

            builder.HasKey(m => m.Id);

            builder.Property(m => m.AggregateId)
                .IsRequired();

            builder.Property(m => m.Type)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(m => m.Content)
                .IsRequired();

            builder.Property(m => m.OccurredOnUtc)
                .IsRequired();

            // The worker's poll path is "oldest pending first". A filtered index over just the
            // pending rows keeps that query cheap and small no matter how large the processed
            // history grows, so the outbox doesn't drag on the write path. The filter matches the
            // worker's drain predicate exactly — unpublished AND not dead-lettered — so quarantined
            // poison messages drop out of the index too. HasFilter is SQL Server syntax; the
            // InMemory provider (dev/test) ignores it and simply omits the filter.
            builder.HasIndex(m => m.OccurredOnUtc)
                .HasFilter("[ProcessedOnUtc] IS NULL AND [DeadLetteredOnUtc] IS NULL")
                .HasDatabaseName("IX_OutboxMessages_Unprocessed");
        }
    }
}
