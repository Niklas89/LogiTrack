using LogiTrack.Data;
using LogiTrack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LogiTrack.Controllers;

[ApiController]
[Route("api/orders")]
public class OrderController(LogiTrackContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Order>>> GetAll(CancellationToken cancellationToken) =>
        await context.Orders.AsNoTracking().Include(order => order.Items)
            .OrderBy(order => order.OrderId).ToListAsync(cancellationToken);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> GetById(int id, CancellationToken cancellationToken)
    {
        var order = await context.Orders.AsNoTracking().Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.OrderId == id, cancellationToken);
        if (order is null)
            return Problem(statusCode: 404, detail: $"Order {id} was not found.");
        return order;
    }

    [HttpPost]
    public async Task<ActionResult<Order>> Create(Order order, CancellationToken cancellationToken)
    {
        // This route creates new items atomically with the order; existing IDs are rejected.
        if (order.OrderId != 0 || order.Items is null ||
            order.Items.Any(item => item is null || item.ItemId != 0 || item.OrderId is not null))
            return Problem(statusCode: 400,
                detail: "Use orderId 0 and an items array of new items with itemId 0 and no orderId.");

        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = order.OrderId }, order);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var order = await context.Orders.FindAsync([id], cancellationToken);
        if (order is null)
            return Problem(statusCode: 404, detail: $"Order {id} was not found.");

        context.Orders.Remove(order);
        await context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
