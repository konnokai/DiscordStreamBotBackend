using System;
using System.Security.Cryptography;

namespace DiscordStreamBotBackend.Services.Auth
{
    // https://blog.miniasp.com/post/2008/05/13/Random-vs-RNGCryptoServiceProvider
    // https://learn.microsoft.com/zh-tw/dotnet/api/system.security.cryptography.randomnumbergenerator?view=net-7.0
    /// <summary>
    /// 使用密碼編譯安全的亂數產生器。
    /// </summary>
    public static class RNG
    {
        private static RandomNumberGenerator rngp = RandomNumberGenerator.Create();
        private static byte[] rb = new byte[4];

        /// <summary>
        /// 產生非負亂數。
        /// </summary>
        public static int Next()
        {
            rngp.GetBytes(rb);
            int value = BitConverter.ToInt32(rb, 0);
            if (value < 0) value = -value;
            return value;
        }
        /// <summary>
        /// 產生 0 到 max 之間的亂數，包含 max。
        /// </summary>
        /// <param name="max">最大值。</param>
        public static int Next(int max)
        {
            rngp.GetBytes(rb);
            int value = BitConverter.ToInt32(rb, 0);
            value = value % (max + 1);
            if (value < 0) value = -value;
            return value;
        }
        /// <summary>
        /// 產生 min 到 max 之間的亂數，包含兩端。
        /// </summary>
        /// <param name="min">最小值。</param>
        /// <param name="max">最大值。</param>
        public static int Next(int min, int max)
        {
            int value = Next(max - min) + min;
            return value;
        }
    }
}
