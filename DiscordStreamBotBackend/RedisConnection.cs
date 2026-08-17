using StackExchange.Redis;
using System;

public sealed class RedisConnection
{
    private static readonly Lazy<RedisConnection> lazy = new(() =>
    {
        if (string.IsNullOrEmpty(_settingOption)) throw new InvalidOperationException("請先呼叫 Init()。");
        return new RedisConnection();
    });

    private static string _settingOption;

    public readonly ConnectionMultiplexer ConnectionMultiplexer;

    public static RedisConnection Instance
    {
        get
        {
            return lazy.Value;
        }
    }

    private RedisConnection()
    {
        ConnectionMultiplexer = ConnectionMultiplexer.Connect(_settingOption);
    }

    public static void Init(string settingOption)
    {
        _settingOption = settingOption;
    }
}

