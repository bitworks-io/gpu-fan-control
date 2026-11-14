using LightweightAmdGpuFanControl.Cli;
using Xunit;

public class LaunchDecisionTests
{
    [Fact]
    public void Null_IsTray() => Assert.Equal(LaunchAction.Tray, LaunchDecision.Decide(null));

    [Theory]
    [InlineData(new string[0], LaunchAction.Tray)]
    [InlineData(new[] { "foo" }, LaunchAction.Tray)]
    [InlineData(new[] { "/stop" }, LaunchAction.Tray)]
    [InlineData(new[] { "--stop" }, LaunchAction.Stop)]
    [InlineData(new[] { "--STOP" }, LaunchAction.Stop)]
    [InlineData(new[] { "--stop", "--x" }, LaunchAction.Unsupported)]
    [InlineData(new[] { "foo", "--stop" }, LaunchAction.Unsupported)]
    [InlineData(new[] { "--restore-auto" }, LaunchAction.Unsupported)]
    [InlineData(new[] { "--probe" }, LaunchAction.Unsupported)]
    [InlineData(new[] { "--reset-tuning-to-factory", "--gpu", "768" }, LaunchAction.Unsupported)]
    [InlineData(new[] { "--x" }, LaunchAction.Unsupported)]
    public void Decide_Cases(string[] args, LaunchAction expected)
        => Assert.Equal(expected, LaunchDecision.Decide(args));
}
