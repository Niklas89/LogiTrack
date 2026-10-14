using System.Data.Common;
using System.Diagnostics;
using LogiTrack.Controllers;
using LogiTrack.Data;
using LogiTrack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;

namespace LogiTrack.Tests;

public class PerformanceTests
{
    [Test]
    public async Task CacheExpiresAndAllWritesRefreshInventoryWithFewerDatabaseCalls()
    {
        var database = "LogiTrackPerformanceTests_" + Guid.NewGuid().ToString("N");
        var counter = new QueryCounter();
        var options = new DbContextOptionsBuilder<LogiTrackContext>()
            .UseSqlServer($"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True")
            .AddInterceptors(counter).Options;
        await using var context = new LogiTrackContext(options);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        using var cache = new InventoryCache(memory);
        var inventory = new InventoryController(context, cache);
        var orders = new OrderController(context, cache);
        var ct = CancellationToken.None;
        try
        {
            await context.Database.MigrateAsync();
            await inventory.Create(new InventoryItem { Name = "Box", Quantity = 1, Location = "A" }, ct);
            context.ChangeTracker.Clear();
            // Warm up EF/SQL before comparing equivalent list reads.
            await inventory.GetAll(ct);
            cache.Invalidate();
            counter.Commands = 0;
            var timer = Stopwatch.StartNew();
            var cold = (await inventory.GetAll(ct)).Value!;
            var coldTime = timer.Elapsed.TotalMilliseconds;
            Assert.That(counter.Commands, Is.EqualTo(1));
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
            counter.Commands = 0;
            timer.Restart();
            for (var i = 0; i < 100; i++)
                Assert.That((await inventory.GetAll(ct)).Value!.Count, Is.EqualTo(cold.Count));
            var warmTime = timer.Elapsed.TotalMilliseconds / 100;
            Assert.That(counter.Commands, Is.Zero, "Cache hits must avoid SQL entirely.");
            TestContext.Out.WriteLine($"Uncached list: {coldTime:F3} ms; cached average (100 reads): {warmTime:F3} ms; SQL reads: 1 vs 0.");

            var extra = new InventoryItem { Name = "Pallet", Quantity = 2, Location = "B" };
            await inventory.Create(extra, ct);
            Assert.That((await inventory.GetAll(ct)).Value!.Count, Is.EqualTo(2));
            await inventory.Delete(extra.ItemId, ct);
            Assert.That((await inventory.GetAll(ct)).Value!.Count, Is.EqualTo(1));

            var order = new Order { CustomerName = "Test", Items = [new() { Name = "Order item", Location = "C" }] };
            await orders.Create(order, ct);
            Assert.That((await inventory.GetAll(ct)).Value!.Single(item => item.ItemId == order.Items[0].ItemId).OrderId,
                Is.EqualTo(order.OrderId));
            context.ChangeTracker.Clear();
            counter.Commands = 0;
            Assert.That((await orders.GetAll(ct)).Value!.Single().Items.Count, Is.EqualTo(1));
            Assert.That(counter.Commands, Is.EqualTo(1), "Eager loading should use one SQL query.");
            Assert.That(context.ChangeTracker.Entries(), Is.Empty);
            counter.Commands = 0;
            Assert.That(await orders.Delete(order.OrderId, ct), Is.TypeOf<NoContentResult>());
            Assert.That(counter.Commands, Is.EqualTo(1), "Deletion must not first fetch the order.");
            Assert.That((await inventory.GetAll(ct)).Value!.All(item => item.OrderId == null), Is.True);
            Assert.That(await orders.Delete(order.OrderId, ct), Is.TypeOf<ObjectResult>().With.Property("StatusCode").EqualTo(404));

            // Simulate an external DB writer: stale until the absolute TTL expires.
            await context.InventoryItems.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Quantity, 7));
            Assert.That((await inventory.GetAll(ct)).Value!.Any(item => item.Quantity != 7), Is.True);
            await Task.Delay(TimeSpan.FromSeconds(31));
            counter.Commands = 0;
            Assert.That((await inventory.GetAll(ct)).Value!.All(item => item.Quantity == 7), Is.True);
            Assert.That(counter.Commands, Is.EqualTo(1));
        }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Commands;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return ValueTask.FromResult(result);
        }
    }
}
