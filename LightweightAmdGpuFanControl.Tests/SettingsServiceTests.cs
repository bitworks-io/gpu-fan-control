using System;
using System.IO;
using LightweightAmdGpuFanControl.Models;
using LightweightAmdGpuFanControl.Services;
using Xunit;

public class SettingsServiceTests
{
    private static string TempPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lafc-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json");
    }

    [Fact]
    public void Clamps_out_of_range_fields()
    {
        var path = TempPath();
        File.WriteAllText(path, "{\"TargetTempC\":200,\"MinFanPercent\":5,\"MaxFanPercent\":99,\"HysteresisC\":50}");
        var s = new SettingsService(path).Load();
        Assert.Equal(90, s.TargetTempC);
        Assert.InRange(s.MinFanPercent, 20, 70);
        Assert.InRange(s.MaxFanPercent, 40, 85);
        Assert.Equal(10, s.HysteresisC);
        Assert.True(s.MinFanPercent < s.MaxFanPercent);
    }

    [Fact]
    public void Enforces_min_less_than_max()
    {
        var path = TempPath();
        File.WriteAllText(path, "{\"MinFanPercent\":80,\"MaxFanPercent\":40}");
        var s = new SettingsService(path).Load();
        Assert.True(s.MinFanPercent < s.MaxFanPercent);
        Assert.InRange(s.MinFanPercent, 20, 70);
        Assert.InRange(s.MaxFanPercent, 40, 85);
    }

    [Fact]
    public void Old_schema_json_deserializes_with_new_defaults()
    {
        var path = TempPath();
        File.WriteAllText(path, "{\"TargetTempC\":72,\"StartWithWindows\":true}");
        var s = new SettingsService(path).Load();
        Assert.Equal(72, s.TargetTempC);
        Assert.True(s.StartWithWindows);
        Assert.Equal(20, s.MinFanPercent);
        Assert.Equal(85, s.MaxFanPercent);
        Assert.Equal(3, s.HysteresisC);
        Assert.Equal(90, s.CriticalTempC);
        Assert.NotNull(s.Gpus);
    }

    [Fact]
    public void Manual_fan_setpoint_survives_raising_then_lowering_min_fan()
    {
        // Regression (hardware-reported): in Manual mode, raising the minimum fan speed
        // permanently ratcheted the manual setpoint upward — Validate clamped
        // ManualFanPercent to the live Min/Max — so lowering Min again never brought the
        // fan back down. The manual setpoint must be preserved; the policy clamps it to
        // the curve bounds at runtime instead of destroying the stored value.
        var path = TempPath();
        var svc = new SettingsService(path);

        var s = new AppSettings
        {
            Mode = FanMode.Manual,
            ManualFanPercent = 50,
            MinFanPercent = 20,
            MaxFanPercent = 85
        };
        svc.Save(s);

        // User raises the minimum fan speed above their manual setpoint.
        s.MinFanPercent = 60;
        svc.Save(s);
        Assert.Equal(50, s.ManualFanPercent); // not ratcheted up to 60

        // User lowers it back down — the manual setpoint is still what they chose.
        s.MinFanPercent = 20;
        svc.Save(s);
        Assert.Equal(50, s.ManualFanPercent);
    }

    [Fact]
    public void Corrupt_json_returns_defaults()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ this is not valid json ");
        var s = new SettingsService(path).Load();
        Assert.Equal(65, s.TargetTempC);
        Assert.Equal(85, s.MaxFanPercent);
    }
}
