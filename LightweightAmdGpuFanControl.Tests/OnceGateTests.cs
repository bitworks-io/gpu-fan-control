using LightweightAmdGpuFanControl;
using Xunit;

public class OnceGateTests
{
    [Fact]
    public void FirstEntersSecondDoesNot()
    {
        var gate = new OnceGate();
        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
    }

    [Fact]
    public void GatesAreIndependent()
    {
        var a = new OnceGate();
        var b = new OnceGate();
        Assert.True(a.TryEnter());
        Assert.True(b.TryEnter());
        Assert.False(a.TryEnter());
        Assert.False(b.TryEnter());
    }

    [Fact]
    public async Task ParallelCallers_ExactlyOneEnters()
    {
        var gate = new OnceGate();
        using var barrier = new Barrier(64);
        var tasks = Enumerable.Range(0, 64).Select(_ => Task.Factory.StartNew(() =>
        {
            barrier.SignalAndWait();
            return gate.TryEnter();
        }, TaskCreationOptions.LongRunning)).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(r => r));
    }
}
