namespace LightweightAmdGpuFanControl.Models;

/// <summary>
/// Per-GPU control preference. The shared global curve (target/min/max) applies to every
/// GPU whose <see cref="Enabled"/> flag is set.
/// </summary>
public sealed class GpuConfig
{
    /// <summary>Stable identifier for the GPU (backend-provided).</summary>
    public string GpuId { get; set; } = "";

    /// <summary>Whether the fan of this GPU is controlled by the app.</summary>
    public bool Enabled { get; set; }
}
