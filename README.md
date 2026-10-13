# LogiTrack — Activities 1, 2, and 3

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
Activity 3 below adds authentication and role restrictions. Pagination remains a future improvement.

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

## Activity 3: Identity and JWT authentication

Identity and JWT bearer packages use version 10.0.12, matching the existing .NET/EF
packages. SQL Server is retained in place of the assignment's SQLite provider so
existing orders, inventory, and migrations remain usable. `ApplicationUser` derives
from `IdentityUser`; `LogiTrackContext` now derives from `IdentityDbContext<ApplicationUser>`.
The `AddIdentity` migration adds the Identity tables and has been applied locally.

### Configuration and startup

A random development signing key has been saved in .NET user secrets on this
machine. It is not included in Git. For a fresh checkout on another machine, create
one using PowerShell 7:

```powershell
$jwtKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
@{ 'Jwt:SigningKey' = $jwtKey } | ConvertTo-Json -Compress | dotnet user-secrets set
```

Then run:

```powershell
dotnet restore LogiTrack.slnx
dotnet ef database update
dotnet run --launch-profile http
```

Startup fails if the signing key is absent or too short. For deployment, supply
`Jwt__SigningKey` through a secret store/environment variable, set the intended
`Jwt__Issuer` and `Jwt__Audience`, and serve the API over HTTPS. Development HTTP
is for localhost testing only. Production enables HTTPS redirection and HSTS;
configure the HTTPS listener/proxy correctly before exposing the service.
Never commit signing keys, passwords, or real access tokens.

### Register, log in, and authorize Swagger

Open http://localhost:5150/swagger and refresh to load the new endpoints.

1. Execute `POST /api/auth/register` with the JSON below (choose your own password).
2. Execute `POST /api/auth/login` with the same JSON.
3. Copy `accessToken` from the response. Click **Authorize** and paste the token
   only, without the `Bearer` prefix. Swagger adds the header automatically.
4. GET inventory/orders should return 200. New users cannot POST or DELETE these
   resources: these requests return 403. Without a valid token, they return 401.

```json
{
  "email": "samir@example.com",
  "password": "ChooseYourOwn!90210"
}
```

Registration returns 201 and only the new user's ID/email. Passwords must contain
12–128 characters, uppercase, lowercase, a digit, and a symbol. Identity hashes
and salts passwords. Registration accepts no roles or Identity security fields.
Login returns `accessToken`, `tokenType`, and `expiresAtUtc`; tokens last 15 minutes.

### Assign the Manager role

After registering the intended manager, run this local operator command from the
project directory (in a separate terminal if the API is running):

```powershell
dotnet run --no-build --launch-profile http -- --grant-manager samir@example.com
```

The command finds an existing account, creates the Manager role if necessary,
assigns it, and exits. It neither creates an account nor sets a default password.
There is no public role-assignment endpoint and the first registered user is not
implicitly privileged. Log in again and replace the Swagger token after promotion.

| Routes | Required access |
| --- | --- |
| POST /api/auth/register, /api/auth/login | Anonymous; rate limited |
| GET /api/inventory, /api/orders, /api/orders/{id} | Any authenticated user |
| POST /api/inventory, /api/orders | Manager |
| DELETE /api/inventory/{id}, /api/orders/{id} | Manager |

Order creation requires Manager because it also creates inventory. This activity
uses shared warehouse access: authenticated users can read all orders; ownership
or tenant-based isolation is not implemented.

### Security review and validation

- JWT validation checks signature, HS256 algorithm, issuer, audience, and lifetime
  with 30 seconds of clock tolerance. Identity cookie defaults are overridden so
  API failures return 401/403 rather than redirects.
- Login uses `UserManager` and `SignInManager.CheckPasswordSignInAsync`, with a
  15-minute lockout after five failed passwords. Unknown, locked, and incorrect
  credentials receive the same generic 401 message.
- Registration and login share a limit of 20 requests per minute per source IP
  per app instance, returning 429 when exceeded. Proxy and multi-instance rate
  limiting need deployment-specific configuration.
- Input DTOs prevent role/security-field overposting. Duplicate registrations
  return a generic 400. Login responses use `Cache-Control: no-store`.
- Tokens are stateless: role changes, account lockout, and password changes do not
  revoke tokens already issued. Log in again for updated roles; existing tokens
  expire after 15 minutes. Refresh tokens, revocation, email verification, and an
  MFA login flow are not implemented. Accounts with two-factor enabled cannot use
  this password-only login endpoint.
- Swagger is available only in Development. Its bearer scheme enables testing
  protected routes; tokens are not configured to persist across page reloads.

Five automated security tests passed, including SQL Server integration coverage
for registration, duplicate/weak-password rejection, login, denied anonymous access
to all seven routes, role-overposting rejection, ordinary-user 403 responses,
Manager CRUD, tampered tokens, lockout, and rate limiting. Token validation tests
also reject expired tokens and incorrect issuer, audience, or signing keys.

To repeat them with local SQL Server running:

```powershell
dotnet test Tests/LogiTrack.Tests.csproj
```

The integration test applies migrations to a unique `LogiTrackSecurityTests_*`
database and deletes only that test database afterward. The test account needs
permission to create/drop databases. It does not modify the main LogiTrack data.
Use Swagger to repeat the user/Manager access checks manually.
