# Redis

[← Back to README](../README.md)

`RedisDatabaseHarness` gives direct access to the Redis database (`Database`). `DatabaseNumber` selects a database
index and `KeyPrefix` scopes its keys; `FlushAsync` deletes the harness's data (keys with `KeyPrefix` when set,
otherwise the whole database). To keep fixtures that share one Redis server apart, give each application its own
database: `DefaultDatabase` on `RedisHarness`/`RedisStackHarness` adds `defaultDatabase=N` to the connection string
passed to the application, and `RedisDatabaseHarness` follows it:

```csharp
var redis = new RedisHarness { ConnectionName = "Redis", DefaultDatabase = 3 };
var redisDatabase = new RedisDatabaseHarness(redis);

await redisDatabase.FlushAsync();
```
