# LogiTrack — Activities 1 and 2

ASP.NET Core project targeting .NET 10 with EF Core 10 and SQL Server Developer.
Part 2 adds async inventory and order controllers, validation, and development Swagger documentation.

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

## Activity 2 API

Start with `dotnet run --launch-profile http`, then open
http://localhost:5150/swagger. Swagger UI loads its assets from a public CDN,
so the browser needs internet access. The OpenAPI document is available at
`/openapi/v1.json` in Development and can also be imported into Postman.

| Method | Route | Result |
| --- | --- | --- |
| GET | /api/inventory | 200 with all inventory |
| POST | /api/inventory | 201 with the created item |
| DELETE | /api/inventory/{id} | 204, or 404 if missing |
| GET | /api/orders | 200 with orders and their items |
| GET | /api/orders/{id} | 200 with item details, or 404 |
| POST | /api/orders | 201 with the order and a Location header |
| DELETE | /api/orders/{id} | 204, or 404 if missing |

Order POST accepts multiple **new** inventory items:

```json
{
  "customerName": "Samir",
  "items": [
    { "name": "Box", "quantity": 2, "location": "Warehouse A" },
    { "name": "Pallet", "quantity": 1, "location": "Warehouse B" }
  ]
}
```

Omit generated IDs and item `orderId` values. Existing inventory IDs are rejected;
this route creates the order and its nested inventory together in one SaveChanges
transaction. An empty items array is allowed. Deleting an order preserves inventory
and clears its order IDs. Deleting inventory also removes it from order membership.
These routes do not decrement stock or implement per-order line quantities.

Review improvements applied: explicit routes, integer route constraints, async EF
calls with cancellation, no-tracking reads, eager-loaded order items, automatic
DataAnnotations validation (400), Problem Details for missing IDs and invalid
creation IDs, and a JSON-ignored back-reference to prevent serialization cycles.
Authentication and pagination remain future activities.

### Verification

`dotnet build --no-restore` passed with zero warnings and zero errors.
Manual testing through Swagger was reported successful by the project owner.

To repeat the manual checks:

1. Apply the existing migration with `dotnet ef database update` and start the app.
2. In Swagger, use **Try it out** for each of the seven routes above. Create
   inventory and a two-item order; copy their returned IDs into GET/DELETE calls.
3. Confirm GET requests return the saved data and that it survives an app restart.
4. Delete the test order, confirm its inventory remains with null order IDs, then
   delete each test inventory item. Repeat a delete to check the 404 response.
5. Submit a negative quantity or missing customer name and confirm a 400 response.

`LogiTrack.http` also contains manual request examples.
