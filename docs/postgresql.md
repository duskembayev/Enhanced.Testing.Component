# PostgreSQL and EF Core

[← Back to README](../README.md)

## EF Core

`DbContextHarness<TContext>` creates the schema on start (`EnsureCreatedOnStart`) and runs queries against the
application's database (`ExecuteAsync`).

## Per-test databases from a template

To isolate tests that write to PostgreSQL without starting a container per test, prepare a template database once and
copy it for each test. `CREATE DATABASE ... TEMPLATE` copies the database at the storage level, so a copy stays fast
(about 60 ms, mostly `docker exec` overhead) even when building the schema and seed data takes much longer:

```csharp
await postgreSql.CreateTemplateAsync("people_template", async connectionString =>
{
    var options = new DbContextOptionsBuilder<PeopleDbContext>().UseNpgsql(connectionString).Options;
    await using var context = new PeopleDbContext(options);
    await context.Database.EnsureCreatedAsync();
});

var connectionString = await postgreSql.CreateDatabaseAsync($"people_{Guid.NewGuid():N}", "people_template");
```

- `CreateTemplateAsync` creates the database (dropping an existing one with that name), runs your callback with a
  non-pooled connection string, then marks the database as a template, blocks new connections to it and closes the
  open ones. PostgreSQL refuses to copy a template that has open connections.
- `CreateDatabaseAsync` returns the new database's connection string. Without a template it creates an empty database.
- `DropDatabaseAsync` drops a database (or a template) and closes its connections. `GetConnectionString(database)`
  returns the connection string for any database in the container.

The SQL runs through `psql` inside the container, so the package doesn't depend on Npgsql. Requires PostgreSQL 13 or
later. Names are quoted, must be at most 63 bytes (PostgreSQL's identifier limit) and are case-sensitive.

Databases live as long as the container, so drop per-test databases with `DropDatabaseAsync` when a test finishes if
the container is shared by many tests. `CreateTemplateAsync` drops and re-creates a template with the same name.
