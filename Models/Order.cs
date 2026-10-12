using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace LogiTrack.Models;

public class Order
{
    [Key]
    public int OrderId { get; set; }
    [Required, MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;
    public DateTime DatePlaced { get; set; } = DateTime.UtcNow;
    public List<InventoryItem> Items { get; set; } = [];

    public void AddItem(InventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if ((item.Order is not null && !ReferenceEquals(item.Order, this)) ||
            (item.OrderId.HasValue && item.OrderId != OrderId))
            throw new InvalidOperationException("The item already belongs to another order.");
        // Unsaved items share ID zero; compare their references instead.
        if (Items.Any(existing => ReferenceEquals(existing, item) ||
            (item.ItemId != 0 && existing.ItemId == item.ItemId))) return;
        Items.Add(item);
        item.Order = this;
        item.OrderId = OrderId == 0 ? null : OrderId;
    }

    public bool RemoveItem(int itemId)
    {
        if (itemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(itemId), "Save the item before removing it by ID.");
        var item = Items.Find(item => item.ItemId == itemId);
        if (item is null) return false;
        Items.Remove(item);
        item.Order = null;
        item.OrderId = null;
        return true;
    }

    // Load Items before summarizing an order retrieved from EF Core.
    public string GetOrderSummary() => FormatSummary(OrderId, CustomerName, Items.Count, DatePlaced);
    public static string FormatSummary(int id, string customer, int count, DateTime placed) =>
        $"Order #{id} for {customer} | Items: {count} | Placed: {placed.ToString("M/d/yyyy", CultureInfo.InvariantCulture)}";
}
