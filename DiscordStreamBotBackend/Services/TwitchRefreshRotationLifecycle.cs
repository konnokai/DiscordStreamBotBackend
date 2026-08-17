using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

internal sealed class TwitchRefreshRotationLifecycle
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Task> _acceptedPersistenceTasks = [];
    private readonly Action<int> _pendingCountChanged;
    private TaskCompletionSource _activeOperationsDrained = CompletedSource();
    private Task _stopTask;
    private long _nextPersistenceId;
    private int _activeOperations;
    private bool _stopping;

    public TwitchRefreshRotationLifecycle(Action<int> pendingCountChanged = null)
    {
        _pendingCountChanged = pendingCountChanged;
    }

    public int ActiveOperationCount
    {
        get
        {
            lock (_gate)
                return _activeOperations;
        }
    }

    public int PendingPersistenceCount
    {
        get
        {
            lock (_gate)
                return _acceptedPersistenceTasks.Count;
        }
    }

    /// <summary>在尚未關機時登記 refresh operation，讓 drain 等待 rotation 寫入完成。</summary>
    public bool TryBeginRefresh(out Lease lease)
    {
        lock (_gate)
        {
            if (_stopping)
            {
                lease = null;
                return false;
            }

            if (_activeOperations++ == 0)
                _activeOperationsDrained = NewSource();
            lease = new Lease(this);
            return true;
        }
    }

    /// <summary>追蹤 provider 已接受的 rotation 寫入工作，直到完成或確認狀態已過期。</summary>
    public void TrackAcceptedPersistence(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        long id;
        int count;
        lock (_gate)
        {
            id = ++_nextPersistenceId;
            _acceptedPersistenceTasks.Add(id, task);
            count = _acceptedPersistenceTasks.Count;
        }
        NotifyPendingCountChanged(count);
        _ = RemoveWhenCompletedAsync(id, task);
    }

    /// <summary>停止接受新的 refresh，等待進行中的 operation 完成交接，再等待所有寫入工作結束。</summary>
    public Task StopAcceptingAndDrainAsync()
    {
        lock (_gate)
        {
            // 先停止接受 refresh，再等現有 operation 登記已接受的 rotation 寫入工作。
            // 完成這段交接後才能 drain task snapshot，避免關機在空窗期提前結束。
            _stopping = true;
            return _stopTask ??= DrainAsync(_activeOperationsDrained.Task);
        }
    }

    private async Task DrainAsync(Task activeOperationsDrained)
    {
        await Task.Yield();
        await activeOperationsDrained;
        while (true)
        {
            Task[] pending;
            lock (_gate)
                pending = _acceptedPersistenceTasks.Values.ToArray();
            if (pending.Length == 0)
                return;
            await Task.WhenAll(pending);
            await Task.Yield();
        }
    }

    private async Task RemoveWhenCompletedAsync(long id, Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // 原始 task 由關閉 drain 觀察；此 observer 只負責移除追蹤狀態。
        }
        finally
        {
            int count;
            lock (_gate)
            {
                _acceptedPersistenceTasks.Remove(id);
                count = _acceptedPersistenceTasks.Count;
            }
            NotifyPendingCountChanged(count);
        }
    }

    private void NotifyPendingCountChanged(int count)
    {
        try
        {
            _pendingCountChanged?.Invoke(count);
        }
        catch
        {
            // 指標更新不得中斷已接受的 refresh rotation 寫入工作。
        }
    }

    private void CompleteRefresh()
    {
        TaskCompletionSource drained = null;
        lock (_gate)
        {
            if (--_activeOperations == 0)
                drained = _activeOperationsDrained;
        }
        drained?.TrySetResult();
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = NewSource();
        source.SetResult();
        return source;
    }

    private static TaskCompletionSource NewSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public sealed class Lease : IDisposable
    {
        private TwitchRefreshRotationLifecycle _owner;

        internal Lease(TwitchRefreshRotationLifecycle owner)
        {
            _owner = owner;
        }

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.CompleteRefresh();
    }
}
