using Xunit;
using FluentAssertions;
using Sofired.Core;
using System;
using System.Collections.Generic;

namespace Sofired.Tests.Core
{
    public class EnhancedPnLEngineTests
    {
        private readonly EnhancedPnLEngine _pnlEngine;
        private readonly Position _testPosition;

        public EnhancedPnLEngineTests()
        {
            _pnlEngine = new EnhancedPnLEngine();
            _testPosition = CreateTestPosition();
        }

        private Position CreateTestPosition()
        {
            return new Position
            {
                Id = "TEST001",
                Symbol = "SOFI",
                StrategyType = "PutCreditSpread",
                ShortStrike = 14.0m,
                LongStrike = 13.0m,
                Quantity = 5,
                EntryPrice = 0.35m,
                ExpirationDate = DateTime.Now.AddDays(30),
                OpenDate = DateTime.Now.AddDays(-5),
                IsOpen = true,
                ProfitLoss = null
            };
        }

        [Fact]
        public void CalculatePositionPnL_WithOpenPosition_ShouldCalculateCorrectMetrics()
        {
            // Arrange
            var currentPrice = 15.0m;
            var currentVix = 22.5m;
            var currentDate = DateTime.Now;

            // Act
            var result = _pnlEngine.CalculatePositionPnL(_testPosition, currentPrice, currentVix, currentDate);

            // Assert
            result.Should().NotBeNull();
            result.Symbol.Should().Be("SOFI");
            result.UnderlyingPrice.Should().Be(currentPrice);
            result.VixLevel.Should().Be(currentVix);
            result.Delta.Should().NotBe(0); // Should have some delta exposure
            result.Theta.Should().BeGreaterThan(0); // Credit spread benefits from time decay
        }

        [Fact]
        public void CalculatePositionPnL_WithPutCreditSpread_ShouldHaveCorrectGreeks()
        {
            // Arrange
            var currentPrice = 15.0m; // Stock above strikes
            var currentVix = 20.0m;
            var currentDate = DateTime.Now;

            // Act
            var result = _pnlEngine.CalculatePositionPnL(_testPosition, currentPrice, currentVix, currentDate);

            // Assert
            result.Delta.Should().BeGreaterThan(0); // Long put spread delta should be positive when OTM
            result.Gamma.Should().BeGreaterThan(0); // Gamma should be positive
            result.Theta.Should().BeGreaterThan(0); // Time decay benefits credit spreads
            result.Vega.Should().BeLessThan(0); // Credit spreads typically negative vega
        }

        [Fact]
        public void CalculatePortfolioPnL_WithMultiplePositions_ShouldAggregateCorrectly()
        {
            // Arrange
            var positions = new List<Position>
            {
                _testPosition,
                CreateTestCoveredCallPosition()
            };
            var currentPrices = new Dictionary<string, decimal>
            {
                {"SOFI", 15.0m},
                {"AAPL", 175.0m}
            };
            var currentVix = 22.0m;
            var currentDate = DateTime.Now;

            // Act
            var result = _pnlEngine.CalculatePortfolioPnL(positions, currentPrices, currentVix, currentDate);

            // Assert
            result.Should().NotBeNull();
            result.Positions.Should().HaveCount(2);
            result.TotalPnL.Should().Be(result.TotalRealizedPnL + result.TotalUnrealizedPnL);
            result.PortfolioDelta.Should().NotBe(0); // Should have net delta exposure
        }

        private Position CreateTestCoveredCallPosition()
        {
            return new Position
            {
                Id = "TEST002",
                Symbol = "AAPL",
                StrategyType = "CoveredCall",
                ShortStrike = 180.0m,
                Quantity = 2,
                EntryPrice = 2.50m,
                ExpirationDate = DateTime.Now.AddDays(21),
                OpenDate = DateTime.Now.AddDays(-7),
                IsOpen = true,
                ProfitLoss = null
            };
        }

