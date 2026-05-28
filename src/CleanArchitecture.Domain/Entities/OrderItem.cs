using System;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.ValueObjects;

namespace CleanArchitecture.Domain.Entities
{
    public class OrderItem : BaseEntity
    {
        public Guid OrderId { get; private set; }
        public Guid ProductId { get; private set; }
        public string ProductName { get; private set; } = string.Empty;
        public Money UnitPrice { get; private set; } = Money.Zero;
        public int Quantity { get; private set; }

        public Money LineTotal => UnitPrice * Quantity;

        private OrderItem() { }

        public OrderItem(Guid productId, string productName, Money unitPrice, int quantity)
        {
            if (productId == Guid.Empty)
                throw new DomainException("OrderItem productId must not be empty.");
            if (string.IsNullOrWhiteSpace(productName))
                throw new DomainException("OrderItem productName must not be empty.");
            if (unitPrice is null)
                throw new DomainException("OrderItem unitPrice must not be null.");
            if (quantity <= 0)
                throw new DomainException("OrderItem quantity must be positive.");

            ProductId = productId;
            ProductName = productName;
            UnitPrice = unitPrice;
            Quantity = quantity;
        }

        internal void AttachTo(Guid orderId)
        {
            OrderId = orderId;
        }
    }
}
