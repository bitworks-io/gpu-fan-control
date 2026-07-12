using LightweightAmdGpuFanControl.Control;
using LightweightAmdGpuFanControl.Models;
using Xunit;

namespace LightweightAmdGpuFanControl.Tests;

public class GpuEnablementTests
{
    // ----- helpers -----

    private static AppSettings EmptySettings() => new();

    private static AppSettings SavedSettings(params (string id, bool enabled)[] gpus)
    {
        var s = new AppSettings();
        s.Gpus = gpus.Select(g => new GpuConfig { GpuId = g.id, Enabled = g.enabled }).ToList();
        return s;
    }

    // ----- fresh-install scenarios -----

    [Fact]
    public void SingleGpu_NoSavedConfig_EnabledByDefault()
    {
        var detected = new[] { "ADLX:GPU0" };
        var result = GpuEnablement.Reconcile(detected, EmptySettings());

        Assert.Single(result);
        Assert.Equal("ADLX:GPU0", result[0].GpuId);
        Assert.True(result[0].Enabled, "Primary GPU should be auto-enabled on first run.");
    }

    [Fact]
    public void TwoGpus_NoSavedConfig_OnlyFirstEnabled()
    {
        var detected = new[] { "ADLX:GPU0", "ADLX:GPU1" };
        var result = GpuEnablement.Reconcile(detected, EmptySettings());

        Assert.Equal(2, result.Count);
        Assert.True(result[0].Enabled, "Primary GPU should be auto-enabled.");
        Assert.False(result[1].Enabled, "Secondary GPU should default to disabled (opt-in).");
    }

    [Fact]
    public void ThreeGpus_NoSavedConfig_OnlyFirstEnabled()
    {
        var detected = new[] { "ADLX:GPU0", "ADLX:GPU1", "ADLX:GPU2" };
        var result = GpuEnablement.Reconcile(detected, EmptySettings());

        Assert.Equal(3, result.Count);
        Assert.True(result[0].Enabled);
        Assert.False(result[1].Enabled);
        Assert.False(result[2].Enabled);
    }

    [Fact]
    public void NoGpus_ReturnsEmptyList()
    {
        var result = GpuEnablement.Reconcile(Array.Empty<string>(), EmptySettings());
        Assert.Empty(result);
    }

    // ----- saved-config respected -----

    [Fact]
    public void SavedConfig_PrimaryDisabled_RespectsThatChoice()
    {
        var detected = new[] { "ADLX:GPU0" };
        var settings = SavedSettings(("ADLX:GPU0", false));
        var result = GpuEnablement.Reconcile(detected, settings);

        Assert.Single(result);
        Assert.False(result[0].Enabled, "Explicit saved disable must be respected even for primary.");
    }

    [Fact]
    public void SavedConfig_SecondaryEnabled_RespectedWhenExplicitlySet()
    {
        var detected = new[] { "ADLX:GPU0", "ADLX:GPU1" };
        // User previously opted in GPU1
        var settings = SavedSettings(("ADLX:GPU0", true), ("ADLX:GPU1", true));
        var result = GpuEnablement.Reconcile(detected, settings);

        Assert.True(result[0].Enabled);
        Assert.True(result[1].Enabled);
    }

    [Fact]
    public void SavedConfig_PrimaryEnabled_SecondaryNew_NewOneDefaultsDisabled()
    {
        var detected = new[] { "ADLX:GPU0", "ADLX:GPU1" };
        // Only GPU0 previously saved; GPU1 is newly detected
        var settings = SavedSettings(("ADLX:GPU0", true));
        var result = GpuEnablement.Reconcile(detected, settings);

        Assert.True(result[0].Enabled, "Saved enabled primary stays enabled.");
        Assert.False(result[1].Enabled, "Hot-added secondary defaults to disabled (opt-in).");
    }

    [Fact]
    public void SavedConfig_AnotherGpuEnabled_NewPrimaryDetected_NewOneDefaultsDisabled()
    {
        // GPU0 is new (not in saved config); GPU1 was previously enabled.
        // Even though GPU0 is the first detected, another GPU is already enabled → opt-in.
        var detected = new[] { "ADLX:GPU0", "ADLX:GPU1" };
        var settings = SavedSettings(("ADLX:GPU1", true));
        var result = GpuEnablement.Reconcile(detected, settings);

        Assert.False(result[0].Enabled,
            "New primary detected, but another GPU is already enabled — should require opt-in.");
        Assert.True(result[1].Enabled, "Saved enabled GPU1 stays enabled.");
    }

    // ----- ordering / identity -----

    [Fact]
    public void ResultPreservesDetectionOrder()
    {
        var detected = new[] { "ADL:0", "ADL:1", "ADL:2" };
        var result = GpuEnablement.Reconcile(detected, EmptySettings());

        Assert.Equal("ADL:0", result[0].GpuId);
        Assert.Equal("ADL:1", result[1].GpuId);
        Assert.Equal("ADL:2", result[2].GpuId);
    }

    [Fact]
    public void StaleGpuInSavedConfig_IsIgnored()
    {
        // GPU99 is in saved config but not detected → should not appear in result
        var detected = new[] { "ADLX:GPU0" };
        var settings = SavedSettings(("ADLX:GPU99", true));
        var result = GpuEnablement.Reconcile(detected, settings);

        Assert.Single(result);
        Assert.Equal("ADLX:GPU0", result[0].GpuId);
    }

    [Fact]
    public void SavedConfigEntryWithEmptyId_IsIgnored()
    {
        // Guard against corrupt/empty GpuId entries in saved config
        var detected = new[] { "ADLX:GPU0" };
        var settings = new AppSettings();
        settings.Gpus.Add(new GpuConfig { GpuId = "", Enabled = true });
        var result = GpuEnablement.Reconcile(detected, settings);

        // GPU0 has no saved preference (the empty entry is skipped), so it defaults to enabled.
        Assert.Single(result);
        Assert.True(result[0].Enabled);
    }
}
