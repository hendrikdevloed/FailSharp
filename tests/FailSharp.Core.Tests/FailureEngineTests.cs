using FailSharp.Core;
using Xunit;

namespace FailSharp.Core.Tests;

public class FailureEngineTests
{
    [Fact]
    public async Task ConsiderFailure_WhenDisabled_NeverFails()
    {
        var client = new FakeSimVarClient();
        var settings = new FailureSettings { Enabled = false, ProbabilityPercent = 100, EligibleEngines = new HashSet<int> { 1, 2 } };
        var engine = new FailureEngine(client, settings);

        var result = await engine.ConsiderFailureAsync();

        Assert.Null(result);
        Assert.Empty(client.FailedEngines);
    }

    [Fact]
    public async Task ConsiderFailure_WhenNotConnected_NeverFails()
    {
        var client = new FakeSimVarClient { IsConnected = false };
        var settings = new FailureSettings { Enabled = true, ProbabilityPercent = 100, EligibleEngines = new HashSet<int> { 1, 2 } };
        var engine = new FailureEngine(client, settings);

        var result = await engine.ConsiderFailureAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsiderFailure_ZeroProbability_NeverFails()
    {
        var client = new FakeSimVarClient();
        var settings = new FailureSettings { Enabled = true, ProbabilityPercent = 0, EligibleEngines = new HashSet<int> { 1, 2 } };
        var engine = new FailureEngine(client, settings);

        var result = await engine.ConsiderFailureAsync();

        Assert.Null(result);
        Assert.Empty(client.FailedEngines);
    }

    [Fact]
    public async Task ConsiderFailure_HundredPercentProbability_AlwaysFailsAnEligibleEngine()
    {
        var client = new FakeSimVarClient { EngineCount = 3 };
        var settings = new FailureSettings { Enabled = true, ProbabilityPercent = 100, EligibleEngines = new HashSet<int> { 2, 3 } };
        var engine = new FailureEngine(client, settings);

        var result = await engine.ConsiderFailureAsync();

        Assert.NotNull(result);
        Assert.Contains(result!.Value, new[] { 2, 3 });
        Assert.Single(client.FailedEngines);
    }

    [Fact]
    public async Task ConsiderFailure_OnlyPicksEligibleEngines()
    {
        var client = new FakeSimVarClient { EngineCount = 4 };
        var settings = new FailureSettings { Enabled = true, ProbabilityPercent = 100, EligibleEngines = new HashSet<int> { 1 } };
        var engine = new FailureEngine(client, settings, new Random(42));

        for (var i = 0; i < 20; i++)
        {
            await client.RepairEngineAsync(1);
            var result = await engine.ConsiderFailureAsync();
            Assert.Equal(1, result);
        }
    }

    [Fact]
    public async Task ConsiderFailure_WhenAllEligibleEnginesAlreadyFailed_ReturnsNull()
    {
        var client = new FakeSimVarClient { EngineCount = 1 };
        await client.FailEngineAsync(1);
        var settings = new FailureSettings { Enabled = true, ProbabilityPercent = 100, EligibleEngines = new HashSet<int> { 1 } };
        var engine = new FailureEngine(client, settings);

        var result = await engine.ConsiderFailureAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task RepairAllAsync_ClearsFailuresOnEveryEngine()
    {
        var client = new FakeSimVarClient { EngineCount = 2 };
        await client.FailEngineAsync(1);
        await client.FailEngineAsync(2);
        var engine = new FailureEngine(client, new FailureSettings());

        await engine.RepairAllAsync();

        Assert.Empty(client.FailedEngines);
    }
}
