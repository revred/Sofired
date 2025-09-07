using FluentAssertions;
using Sofired.Core;
using Xunit;

namespace Sofired.Core.Tests;

public class SlippageModelTests
{
    [Fact]
    public void Ladder_Is_Conservative_And_NonIncreasing()
    {
        var ladder = SlippageModel.SellLadder(0.90, 1.00).ToArray();
        ladder.Length.Should().Be(3);
        ladder[0].Should().BeGreaterThanOrEqualTo(ladder[1]);
        ladder[1].Should().BeGreaterThanOrEqualTo(ladder[2]);
        ladder[2].Should().BeGreaterThanOrEqualTo(0.90); // never under bid
    }

    [Fact]
    public void Ladder_Handles_Tight_Spreads_Correctly()
    {
        var ladder = SlippageModel.SellLadder(1.00, 1.02, 0.01).ToArray();
        ladder.Length.Should().Be(3);
        ladder[0].Should().Be(1.01); // mid
        ladder[1].Should().BeApproximately(1.008, 0.001); // max(step3, mid - tick) = max(1.008, 1.00) = 1.008
        ladder[2].Should().BeApproximately(1.008, 0.001); // step3 = max(bid, mid - 10% width) = max(1.00, 1.008) = 1.008
    }

    [Fact]
    public void Apply_Slippage_Returns_Correct_Attempt_Price()
    {
        var price1 = SlippageModel.ApplySlippage(0.90, 1.00, 1);
        var price2 = SlippageModel.ApplySlippage(0.90, 1.00, 2);
        var price3 = SlippageModel.ApplySlippage(0.90, 1.00, 3);

        price1.Should().BeGreaterThanOrEqualTo(price2);
        price2.Should().BeGreaterThanOrEqualTo(price3);
        price3.Should().BeGreaterThanOrEqualTo(0.90);
    }

    [Fact]
    public void GetRealisticFillPrice_Finds_Best_Available_Price()
    {
        // Request mid price, should get mid
        var fillPrice1 = SlippageModel.GetRealisticFillPrice(0.90, 1.00, 0.95);
        fillPrice1.Should().Be(0.95);

        // Request above mid, should get mid (best we can do)
        var fillPrice2 = SlippageModel.GetRealisticFillPrice(0.90, 1.00, 0.98);
        fillPrice2.Should().Be(0.95);

        // Request very low price, should get worst ladder price
        var fillPrice3 = SlippageModel.GetRealisticFillPrice(0.90, 1.00, 0.85);
        fillPrice3.Should().Be(0.90); // Will fall back to bid
    }

    [Fact]
    public void Handles_Invalid_Quotes_Gracefully()
    {
        var ladder1 = SlippageModel.SellLadder(0, 1.00).ToArray();
        ladder1.Should().BeEmpty();

        var ladder2 = SlippageModel.SellLadder(1.00, 0.90).ToArray(); // inverted
        ladder2.Should().BeEmpty();

        var price = SlippageModel.ApplySlippage(0, 1.00, 1);
        price.Should().Be(0); // bid returned when invalid
    }
}