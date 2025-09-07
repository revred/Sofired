using FluentAssertions;
using Sofired.Core;
using Xunit;

namespace Sofired.Core.Tests;

public class RealityCheckTests
{
    [Fact]
    public void Blocks_Wide_Spread_And_Low_OI_And_Stale_Quotes()
    {
        var r = RealityCheck.All(
            bid: 0.80, ask: 1.05, oi: 100, quoteAgeSec: 3.5, venueCount: 1,
            delta: 0.14, deltaMin: 0.10, deltaMax: 0.15,
            vix: 22, scaleUsed: 0.9, scaleExpectedHigh: 0.7,
            earningsDays: 5, size: 4, baselineSize: 6,
            dailyLossPct: -0.005, dailyStopPct: 0.01,
            timeOk: true, nbboSane: false, // crossed/locked NBBO
            maxSpreadPct: 0.12, minOi: 250, maxQuoteAgeSec: 2.0, minVenues: 2
        );
        r.Ok.Should().BeFalse();
        r.Reasons.Should().Contain(new[] { "NBBO_CROSSED_OR_LOCKED", "SPREAD_TOO_WIDE", "OPEN_INTEREST_TOO_LOW", "QUOTE_TOO_STALE", "INSUFFICIENT_VENUES" });
    }

    [Fact]
    public void Enforces_Earnings_Size_Cut_And_Inverse_Vix_Scaling()
    {
        var r = RealityCheck.All(
            bid: 0.95, ask: 1.05, oi: 500, quoteAgeSec: 0.5, venueCount: 3,
            delta: 0.11, deltaMin: 0.10, deltaMax: 0.15,
            vix: 28, scaleUsed: 0.85, scaleExpectedHigh: 0.7,  // too high for high regime
            earningsDays: 2, size: 6, baselineSize: 6,        // size not cut
            dailyLossPct: -0.002, dailyStopPct: 0.01,
            timeOk: true, nbboSane: true
        );
        r.Ok.Should().BeFalse();
        r.Reasons.Should().Contain(new[] { "EARNINGS_SIZE_NOT_REDUCED", "VIX_SCALING_NOT_INVERSE" });
    }

    [Fact]
    public void Kill_Switch_And_Time_Window_Guard_Work()
    {
        var r = RealityCheck.All(
            bid: 0.95, ask: 1.05, oi: 500, quoteAgeSec: 0.5, venueCount: 3,
            delta: 0.11, deltaMin: 0.10, deltaMax: 0.15,
            vix: 20, scaleUsed: 0.9, scaleExpectedHigh: 0.7,
            earningsDays: 10, size: 4, baselineSize: 6,
            dailyLossPct: -0.012, dailyStopPct: 0.01, // breach
            timeOk: false, nbboSane: true
        );
        r.Ok.Should().BeFalse();
        r.Reasons.Should().Contain(new[] { "DAILY_KILL_SWITCH_BREACHED", "OUTSIDE_EXECUTION_WINDOW" });
    }

    [Fact]
    public void Allows_Good_Conditions_Through()
    {
        var r = RealityCheck.All(
            bid: 0.95, ask: 1.05, oi: 500, quoteAgeSec: 0.5, venueCount: 3,
            delta: 0.12, deltaMin: 0.10, deltaMax: 0.15,
            vix: 18, scaleUsed: 0.9, scaleExpectedHigh: 0.7,
            earningsDays: 10, size: 4, baselineSize: 6,
            dailyLossPct: -0.005, dailyStopPct: 0.01,
            timeOk: true, nbboSane: true,
            maxSpreadPct: 0.12, minOi: 250, maxQuoteAgeSec: 2.0, minVenues: 2
        );
        r.Ok.Should().BeTrue();
        r.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void Delta_Out_Of_Band_Is_Blocked()
    {
        var r = RealityCheck.All(
            bid: 0.95, ask: 1.05, oi: 500, quoteAgeSec: 0.5, venueCount: 3,
            delta: 0.05, deltaMin: 0.10, deltaMax: 0.15, // delta too low
            vix: 18, scaleUsed: 0.9, scaleExpectedHigh: 0.7,
            earningsDays: 10, size: 4, baselineSize: 6,
            dailyLossPct: -0.005, dailyStopPct: 0.01,
            timeOk: true, nbboSane: true
        );
        r.Ok.Should().BeFalse();
        r.Reasons.Should().Contain("DELTA_OUT_OF_BAND");
    }
}