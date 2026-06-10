using System.Collections.Generic;
using System.Linq;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Enums;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.ValueObjects;

namespace CleanArchitecture.Domain.Entities
{
    public class Order : BaseEntity
    {
        private readonly List<OrderItem> _items = new List<OrderItem>();

        public string CustomerName { get; private set; } = string.Empty;
        public OrderStatus Status { get; private set; } = OrderStatus.Pending;

        // AsReadOnly prevents callers from casting back to List<OrderItem>
        // and bypassing the AddItem invariants.
        public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

        public Money TotalAmount => _items.Aggregate(Money.Zero, (sum, i) => sum + i.LineTotal);

        private Order() { }

        public Order(string customerName, IEnumerable<OrderItem> items)
        {
            if (string.IsNullOrWhiteSpace(customerName))
                throw new DomainException("Order customer name must not be empty.");
            if (customerName.Length > 200)
                throw new DomainException("Order customer name must be at most 200 characters.");
            if (items == null)
                throw new DomainException("Order items collection must not be null.");

            CustomerName = customerName;
            Status = OrderStatus.Pending;

            foreach (var item in items)
            {
                AddItem(item);
            }

            if (_items.Count == 0)
                throw new DomainException("Order must have at least one item.");
        }

        public void AddItem(OrderItem item)
        {
            if (item == null)
                throw new DomainException("Order item must not be null.");
            if (Status != OrderStatus.Pending)
                throw new DomainException("Cannot modify a non-pending order.");

            item.AttachTo(Id);
            _items.Add(item);
        }

        public void Cancel()
        {
            if (Status == OrderStatus.Cancelled)
                throw new DomainException("Order is already cancelled.");
            Status = OrderStatus.Cancelled;
        }

        public void Confirm()
        {
            if (Status != OrderStatus.Pending)
                throw new DomainException("Only pending orders can be confirmed.");
            Status = OrderStatus.Confirmed;
        }
    }
}
