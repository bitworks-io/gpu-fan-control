namespace LightweightAmdGpuFanControl.Control;

/// <summary>Per-GPU control state for fail-closed fan writes.</summary>
public enum GpuControlState { Starting, Regulating, Refused, Degraded }

/// <summary>What the caller must do after a write error.</summary>
public enum WriteErrorAction { None, ReleaseOnce }

/// <summary>
/// Pure per-GPU state machine. <c>Starting</c> covers start-up and the self-test; a write error there refuses the GPU.
/// A write error while <c>Regulating</c> degrades it and asks for exactly one release. <c>Refused</c> and
/// <c>Degraded</c> are final: the GPU is never written again this run and never returns to <c>Regulating</c>.
/// Not thread-safe; the caller serializes access (the control lock).
/// </summary>
public sealed class GpuControlTracker
{
    public GpuControlState State { get; private set; } = GpuControlState.Starting;

    /// <summary>The first write error's text (single line); null until one occurs.</summary>
    public string? Message { get; private set; }

    public bool MayWrite => State == GpuControlState.Starting || State == GpuControlState.Regulating;

    /// <summary>The self-test passed. Only valid from <c>Starting</c>.</summary>
    public bool BeginRegulating()
    {
        if (State != GpuControlState.Starting) return false;
        State = GpuControlState.Regulating;
        return true;
    }

    /// <summary>Refuses the GPU without a write error (for example a self-test that did not pass). Only valid from <c>Starting</c>.</summary>
    public void Refuse(string message)
    {
        if (State != GpuControlState.Starting) return;
        State = GpuControlState.Refused;
        Message = OneLine(message);
    }

    public WriteErrorAction OnWriteError(string? message)
    {
        switch (State)
        {
            case GpuControlState.Starting:
                State = GpuControlState.Refused;
                Message = OneLine(message);
                return WriteErrorAction.None;
            case GpuControlState.Regulating:
                State = GpuControlState.Degraded;
                Message = OneLine(message);
                return WriteErrorAction.ReleaseOnce;
            default:
                return WriteErrorAction.None;
        }
    }

    private static string OneLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "(no message)";
        var parts = message!.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(p => p.Trim()).Where(p => p.Length > 0));
    }
}
