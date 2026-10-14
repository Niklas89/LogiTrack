# Final capstone submission

## Architecture and key decisions

LogiTrack is an ASP.NET Core .NET 10 API with controllers for authentication,
inventory, and orders. EF Core 10 persists business data and ASP.NET Core Identity
users/roles in SQL Server. Migrations reproduce the schema. Database persistence
is the selected state strategy: restarting the API does not discard saved data.
Orders and nested inventory are committed together by a single SaveChanges call.
Deleting an order preserves inventory through an ON DELETE SET NULL foreign key.

JWT bearer authentication protects all business routes. Any authenticated warehouse
user can read data; only Managers can create or delete it. Roles are assigned by a
local operator command, never public registration. This is shared warehouse data,
not a per-user cart; session IDs and raw user tokens are not stored on orders.
The signing key and connection configuration must remain stable across restarts.

A singleton IMemoryCache service accelerates inventory reads for 30 seconds. The
SQL database remains authoritative. A fresh host starts with an empty cache and
rehydrates it on the first read. Writes invalidate it, concurrent misses share a
fill gate, and a version check prevents stale fills following concurrent writes.
No-tracking queries, eager loading and direct SQL deletion avoid unnecessary work.

## Final review changes

- Added an HTTP integration test that disposes the first host completely, creates
  a new host against the same SQL database, and verifies inventory, order fields,
  relationships, existing JWT access, fresh login and persisted Manager membership.
- Added HTTP checks for invalid input, rejected existing IDs, no partial order
  creation, Location links, cache invalidation and post-restart deletion behavior.
- Added no-store response headers to inventory and order controllers to prevent
  browser/proxy storage of protected responses. Server-side caching still works.
- Retained the existing centralized cache and database relationship handling;
  no additional session store or duplicated persistence logic was necessary.

## Verification — October 14, 2026

`dotnet build LogiTrack.slnx --no-restore -c Release` succeeded with zero warnings
and zero errors. `dotnet test Tests/LogiTrack.Tests.csproj --logger "console;verbosity=normal"`
passed all seven tests using local SQL Server.

Coverage includes registration/login, invalid and expired tokens, anonymous 401,
ordinary-user 403, Manager access, lockout and rate limiting; inventory and order
creation/read/deletion; invalid input; cache expiry and all write invalidations;
and a fresh application-host restart with the same persistent database.
Each integration test creates and removes only its own uniquely named test database.
SQL Server must run locally, and the test account needs create/drop database rights.

Measured controller reads: 2.230 ms uncached versus 0.001 ms mean cached, with one
SQL read versus zero per cache hit. Restart test HTTP reads: 15.951 ms cold versus
0.858 ms mean warm (20 requests). HTTP measurements use in-process TestServer,
not network load; these observations are not latency guarantees.

Swagger loaded in a browser and an anonymous inventory GET returned 401.
The full authenticated workflow was exercised by automated HTTP tests; a complete
manual Swagger replay is described below. Restart verification recreates the
application host and all its services, not the SQL Server service or operating system.

## Swagger peer-review walkthrough

1. Configure the signing key, apply migrations, and run the HTTP profile as
   described in README.md. Open http://localhost:5150/swagger.
2. Register and log in. Authorize using the access token. Read inventory (200),
   then try creating inventory as an ordinary user (403).
3. Grant Manager through the documented local command. Log in again and replace
   the token. Create inventory and an order using the README JSON examples (201).
4. Read inventory twice and retrieve the order by ID. Record the returned IDs.
5. Stop the API and restart with the same database and signing key. Reauthorize
   if needed. Confirm the saved inventory and order are returned, then log in
   again to verify the persisted account. An expired token requires fresh login.
6. Send negative quantity, missing customerName and existing nested item IDs;
   expect 400 and no new records. Delete your test order (204), verify its inventory
   remains with null orderId, and repeat the deletion (404). Delete test items.
7. Clear authorization and verify protected reads return 401.

## Scope and future deployment work

This is a tested capstone API, not a complete production operations setup. Large
lists still need pagination. Multiple application instances need coordinated cache
invalidation and rate limiting. Deployment should include managed secrets, trusted
TLS, database backups with restore drills, monitoring and health checks. JWT
revocation, tenant/owner isolation and per-order product quantities are outside
this project's current scope. Existing tokens expire rather than being revoked
immediately after role or account changes.