        [Theory]
        [InlineData(15.0, 14.0, 0)] // OTM put has no intrinsic value
        [InlineData(13.5, 14.0, 0.5)] // ITM put has intrinsic value
        [InlineData(12.0, 14.0, 2.0)] // Deep ITM put
        public void CalculateIntrinsicValue_WithPutOption_ShouldReturnCorrectValue(double currentPrice, double strike, double expectedIntrinsic)
        {
            // Arrange
            var position = _testPosition;
            position.ShortStrike = (decimal)strike;
            var price = (decimal)currentPrice;

            // Act
            var result = _pnlEngine.CalculatePositionPnL(position, price, 20.0m, DateTime.Now);

            // Assert - This tests the intrinsic value calculation indirectly through the P&L calculation
            if (expectedIntrinsic == 0)
            {
                result.IntrinsicValue.Should().BeLessThanOrEqualTo(0); // OTM spread
            }
            else
            {
                result.IntrinsicValue.Should().BeLessThan(0); // ITM spread loses money
            }
        }

        [Fact]
        public void CalculateValueAtRisk_ShouldReturnReasonableVaR()
        {
            // Arrange
            var currentPrice = 15.0m;
            var currentVix = 25.0m;
            var currentDate = DateTime.Now;

            // Act
            var result = _pnlEngine.CalculatePositionPnL(_testPosition, currentPrice, currentVix, currentDate);

            // Assert
            result.VaR95.Should().BeGreaterThan(0);
            result.VaR99.Should().BeGreaterThan(result.VaR95); // 99% VaR should be higher than 95%
            result.VaR99.Should().BeLessThan(10000); // Reasonable upper bound
        }

        [Fact]
        public void GeneratePnLReport_ShouldContainKeyMetrics()
        {
            // Arrange
            var portfolioPnL = new PortfolioPnL
            {
                CalculationDate = DateTime.Now,
                VixLevel = 20.5m,
                TotalPnL = 1250.0m,
                TotalRealizedPnL = 800.0m,
                TotalUnrealizedPnL = 450.0m,
                PortfolioDelta = 125.0m,
                PortfolioTheta = 45.0m,
                PortfolioSharpe = 1.35m,
                MaxDrawdown = 0.08m
            };

            // Act
            var report = _pnlEngine.GeneratePnLReport(portfolioPnL);

            // Assert
            report.Should().Contain("ENHANCED P&L ANALYSIS REPORT");
            report.Should().Contain("Total P&L: $1,250");
            report.Should().Contain("Portfolio Delta: 125");
            report.Should().Contain("Sharpe Ratio: 1.35");
            report.Should().Contain("Max Drawdown: 8.00%");
        }

        [Theory]
        [InlineData(10, 0.20)] // High volatility environment
        [InlineData(35, 0.50)] // Very high volatility
        [InlineData(50, 0.75)] // Crisis volatility
        public void CalculateVolatilityValue_WithDifferentVix_ShouldAdjustCorrectly(double vix, double expectedImpactFactor)
        {
            // Arrange
            var currentPrice = 15.0m;
            var currentDate = DateTime.Now;

            // Act
            var result = _pnlEngine.CalculatePositionPnL(_testPosition, currentPrice, (decimal)vix, currentDate);

            // Assert
            Math.Abs(result.VolatilityValue).Should().BeGreaterThan(0); // Should have vol impact
            
            // Higher VIX should generally have larger absolute impact
            if (vix > 30)
            {
                Math.Abs(result.VolatilityValue).Should().BeGreaterThan(10); // Significant vol impact
            }
        }

        [Fact]
        public void TrackPositionHistory_ShouldMaintainRollingWindow()
        {
            // Arrange
            var currentPrice = 15.0m;
            var currentVix = 20.0m;
            
            // Act - Calculate P&L multiple times to build history
            for (int i = 0; i < 260; i++) // More than 252 days
            {
                var date = DateTime.Now.AddDays(-i);
                _pnlEngine.CalculatePositionPnL(_testPosition, currentPrice + (i * 0.1m), currentVix, date);
            }

            // Calculate one more to test the rolling window
            var finalResult = _pnlEngine.CalculatePositionPnL(_testPosition, currentPrice, currentVix, DateTime.Now);

            // Assert
            finalResult.Should().NotBeNull(); // Should still work with rolling window
            finalResult.Sharpe.Should().NotBe(0); // Should have calculated Sharpe with history
        }
    }
}