namespace Enhanced.Testing.Component.Redis;

internal static class RedisConnectionString
{
    public static string Build(string host, ushort port, int? defaultDatabase)
    {
        if (defaultDatabase is null)
        {
            return $"{host}:{port}";
        }

        ArgumentOutOfRangeException.ThrowIfNegative(defaultDatabase.Value, nameof(defaultDatabase));
        return $"{host}:{port},defaultDatabase={defaultDatabase.Value}";
    }
}
