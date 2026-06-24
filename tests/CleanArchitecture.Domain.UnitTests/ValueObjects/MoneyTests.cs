using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Domain.UnitTests.ValueObjects
{
    public class MoneyTests
    {
        [Fact]
        public void Constructor_WithNonNegativeAmount_StoresAmount()
        {
            var money = new Money(12.34m);

            Assert.Equal(12.34m, money.Amount);
        }

        [Fact]
        public void Constructor_WithNegativeAmount_Throws()
        {
            var ex = Assert.Throws<DomainException>(() => new Money(-0.01m));

            Assert.Contains("must not be negative", ex.Message);
        }

        [Fact]
        public void Zero_HasAmountZero()
        {
            Assert.Equal(0m, Money.Zero.Amount);
        }

        [Fact]
        public void Equality_IsValueBased()
        {
            var a = new Money(100m);
            var b = new Money(100m);

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equality_DistinguishesAmounts()
        {
            Assert.NotEqual(new Money(10m), new Money(20m));
        }

        [Fact]
        public void Addition_SumsAmounts()
        {
            var sum = new Money(10m) + new Money(2.5m);

            Assert.Equal(new Money(12.5m), sum);
        }

        [Fact]
        public void Multiplication_MoneyTimesInt_ScalesAmount()
        {
            Assert.Equal(new Money(50m), new Money(12.5m) * 4);
        }

        [Fact]
        public void Multiplication_ByZero_YieldsZero()
        {
            Assert.Equal(Money.Zero, new Money(99m) * 0);
        }

        [Fact]
        public void Multiplication_ByNegativeInt_Throws()
        {
            Assert.Throws<DomainException>(() => new Money(10m) * -1);
        }
    }
}
