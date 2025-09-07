using Xunit;
using FluentAssertions;
using Moq;
using Sofired.Core;
using System;
using System.Threading.Tasks;

namespace Sofired.Tests.Core
{
    public class RealOptionsEngineTests
    {
        private readonly Mock<ThetaDataClient> _mockThetaClient;
        private readonly RealOptionsEngine _realOptionsEngine;

        public RealOptionsEngineTests()
        {
            _mockThetaClient = new Mock<ThetaDataClient>("http://localhost", "25510");
            _realOptionsEngine = new RealOptionsEngine(_mockThetaClient.Object);
        }

        [Fact]
        public async Task GetPutSpreadPricing_WithValidData_ShouldReturnRealPricing()
        {
            // Arrange
            var symbol = "SOFI";
            var stockPrice = 15.50m;
            var expirationDate = DateTime.Now.AddDays(30);
            var shortStrike = 14.0m;
            var longStrike = 13.0m;
            var tradingDate = DateTime.Now;

            // Mock ThetaData to return some options data
            _mockThetaClient.Setup(x => x.GetOptionsChain(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<OptionData>
                {
                    new OptionData { Strike = 14.0m, Bid = 0.50m, Ask = 0.55m, OptionType = "PUT" },
                    new OptionData { Strike = 13.0m, Bid = 0.25m, Ask = 0.30m, OptionType = "PUT" }
                });

            // Act
            var result = await _realOptionsEngine.GetPutSpreadPricing(symbol, stockPrice, expirationDate, shortStrike, longStrike, tradingDate);

            // Assert
            result.Should().NotBeNull();
            result.IsRealData.Should().BeTrue();
            result.NetPremium.Should().BeGreaterThan(0);
            result.ShortLegPremium.Should().BeGreaterThan(result.LongLegPremium);
        }

        [Fact]
        public async Task GetPutSpreadPricing_WithNoOptionsData_ShouldFallbackToSynthetic()
        {
            // Arrange
            var symbol = "SOFI";
            var stockPrice = 15.50m;
            var expirationDate = DateTime.Now.AddDays(30);
            var shortStrike = 14.0m;
            var longStrike = 13.0m;
            var tradingDate = DateTime.Now;

            // Mock ThetaData to return empty options data
            _mockThetaClient.Setup(x => x.GetOptionsChain(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<OptionData>());

            // Act
            var result = await _realOptionsEngine.GetPutSpreadPricing(symbol, stockPrice, expirationDate, shortStrike, longStrike, tradingDate);

            // Assert
            result.Should().NotBeNull();
            result.IsRealData.Should().BeFalse();
            result.NetPremium.Should().BeGreaterThan(0);
            result.HasMarketFriction.Should().BeTrue();
        }

        [Fact]
        public async Task GetCoveredCallPricing_WithValidData_ShouldReturnRealPricing()
        {
            // Arrange
            var symbol = "AAPL";
            var stockPrice = 175.0m;
            var expirationDate = DateTime.Now.AddDays(21);
            var callStrike = 180.0m;
            var tradingDate = DateTime.Now;

            // Mock ThetaData to return call options data
            _mockThetaClient.Setup(x => x.GetOptionsChain(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<OptionData>
                {
                    new OptionData { Strike = 180.0m, Bid = 2.10m, Ask = 2.20m, OptionType = "CALL" }
                });

            // Act
            var result = await _realOptionsEngine.GetCoveredCallPricing(symbol, stockPrice, expirationDate, callStrike, tradingDate);

            // Assert
            result.Should().NotBeNull();
            result.IsRealData.Should().BeTrue();
            result.CallPremium.Should().BeGreaterThan(0);
            result.CallPremium.Should().BeLessThan(10.0m); // Reasonable range for OTM call
        }

        [Theory]
        [InlineData("SOFI", 12.0, 0.45)] // High volatility fintech
        [InlineData("AAPL", 150.0, 0.25)] // Lower volatility mega cap
        [InlineData("TSLA", 200.0, 0.55)] // Very high volatility EV
        public void CalculateSectorVolatility_ShouldReturnAppropriateVolatility(string symbol, decimal price, decimal expectedMinVol)
        {
            // Act
            var volatility = _realOptionsEngine.CalculateSectorVolatility(symbol, price);

            // Assert
            volatility.Should().BeGreaterThanOrEqualTo(expectedMinVol);
            volatility.Should().BeLessThan(1.0m); // Max 100% volatility
        }

        [Fact]
        public async Task GetHistoricalVolatility_WithValidSymbol_ShouldReturnReasonableVolatility()
        {
            // Arrange
            var symbol = "SOFI";
            var lookbackDays = 30;

            // Mock some price history
            var mockPrices = new List<decimal> { 12.0m, 12.5m, 12.2m, 11.8m, 12.3m };
            _mockThetaClient.Setup(x => x.GetHistoricalPrices(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(mockPrices);

            // Act
            var volatility = await _realOptionsEngine.GetHistoricalVolatility(symbol, lookbackDays);

            // Assert
            volatility.Should().BeGreaterThan(0);
            volatility.Should().BeLessThan(2.0m); // Reasonable max volatility
        }

        [Fact]
        public void ApplyMarketFriction_ShouldReducePremiums()
        {
            // Arrange
            var originalPremium = 1.00m;
            var symbol = "SOFI";

            // Act
            var frictionAdjustedPremium = _realOptionsEngine.ApplyMarketFriction(originalPremium, symbol);

            // Assert
            frictionAdjustedPremium.Should().BeLessThan(originalPremium);
            frictionAdjustedPremium.Should().BeGreaterThan(originalPremium * 0.5m); // At least 50% of original
        }

        [Fact]
        public void CalculateGreeks_WithValidInputs_ShouldReturnReasonableGreeks()
        {
            // Arrange
            var stockPrice = 15.0m;
            var strikePrice = 14.0m;
            var timeToExpiry = 0.08m; // ~30 days
            var riskFreeRate = 0.05m;
            var volatility = 0.35m;

            // Act
            var greeks = _realOptionsEngine.CalculateGreeks(stockPrice, strikePrice, timeToExpiry, riskFreeRate, volatility, "PUT");

            // Assert
            greeks.Should().NotBeNull();
            greeks.Delta.Should().BeLessThan(0); // Put delta should be negative
            greeks.Gamma.Should().BeGreaterThan(0); // Gamma always positive
            greeks.Theta.Should().BeLessThan(0); // Theta typically negative (time decay)
            greeks.Vega.Should().BeGreaterThan(0); // Vega typically positive
        }
    }
}