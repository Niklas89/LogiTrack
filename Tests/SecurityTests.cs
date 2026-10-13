using System.IdentityModel.Tokens.Jwt;
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
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;

namespace LogiTrack.Tests;

public class SecurityTests
{
    [Test]
    public async Task IdentityAndAuthorizationWorkAgainstSqlServer()
    {
        // Only this uniquely named disposable database is created and removed.
        var database = "LogiTrackSecurityTests_" + Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LogiTrack"] = $"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True",
                    ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                }));
        });
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LogiTrackContext>();
        Assert.That(context.Database.GetDbConnection().Database, Is.EqualTo(database));
        try
        {
            await context.Database.MigrateAsync();
            foreach (var path in new[] { "/api/inventory", "/api/orders", "/api/orders/1" })
                Assert.That((await client.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            foreach (var path in new[] { "/api/inventory", "/api/orders" })
            {
                Assert.That((await client.PostAsJsonAsync(path, new { })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That((await client.DeleteAsync(path + "/1")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            }

            var credentials = new { email = "security-test@example.com", password = "OnlyForTests!90210" };
            var registration = await client.PostAsJsonAsync("/api/auth/register",
                new { credentials.email, credentials.password, role = "Manager", roles = new[] { "Manager" } });
            Assert.That(registration.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That((await client.PostAsJsonAsync("/api/auth/register", credentials)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await client.PostAsJsonAsync("/api/auth/register", new { email = "weak@example.com", password = "weak" })).StatusCode,
                Is.EqualTo(HttpStatusCode.BadRequest));
            var login = await client.PostAsJsonAsync("/api/auth/login", credentials);
            Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
            Assert.That(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Any(claim => claim.Type == "role"), Is.False);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.That((await client.GetAsync("/api/inventory")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await client.GetAsync("/api/orders")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            foreach (var path in new[] { "/api/inventory", "/api/orders" })
            {
                Assert.That((await client.PostAsJsonAsync(path, new { })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That((await client.DeleteAsync(path + "/1")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            }

            await ManagerRoleCommand.RunAsync(factory.Services, credentials.email);
            login = await client.PostAsJsonAsync("/api/auth/login", credentials);
            token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var inventory = await client.PostAsJsonAsync("/api/inventory", new { name = "Test", quantity = 1, location = "Test" });
            Assert.That(inventory.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            var itemId = (await inventory.Content.ReadFromJsonAsync<InventoryItem>())!.ItemId;
            var orderResponse = await client.PostAsJsonAsync("/api/orders", new
            {
                customerName = "Test",
                items = new[] { new { name = "Box", quantity = 2, location = "Test" }, new { name = "Pallet", quantity = 1, location = "Test" } }
            });
            Assert.That(orderResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            var order = (await orderResponse.Content.ReadFromJsonAsync<Order>())!;
            Assert.That((await client.GetFromJsonAsync<Order>($"/api/orders/{order.OrderId}"))!.Items.Count, Is.EqualTo(2));
            Assert.That((await client.DeleteAsync($"/api/orders/{order.OrderId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That((await client.GetAsync($"/api/orders/{order.OrderId}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await client.DeleteAsync($"/api/inventory/{itemId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^10] + "abcdefghij");
            Assert.That((await client.GetAsync("/api/inventory")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            client.DefaultRequestHeaders.Authorization = null;
            for (var i = 0; i < 5; i++)
                Assert.That((await client.PostAsJsonAsync("/api/auth/login", new { credentials.email, password = "WrongPassword!123" })).StatusCode,
                    Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That((await client.PostAsJsonAsync("/api/auth/login", credentials)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            HttpResponseMessage? limited = null;
            for (var i = 0; i < 21; i++)
            {
                limited = await client.PostAsJsonAsync("/api/auth/login", credentials);
                if (limited.StatusCode == HttpStatusCode.TooManyRequests) break;
            }
            Assert.That(limited!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [TestCase("expired")]
    [TestCase("issuer")]
    [TestCase("audience")]
    [TestCase("signature")]
    public void RejectsInvalidTokens(string invalidField)
    {
        var settings = new JwtSettings { SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)) };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: invalidField == "issuer" ? "wrong" : settings.Issuer,
            audience: invalidField == "audience" ? "wrong" : settings.Audience,
            notBefore: now.AddHours(-1),
            expires: invalidField == "expired" ? now.AddMinutes(-5) : now.AddMinutes(15),
            signingCredentials: new SigningCredentials(invalidField == "signature"
                ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(48)) : settings.GetSigningKey(), SecurityAlgorithms.HmacSha256));
        var handler = new JwtSecurityTokenHandler();
        Assert.Throws(Is.InstanceOf<SecurityTokenException>(), () =>
            handler.ValidateToken(handler.WriteToken(token), settings.ValidationParameters(), out _));
    }
}
