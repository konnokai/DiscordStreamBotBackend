using Google.Apis.Util.Store;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

/// <summary>阻止 Google SDK 隱含 Store/Delete；authoritative MySQL mutation 由持有 lease 的服務明確執行。</summary>
internal sealed class NonPersistentGoogleDataStore : IDataStore
{
    public Task ClearAsync() => Task.CompletedTask;

    public Task DeleteAsync<T>(string key) => Task.CompletedTask;

    public Task<T> GetAsync<T>(string key) => Task.FromResult(default(T));

    public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;
}
