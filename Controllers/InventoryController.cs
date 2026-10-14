using LogiTrack.Data;
using LogiTrack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace LogiTrack.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/inventory")]
public class InventoryController(LogiTrackContext context, InventoryCache cache) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<InventoryItem>>> GetAll(CancellationToken cancellationToken) =>
        await cache.GetAsync(context, cancellationToken);

    [HttpPost]
    [Authorize(Roles = "Manager")]
    public async Task<ActionResult<InventoryItem>> Create(InventoryItem item, CancellationToken cancellationToken)
    {
        if (item.ItemId != 0 || item.OrderId is not null)
            return Problem(statusCode: 400, detail: "New inventory must have itemId 0 and no orderId.");

        context.InventoryItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);
        cache.Invalidate();
        return CreatedAtAction(nameof(GetAll), item);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var item = await context.InventoryItems.FindAsync([id], cancellationToken);
        if (item is null)
            return Problem(statusCode: 404, detail: $"Inventory item {id} was not found.");

        context.InventoryItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
        cache.Invalidate();
        return NoContent();
    }
}
