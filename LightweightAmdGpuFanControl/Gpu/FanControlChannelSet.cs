namespace LightweightAmdGpuFanControl.Gpu;

/// <summary>
/// Owns the single native session (ADLX or ADL) and the per-GPU fan-control channels that
/// borrow it. Disposing the set restores every channel to the driver's automatic curve first,
/// then tears the native session down last — the ordering the channels depend on.
/// </summary>
public sealed class FanControlChannelSet : IDisposable
{
    private readonly IDisposable? _session;
    private bool _disposed;

    public FanControlChannelSet(IReadOnlyList<IFanControlBackend> channels, IDisposable? session)
    {
        Channels = channels;
        _session = session;
    }

    /// <summary>The controllable GPU channels, in detection order (index 0 is the primary GPU).</summary>
    public IReadOnlyList<IFanControlBackend> Channels { get; }

    public bool HasChannels => Channels.Count > 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Channels first: each restores its GPU to automatic without touching the session.
        foreach (var channel in Channels)
        {
            try { channel.Dispose(); }
            catch { /* best-effort restore; continue tearing down the rest */ }
        }

        // Session last: safe to unload the native API now that no channel will use it.
        try { _session?.Dispose(); }
        catch { /* best-effort */ }
    }
}
