using LightweightAmdGpuFanControl.Models;

namespace LightweightAmdGpuFanControl.Control;

/// <summary>
/// Pure helper that reconciles a detected GPU list against saved per-GPU config.
///
/// Default-on rule: a GPU with no saved preference is enabled iff it is the primary
/// (first-enumerated) GPU AND no other GPU is currently enabled. Additional newly-seen
/// GPUs default to disabled. This ensures a fresh install enables exactly one card;
/// hot-adding a second card later keeps it opt-in.
/// </summary>
public static class GpuEnablement
{
    /// <summary>
    /// Produces a reconciled GPU config list from the detected GPU IDs and the existing settings.
    /// The returned list contains exactly one entry per detected GPU, in detection order.
    /// </summary>
    public static IReadOnlyList<GpuConfig> Reconcile(
        IReadOnlyList<string> detectedIds,
        AppSettings settings)
    {
        var saved = settings.Gpus
            .Where(g => !string.IsNullOrEmpty(g.GpuId))
            .ToDictionary(g => g.GpuId, g => g.Enabled);

        // Only GPUs that are still detected count: a stale entry for a removed card must not block the new primary.
        var detected = new HashSet<string>(detectedIds, StringComparer.Ordinal);
        bool anyCurrentlyEnabled = saved.Any(kv => kv.Value && detected.Contains(kv.Key));
        string? primaryId = detectedIds.Count > 0 ? detectedIds[0] : null;

        var result = new List<GpuConfig>(detectedIds.Count);
        foreach (var id in detectedIds)
        {
            bool enabled;
            if (saved.TryGetValue(id, out bool savedEnabled))
            {
                // Respect saved preference exactly.
                enabled = savedEnabled;
            }
            else if (id == primaryId && !anyCurrentlyEnabled)
            {
                // First run / new primary: enable automatically.
                enabled = true;
            }
            else
            {
                // Newly-seen non-primary (or primary when another is already enabled): opt-in.
                enabled = false;
            }

            result.Add(new GpuConfig { GpuId = id, Enabled = enabled });
        }

        return result;
    }
}
