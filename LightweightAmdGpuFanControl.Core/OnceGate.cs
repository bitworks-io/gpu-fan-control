using System.Threading;

namespace LightweightAmdGpuFanControl;

/// <summary>Lets exactly one caller through, however many race for it.</summary>
public sealed class OnceGate
{
    private int _entered;

    public bool TryEnter() => Interlocked.Exchange(ref _entered, 1) == 0;
}
