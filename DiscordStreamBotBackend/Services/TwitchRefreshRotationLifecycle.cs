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

    /// <summary>在尚未關機時登記 refresh operation，使 drain 能等待其完成 rotation 保存交接。</summary>
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

    /// <summary>追蹤 provider 已接受之 rotation 的保存工作，直到落盤或確認 stale。</summary>
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

    /// <summary>停止接納新 refresh，等待執行中 operation 交接後 drain 全部保存工作。</summary>
    public Task StopAcceptingAndDrainAsync()
    {
        lock (_gate)
        {
            // 先停止接納 refresh，再等現有 operation 登記已接受 rotation 的保存工作。
            // 完成這段交接後才可 drain task snapshot，避免關機在空窗提前結束。
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
            // 指標更新不得中斷已接受的 refresh rotation 保存。
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
