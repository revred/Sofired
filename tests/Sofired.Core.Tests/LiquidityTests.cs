using FluentAssertions;
using Sofired.Core;
using Xunit;

namespace Sofired.Core.Tests;

public class LiquidityTests
{
    [Fact]
    public void Ok_Rejects_Wide_Spreads()
    {
        // 20% spread on mid of 1.00 - should be rejected with 12% threshold
        Liquidity.Ok(0.90, 1.10, 0.12).Should().BeFalse();

        // 10% spread on mid of 1.00 - should be accepted with 12% threshold  
        Liquidity.Ok(0.95, 1.05, 0.12).Should().BeTrue();
    }

    [Fact]
    public void Ok_Rejects_Invalid_Quotes()
    {
        Liquidity.Ok(0, 1.00).Should().BeFalse(); // zero bid
        Liquidity.Ok(1.00, 0).Should().BeFalse(); // zero ask
        Liquidity.Ok(1.00, 0.95).Should().BeFalse(); // crossed quotes
        Liquidity.Ok(1.00, 1.00).Should().BeFalse(); // locked quotes
    }

    [Fact]
    public void SpreadPercentage_Calculates_Correctly()
    {
        var spreadPct = Liquidity.SpreadPercentage(0.95, 1.05);
        spreadPct.Should().BeApproximately(0.10, 1e-6); // 10% spread

        var tightSpreadPct = Liquidity.SpreadPercentage(0.99, 1.01);
        tightSpreadPct.Should().BeApproximately(0.02, 1e-6); // 2% spread
    }

    [Fact]
    public void SpreadPercentage_Handles_Invalid_Inputs()
    {
        Liquidity.SpreadPercentage(0, 1.00).Should().Be(double.MaxValue);
        Liquidity.SpreadPercentage(1.00, 0).Should().Be(double.MaxValue);
        Liquidity.SpreadPercentage(1.00, 0.95).Should().Be(double.MaxValue);
    }
}