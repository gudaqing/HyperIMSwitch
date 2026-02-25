using System;
using System.Threading;

namespace HyperIMSwitch.Core.Infrastructure;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsFirstInstance { get; }

    public SingleInstanceGuard(string appName)
    {
        _mutex = new Mutex(true, $"Global\\{appName}_SingleInstance", out bool created);
        IsFirstInstance = created;
    }

    public void Dispose()
    {
        if (IsFirstInstance)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
