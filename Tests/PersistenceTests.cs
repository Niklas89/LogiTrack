using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LogiTrack.Data;
using LogiTrack.Models;
using LogiTrack.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LogiTrack.Tests;

public class PersistenceTests
{
    [Test]
    public async Task WorkflowSurvivesHostRestartAndRehydratesCache()
    {
        var database = "LogiTrackPersistenceTests_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True";
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var credentials = new { email = "persistence@example.com", password = "OnlyForTests!90210" };
        WebApplicationFactory<Program> CreateHost() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:LogiTrack"] = connection, ["Jwt:SigningKey"] = key }));
        });
        var options = new DbContextOptionsBuilder<LogiTrackContext>().UseSqlServer(connection).Options;
        await using var databaseContext = new LogiTrackContext(options);
        try
        {
            await databaseContext.Database.MigrateAsync();
            int itemId;
            Order saved;
            string token;
            await using (var first = CreateHost())
            {
                using var client = first.CreateClient();
                Assert.That((await client.PostAsJsonAsync("/api/auth/register", credentials)).StatusCode, Is.EqualTo(HttpStatusCode.Created));
                await ManagerRoleCommand.RunAsync(first.Services, credentials.email);
                token = await Login(client, credentials);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                // Cache an empty list before writes, then verify invalidation over HTTP.
                Assert.That(await client.GetFromJsonAsync<List<InventoryItem>>("/api/inventory"), Is.Empty);
                var created = await client.PostAsJsonAsync("/api/inventory", new { name = "Persistent pallet", quantity = 4, location = "A" });
                Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
                itemId = (await created.Content.ReadFromJsonAsync<InventoryItem>())!.ItemId;
                var order = await client.PostAsJsonAsync("/api/orders", new { customerName = "Restart test", items = new[] { new { name = "Box", quantity = 2, location = "B" } } });
                Assert.That(order.StatusCode, Is.EqualTo(HttpStatusCode.Created));
                saved = (await order.Content.ReadFromJsonAsync<Order>())!;
                Assert.That(order.Headers.Location, Is.Not.Null);
                Assert.That((await client.GetAsync(order.Headers.Location)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That((await client.GetFromJsonAsync<List<InventoryItem>>("/api/inventory"))!.Count, Is.EqualTo(2));

                foreach (var invalid in new object[] {
                    new { name = "Bad", quantity = -1, location = "A" },
                    new { quantity = 1, location = "A" },
                    new { itemId = itemId, name = "Bad", quantity = 1, location = "A" } })
                    Assert.That((await client.PostAsJsonAsync("/api/inventory", invalid)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                foreach (var invalid in new object[] {
                    new { items = Array.Empty<object>() },
                    new { customerName = "Bad", items = new[] { new { name = "Bad", quantity = -1, location = "A" } } },
                    new { customerName = "Bad", items = new[] { new { itemId, name = "Bad", quantity = 1, location = "A" } } } })
                    Assert.That((await client.PostAsJsonAsync("/api/orders", invalid)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That((await client.GetFromJsonAsync<List<Order>>("/api/orders"))!.Count, Is.EqualTo(1));
            } // Dispose the entire host, service provider and memory cache before starting again.

            await using (var second = CreateHost())
            {
                using var client = second.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var timer = Stopwatch.StartNew();
                var inventory = await client.GetFromJsonAsync<List<InventoryItem>>("/api/inventory");
                var cold = timer.Elapsed.TotalMilliseconds;
                Assert.That(inventory!.Single(item => item.ItemId == itemId).Quantity, Is.EqualTo(4));
                Assert.That(inventory!.Single(item => item.ItemId == saved.Items[0].ItemId).OrderId, Is.EqualTo(saved.OrderId));
                timer.Restart();
                for (var i = 0; i < 20; i++)
                {
                    using var response = await client.GetAsync("/api/inventory");
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
                }
                TestContext.Out.WriteLine($"Restart HTTP cold read: {cold:F3} ms; warm average: {timer.Elapsed.TotalMilliseconds / 20:F3} ms (in-process TestServer).");
                var restored = (await client.GetFromJsonAsync<Order>($"/api/orders/{saved.OrderId}"))!;
                Assert.That(restored.CustomerName, Is.EqualTo(saved.CustomerName));
                Assert.That(restored.DatePlaced, Is.EqualTo(saved.DatePlaced));
                Assert.That(restored.Items.Single().Quantity, Is.EqualTo(2));
                // A fresh login proves Identity and Manager membership also survived.
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client, credentials));
                Assert.That((await client.DeleteAsync($"/api/orders/{saved.OrderId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
                Assert.That((await client.GetFromJsonAsync<List<InventoryItem>>("/api/inventory"))!.All(item => item.OrderId is null), Is.True);
                Assert.That((await client.GetAsync($"/api/orders/{saved.OrderId}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That((await client.DeleteAsync($"/api/inventory/{itemId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            }
        }
        finally { await databaseContext.Database.EnsureDeletedAsync(); }
    }

    private static async Task<string> Login(HttpClient client, object credentials)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", credentials);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }
}
