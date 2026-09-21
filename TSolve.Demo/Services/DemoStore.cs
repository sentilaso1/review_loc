using TSolve.Demo.Models;

namespace TSolve.Demo.Services;

public sealed class DemoStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DemoState _state = new();

    public async Task<T> ReadAsync<T>(Func<DemoState, T> reader)
    {
        await _gate.WaitAsync();
        try { return reader(_state); }
        finally { _gate.Release(); }
    }

    public async Task<T> WriteAsync<T>(Func<DemoState, T> writer)
    {
        await _gate.WaitAsync();
        try { return writer(_state); }
        finally { _gate.Release(); }
    }

    public async Task ResetAsync()
    {
        await _gate.WaitAsync();
        try { _state = new DemoState(); }
        finally { _gate.Release(); }
    }
}
