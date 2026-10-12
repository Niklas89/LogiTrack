using LogiTrack.Models;
using Microsoft.EntityFrameworkCore;

namespace LogiTrack.Data;

public static class ActivityDemo
{
    public static async Task RunAsync(LogiTrackContext context)
    {
        var sample = new Order { OrderId = 1001, CustomerName = "Samir", DatePlaced = new DateTime(2025, 4, 5) };
        var pallet = new InventoryItem { ItemId = 1, Name = "Pallet Jack", Quantity = 12, Location = "Warehouse A" };
        var scanner = new InventoryItem { ItemId = 2, Name = "Barcode Scanner", Quantity = 5, Location = "Warehouse B" };
        sample.AddItem(pallet);
        sample.AddItem(scanner);
        sample.AddItem(pallet);
        Verify(sample.Items.Count == 2, "Duplicate items are ignored");
        pallet.DisplayInfo();
        Console.WriteLine(sample.GetOrderSummary());
        Verify(sample.RemoveItem(2) && scanner.Order is null, "Removal clears the relationship");
        Verify(!sample.RemoveItem(999), "Missing item removal returns false");
        Console.WriteLine(sample.GetOrderSummary());

        if (!await context.InventoryItems.AnyAsync())
        {
            context.InventoryItems.Add(new InventoryItem { Name = "Pallet Jack", Quantity = 12, Location = "Warehouse A" });
            await context.SaveChangesAsync();
        }
        Console.WriteLine("Inventory read from SQL Server:");
        foreach (var item in await context.InventoryItems.AsNoTracking().OrderBy(item => item.ItemId).ToListAsync())
            item.DisplayInfo();

        // Roll back test changes so repeated runs leave no extra orders or inventory.
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var order = new Order { CustomerName = "Samir", DatePlaced = new DateTime(2025, 4, 5) };
            var item = new InventoryItem { Name = "Demo Scanner", Quantity = 5, Location = "Warehouse B" };
            order.AddItem(item);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            var orderId = order.OrderId;
            var itemId = item.ItemId;
            context.ChangeTracker.Clear();
            var saved = await context.Orders.Include(value => value.Items).SingleAsync(value => value.OrderId == orderId);
            Verify(saved.Items.Count == 1, "Order and item persisted");
            saved.CustomerName = "Samir Updated";
            saved.Items[0].Quantity = 7;
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            saved = await context.Orders.Include(value => value.Items).SingleAsync(value => value.OrderId == orderId);
            Verify(saved.CustomerName == "Samir Updated" && saved.Items[0].Quantity == 7, "Updates persisted");
            Console.WriteLine(saved.GetOrderSummary());
            saved.RemoveItem(itemId);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            Verify(await context.InventoryItems.AnyAsync(value => value.ItemId == itemId && value.OrderId == null),
                "Removing an item preserves inventory and clears its foreign key");
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();
        }
        // Calculate counts in SQL without loading every inventory entity.
        var summaries = await context.Orders.AsNoTracking().OrderBy(order => order.OrderId)
            .Select(order => new { order.OrderId, order.CustomerName, Count = order.Items.Count, order.DatePlaced }).ToListAsync();
        foreach (var order in summaries)
            Console.WriteLine(Order.FormatSummary(order.OrderId, order.CustomerName, order.Count, order.DatePlaced));
        Console.WriteLine("All Activity 1 checks passed. Test order changes rolled back; inventory seed retained.");
    }

    private static void Verify(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"FAILED: {description}");
        Console.WriteLine($"PASS: {description}");
    }
}
