# Enhanced.Testing.Component.PostgreSql

[![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.PostgreSql)](https://www.nuget.org/packages/Enhanced.Testing.Component.PostgreSql)

A PostgreSQL container harness for [Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component),
with fast non-durable defaults and per-test template databases.

```shell
dotnet add package Enhanced.Testing.Component.PostgreSql
```

## Usage

```csharp
var postgreSql = new PostgreSqlHarness { ConnectionName = "Orders" };

var component = ComponentBuilder.Create<Program>()
                                .AddHarness(postgreSql)
                                .AddHarness(http)
                                .Build();
```

The application reads `ConnectionStrings:Orders` and gets the container's connection string.
`postgreSql.GetConnectionString()` returns it to the test.

| Option | Default | |
|---|---|---|
| `Image` | `postgres:15.1` | The container image |
| `Username`, `Password`, `Database` | Testcontainers defaults | Credentials and database name |
| `ConnectionProperties` | empty | Extra connection string properties (escaped correctly) |
| `Durable` | `false` | See below |

`ConnectionName`, `ConfigureContainer` and `StartConcurrently` are shared by all container harnesses; see
[Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component/README.md#container-harnesses).

To create the schema with EF Core and query the application's database, add
[Enhanced.Testing.Component.DbContext](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.DbContext/README.md).

## Fast, non-durable by default

Test data does not need to survive a crash, so the harness keeps the data directory (`PGDATA`) on tmpfs and runs with
`fsync`, `full_page_writes` and `synchronous_commit` off. Nothing is written to disk, which reduces IO load when many
containers run in parallel.

Set `Durable = true` to get the image's durable behaviour instead, for example when a test restarts the server and
expects the data to survive, or when the database is too large to keep in memory:

```csharp
var postgreSql = new PostgreSqlHarness { Durable = true };
```

The harness mounts tmpfs at `/var/lib/postgresql/data`; if `ConfigureContainer` mounts a volume or bind mount there,
Docker rejects the duplicate mount point, so set `Durable = true` in that case.

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

## A database per component

When the application itself should work with a clean database, add a `PostgreSqlDatabaseHarness` to the component.
It creates a database in the shared container when the component starts, passes its connection string to the
application, and drops it when the component stops. It fits a
[`ComponentEnvironment`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component/README.md#a-fresh-application-per-test)
that shares the container between per-test components:

```csharp
var orders = new PostgreSqlDatabaseHarness(fixture.PostgreSql)
{
    ConnectionName = "Orders",
    Template = "orders_template"
};

await using var component = await fixture.Environment
                                         .CreateComponent<Program>()
                                         .AddHarness(orders)
                                         .AddHarness(http)
                                         .StartAsync();
```

| Option | Default | |
|---|---|---|
| `ConnectionName` | none | Passes the database's connection string as `ConnectionStrings:{ConnectionName}`, overriding the one set by `PostgreSqlHarness` |
| `Template` | none | The template database to copy; without it the database is empty |
| `Database` | `test_<guid>`, new on every start | The database name; `CurrentDatabase` returns the one in use |

`GetConnectionString()` returns the connection string to the test. The database is dropped when the component stops,
even if its start was cancelled. If a database with a fixed `Database` name already exists, the start fails and that
database is left untouched. In a component that also owns the
`PostgreSqlHarness`, add the database harness after it.
