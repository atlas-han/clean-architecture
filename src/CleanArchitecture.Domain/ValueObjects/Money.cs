using CleanArchitecture.Domain.Exceptions;

namespace CleanArchitecture.Domain.ValueObjects
{
    public sealed record Money
    {
        public decimal Amount { get; }

        public Money(decimal amount)
        {
            if (amount < 0)
                throw new DomainException("Money amount must not be negative.");
            Amount = amount;
        }

        public static Money Zero { get; } = new Money(0m);

        public static Money operator +(Money left, Money right) =>
            new Money(left.Amount + right.Amount);

        public static Money operator *(Money money, int multiplier) =>
            new Money(money.Amount * multiplier);
    }
}
