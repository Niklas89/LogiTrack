# LogiTrack — Activity 1

ASP.NET Core project targeting .NET 10 with EF Core 10 and SQL Server Developer.
API endpoints for inventory and orders are reserved for Part 2.

## Run

From this directory, with SQL Server running and your Windows account granted access:

```powershell
dotnet restore
dotnet tool restore
dotnet ef database update
dotnet run -- --demo
```

The initial migration is already included; do not generate it again.
`dotnet run` starts the web host; `dotnet run -- --demo` runs the activity and exits.
The connection in `appsettings.json` targets **LogiTrack**, not **master**.
SQL Server is used instead of the assignment's SQLite example to match your existing database.
Integrated Security uses the Windows account running the application. The local developer
connection trusts the SQL Server certificate; use a trusted certificate for deployment.

## Files and behavior

- `Models/InventoryItem.cs`: inventory fields, validation attributes and `DisplayInfo()`.
- `Models/Order.cs`: `Items` is the requested list; add/remove methods and stable US-format summaries.
- `Data/LogiTrackContext.cs`: dependency-injected EF context, SQL quantity constraint, optional one-to-many relationship.
- `Data/ActivityDemo.cs`: executable assertions and SQL Server create/read/update/relationship checks.
- `Migrations/`: schema and model snapshot for future activities.

Expected sample output includes:

```text
Item: Pallet Jack | Quantity: 12 | Location: Warehouse A
Order #1001 for Samir | Items: 2 | Placed: 4/5/2025
Order #1001 for Samir | Items: 1 | Placed: 4/5/2025
```

The demo inserts a Pallet Jack only when inventory is empty. It saves and reloads test
orders and inventory updates inside a transaction, then rolls those changes back.
The original inventory seed remains. SQL Server identity values may advance during rollback.
Any failed assertion throws and makes the demo fail.

## Codex review applied

- `AddItem` rejects null and items assigned elsewhere, and checks duplicates in one O(n)
  scan without making a temporary list. Unsaved items are compared by reference because
  they all initially have ID zero. A separate ID cache would require synchronization
  when EF assigns IDs, so a simple scan suits these small orders.
- `RemoveItem` clears the relationship without deleting inventory; IDs must be positive.
- Both methods require the collection to be loaded when editing persisted orders
  (`Include(order => order.Items)`). They manage membership, not stock reservation.
- Summary queries project the item count in SQL and use `AsNoTracking`; they do not
  load every item just to count it. `GetOrderSummary` uses the loaded list's count.
- Quantity has a database check constraint in addition to the validation attribute.
  EF does not automatically execute DataAnnotations validation on `SaveChanges`.
- Deleting an order sets item foreign keys to null and preserves inventory.

This exercise's one-to-many model permits an inventory row to belong to only one order.
For shared products across orders and per-order quantities, introduce an `OrderItem`
entity in a later activity. Public setters are kept for the beginner model and EF;
call the membership methods to preserve their checks.
