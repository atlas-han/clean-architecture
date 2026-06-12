using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Events;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.ValueObjects;

namespace CleanArchitecture.Domain.Entities
{
    public class Product : BaseEntity
    {
        public string Name { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public Money Price { get; private set; } = Money.Zero;
        public int Stock { get; private set; }

        private Product() { }

        public Product(string name, string description, Money price, int stock)
        {
            Rename(name);
            ChangeDescription(description);
            ChangePrice(price);
            AdjustStock(stock);

            RaiseDomainEvent(new ProductRegisteredDomainEvent(Id, Name, Description, Price.Amount, Stock));
        }

        public void Rename(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Product name must not be empty.");
            if (name.Length > 200)
                throw new DomainException("Product name must be at most 200 characters.");
            Name = name;
        }

        public void ChangeDescription(string description)
        {
            Description = description ?? string.Empty;
        }

        public void ChangePrice(Money price)
        {
            if (price is null)
                throw new DomainException("Price must not be null.");
            Price = price;
        }

        public void AdjustStock(int stock)
        {
            if (stock < 0)
                throw new DomainException("Stock must not be negative.");
            Stock = stock;
        }

        public void DecreaseStock(int quantity)
        {
            if (quantity <= 0)
                throw new DomainException("Decrease quantity must be positive.");
            if (quantity > Stock)
                throw new DomainException("Insufficient stock.");
            Stock -= quantity;
        }
    }
}
