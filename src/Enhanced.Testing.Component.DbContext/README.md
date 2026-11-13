# Enhanced.Testing.Component.DbContext

[![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.DbContext)](https://www.nuget.org/packages/Enhanced.Testing.Component.DbContext)

An EF Core harness for [Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component): creates
the application's schema on start and runs queries against the application's database from the test.

```shell
dotnet add package Enhanced.Testing.Component.DbContext
```

## Usage

```csharp
var postgreSql = new PostgreSqlHarness { ConnectionName = "People" };
var peopleDb = new DbContextHarness<PeopleDbContext>();
peopleDb.EnsureCreatedOnStart(builder => builder.UseNpgsql(postgreSql.GetConnectionString()));

var component = ComponentBuilder.Create<Program>()
                                .AddHarness(postgreSql)
                                .AddHarness(peopleDb)
                                .AddHarness(grpc)
                                .Build();
```

`DbContextHarness<TContext>` works with any `DbContext` that has a constructor taking `DbContextOptions`. Add the
harness after the database harness it depends on. The `EnsureCreatedOnStart` callback runs when the component
starts, after the database container is up, and creates the schema with `Database.EnsureCreated`.

In a test, query or seed through the application's own `DbContext` (resolved from the application's service
provider in a new scope):

```csharp
await peopleDb.ExecuteAsync(db => db.Persons.AddAsync(new Person { Name = "Kelly" }).AsTask());

var exists = await peopleDb.ExecuteAsync(db => db.Persons.AnyAsync(p => p.Name == "Kelly"));
```

`Execute` and `Execute<T>` are the synchronous versions.

The harness works with any EF Core provider. For PostgreSQL, see also
[per-test template databases](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.PostgreSql/README.md#per-test-databases-from-a-template).
