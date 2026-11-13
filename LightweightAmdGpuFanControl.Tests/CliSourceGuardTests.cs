using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// The net48 app cannot be built or run by these tests, so the command-line safety properties are pinned
/// against its source text: dispatch before any side effect, v1.0.1 tray body intact, stop wiring present.
/// </summary>
public class CliSourceGuardTests
{
    private const string MutexName = @"Local\\LightweightAmdGpuFanControl.SingleInstance";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LightweightAmdGpuFanControl.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repo root (LightweightAmdGpuFanControl.sln) not found above " + AppContext.BaseDirectory);
    }

    private static string App(params string[] parts)
        => SourceText.Read(Path.Combine(new[] { FindRepoRoot(), "LightweightAmdGpuFanControl" }.Concat(parts).ToArray()));

    private static int IndexOrFail(string source, string token, int from = 0)
    {
        var i = source.IndexOf(token, from, StringComparison.Ordinal);
        Assert.True(i >= 0, "missing: " + token);
        return i;
    }

    private static string MethodBody(string source, string signature)
    {
        var start = IndexOrFail(source, signature);
        var open = source.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException("unbalanced braces after " + signature);
    }

    [Fact]
    public void Program_DispatchesBeforeAnySideEffect()
    {
        var p = App("Program.cs");
        var decide = IndexOrFail(p, "LaunchDecision.Decide(args)");
        var mutex = IndexOrFail(p, "new Mutex(");
        var box = IndexOrFail(p, "MessageBox.Show");
        Assert.True(decide < mutex, "Decide must come before the mutex");
        Assert.True(decide < box, "Decide must come before any MessageBox");
        Assert.Contains("static int Main(string[] args)", p);

        var firstStatement = MethodBody(p, "static int Main(string[] args)").TrimStart('{').TrimStart();
        Assert.StartsWith("var action = LaunchDecision.Decide(args);", firstStatement);

        var stop = IndexOrFail(p, "return StopCommand.Run(");
        var unsupported = IndexOrFail(p, "return ExitCodes.NothingToDo");
        Assert.True(stop < mutex, "Stop branch must return before the mutex");
        Assert.True(unsupported < mutex, "Unsupported branch must return before the mutex");
    }

    [Fact]
    public void Program_TrayBranchKeepsV101Behavior()
    {
        var p = App("Program.cs");
        var mutex = IndexOrFail(p, "new Mutex(");
        var tray = p.Substring(mutex);
        Assert.Contains("new Mutex(true, \"Local\\\\LightweightAmdGpuFanControl.SingleInstance\", out bool createdNew)", p);
        Assert.Contains("\"Lightweight AMD GPU Fan Control is already running.\"", tray);
        Assert.Contains("Application.Run(new SystrayApplicationContext())", tray);
    }

    [Fact]
    public void Program_OnlyTheAlreadyRunningMessageBoxRemains()
    {
        var p = App("Program.cs");
        Assert.Single(Regex.Matches(p, Regex.Escape("MessageBox.Show")));
        var decide = IndexOrFail(p, "LaunchDecision.Decide(args)");
        var box = IndexOrFail(p, "MessageBox.Show");
        var already = IndexOrFail(p, "is already running.");
        Assert.True(decide < box);
        Assert.True(box < already);
    }

    [Fact]
    public void StopCommand_OpensOnlyAndNeverKills()
    {
        var source = App("Services", "StopCommand.cs");
        Assert.Contains("TryOpenExisting", source);
        foreach (var token in new[] { "new EventWaitHandle", "Kill(", "taskkill", "TerminateProcess" })
            Assert.DoesNotContain(token, source);
    }

    [Fact]
    public void StopSignal_UsesTheSharedEventName()
    {
        Assert.Contains("\"Local\\\\LightweightAmdGpuFanControl.Stop\"", App("Services", "StopSignal.cs"));
    }

    [Fact]
    public void SingleInstance_MutexNameMatchesProgram()
    {
        var program = App("Program.cs");
        var instance = App("Services", "SingleInstance.cs");
        Assert.Contains("\"" + MutexName + "\"", program);
        Assert.Contains("\"" + MutexName + "\"", instance);
    }

    [Fact]
    public void Tray_ExitRunsOnce_GateIsTheFirstStatement()
    {
        var s = App("SystrayApplicationContext.cs");
        Assert.Contains("private readonly OnceGate _exitGate = new OnceGate();", s);
        var body = MethodBody(s, "private void Exit()").TrimStart('{').TrimStart();
        Assert.StartsWith("if (!_exitGate.TryEnter()) return;", body);
    }

    [Fact]
    public void FanControlService_StopIsIdempotent()
    {
        var f = App("Services", "FanControlService.cs");
        var stop = MethodBody(f, "public void Stop()");
        var dispose = IndexOrFail(stop, "_channelSet?.Dispose();");
        var nullSet = IndexOrFail(stop, "_channelSet = null;", dispose);
        IndexOrFail(stop, "_channels = new List<ControlChannel>();", nullSet);
    }

    [Fact]
    public void Tray_ListensForStopAndDisposesTheListenerOnEveryExitPath()
    {
        var s = App("SystrayApplicationContext.cs");
        Assert.Contains("StopSignal.Listen(() => _uiContext.Post(_ => Exit(), null), _logService)", s);
        var exit = MethodBody(s, "private void Exit()");
        Assert.Contains("_stopListener", exit);
        Assert.Contains("_fanControlService.Stop()", exit);
        Assert.Contains("_stopListener", MethodBody(s, "private void ShutdownForExternalClose()"));
    }
}
