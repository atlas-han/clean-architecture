using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Exceptions;

namespace CleanArchitecture.Domain.Entities
{
    public class Product : BaseEntity
    {
        public string Name { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public decimal Price { get; private set; }
        public int Stock { get; private set; }

        private Product() { }

        public Product(string name, string description, decimal price, int stock)
        {
            Rename(name);
            ChangeDescription(description);
            ChangePrice(price);
            AdjustStock(stock);
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

        public void ChangePrice(decimal price)
        {
            if (price < 0)
                throw new DomainException("Price must not be negative.");
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
