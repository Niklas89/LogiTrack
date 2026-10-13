using LogiTrack.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace LogiTrack.Data;

public class LogiTrackContext(DbContextOptions<LogiTrackContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Order>().HasMany(order => order.Items)
            .WithOne(item => item.Order).HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<InventoryItem>().ToTable(table =>
            table.HasCheckConstraint("CK_InventoryItems_Quantity", "[Quantity] >= 0"));
    }
}
