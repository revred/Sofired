using FluentAssertions;
using Sofired.Core;
using Xunit;

namespace Sofired.Core.Tests;

public class RealismIntegrationTests
{
    [Fact]
    public void RealOptionsPricing_ValidatesRealism_And_BlocksFantasyTrades()
    {
        // Create a realistic options pricing
        var goodPricing = new RealOptionsPricing
        {
            NetCreditReceived = 0.30m,
            Delta = 0.12m,
            OpenInterest = 500,
            Bid = 0.29,      // 10% spread instead of 13.3%
            Ask = 0.32,      // (0.32-0.29)/0.305 = 9.8% < 12% threshold
            QuoteAgeSec = 1.0,
            VenueCount = 3,
            NbboSane = true
        };

        // Test with good conditions - should pass
        var goodResult = goodPricing.ValidateRealism(
            deltaMin: 0.10, deltaMax: 0.15, vix: 18, scaleUsed: 0.9, scaleExpectedHigh: 0.7,
            earningsDays: 10, size: 4, baselineSize: 6, dailyLossPct: -0.005, dailyStopPct: 0.01,
            timeOk: true
        );
        
        if (!goodResult.Ok)
        {
            // Debug output to see why the "good" result failed
            Console.WriteLine($"Good result failed with reasons: {string.Join(", ", goodResult.Reasons)}");
        }
        goodResult.Ok.Should().BeTrue();
        goodResult.Reasons.Should().BeEmpty();

        // Create a fantasy options pricing (wide spreads, low OI, bad conditions)
        var fantasyPricing = new RealOptionsPricing
        {
            NetCreditReceived = 2.50m, // Unrealistically high credit
            Delta = 0.05m, // Too aggressive delta
            OpenInterest = 50, // Too low OI
            Bid = 2.00,
            Ask = 3.00, // 40% spread - way too wide
            QuoteAgeSec = 5.0, // Stale quotes
            VenueCount = 1, // Single venue
            NbboSane = false // Crossed/locked NBBO
        };

        // Test with fantasy conditions - should be blocked
        var fantasyResult = fantasyPricing.ValidateRealism(
            deltaMin: 0.10, deltaMax: 0.15, vix: 30, scaleUsed: 1.2, scaleExpectedHigh: 0.7, // Bad VIX scaling
            earningsDays: 1, size: 10, baselineSize: 10, // Earnings without size reduction
            dailyLossPct: -0.015, dailyStopPct: 0.01, // Kill switch breached
            timeOk: false // Outside time window
        );

        fantasyResult.Ok.Should().BeFalse();
        fantasyResult.Reasons.Should().Contain(new[] {
            "NBBO_CROSSED_OR_LOCKED",
            "SPREAD_TOO_WIDE", 
            "OPEN_INTEREST_TOO_LOW",
            "QUOTE_TOO_STALE",
            "INSUFFICIENT_VENUES",
            "DELTA_OUT_OF_BAND",
            "EARNINGS_SIZE_NOT_REDUCED",
            "VIX_SCALING_NOT_INVERSE",
            "DAILY_KILL_SWITCH_BREACHED",
            "OUTSIDE_EXECUTION_WINDOW"
        });
    }

    [Fact]
    public void SlippageModel_PreventsOptimisticBacktestFills()
    {
        // Wide spread scenario - should force conservative fills
        var realisticFillPrice = SlippageModel.GetRealisticFillPrice(bid: 0.80, ask: 1.20, requestedPrice: 1.10);
        
        // Should not get the optimistic 1.10, but rather something more conservative
        realisticFillPrice.Should().BeLessOrEqualTo(1.00); // Mid price
        realisticFillPrice.Should().BeGreaterOrEqualTo(0.80); // Never worse than bid

        // Test slippage ladder progression
        var attempt1 = SlippageModel.ApplySlippage(0.90, 1.00, 1); // First attempt
        var attempt2 = SlippageModel.ApplySlippage(0.90, 1.00, 2); // Second attempt  
        var attempt3 = SlippageModel.ApplySlippage(0.90, 1.00, 3); // Final attempt

        attempt1.Should().BeGreaterOrEqualTo(attempt2);
        attempt2.Should().BeGreaterOrEqualTo(attempt3);
        attempt3.Should().BeGreaterOrEqualTo(0.90); // Never below bid
    }

    [Fact]
    public void Liquidity_ChecksPreventWideSpreadFills()
    {
        // Good liquidity - tight spread
        Liquidity.Ok(bid: 0.95, ask: 1.05, maxSpreadPct: 0.12).Should().BeTrue(); // 10% spread

        // Poor liquidity - wide spread  
        Liquidity.Ok(bid: 0.80, ask: 1.20, maxSpreadPct: 0.12).Should().BeFalse(); // 40% spread

        // Calculate exact spread percentages
        var tightSpread = Liquidity.SpreadPercentage(0.95, 1.05);
        var wideSpread = Liquidity.SpreadPercentage(0.80, 1.20);

        tightSpread.Should().BeApproximately(0.10, 0.01); // 10%
        wideSpread.Should().BeApproximately(0.40, 0.01); // 40%
    }
}