namespace Enhanced.Testing.Component.Redis;

internal static class RedisConnectionString
{
    public static string Build(string host, ushort port, int? defaultDatabase) =>
        defaultDatabase is null ? $"{host}:{port}" : $"{host}:{port},defaultDatabase={defaultDatabase.Value}";

    public static int? ValidateDatabase(int? database, string paramName)
    {
        if (database is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(database.Value, paramName);
        }

        return database;
    }
}
