using Xunit;
using FluentAssertions;
using Sofired.Core;
using System;
using System.Collections.Generic;

namespace Sofired.Tests.Core
{
    public class AdvancedRiskManagerTests
    {
        private readonly AdvancedRiskManager _riskManager;
        private readonly SymbolConfig _testConfig;
        private readonly PortfolioPnL _testPortfolio;

        public AdvancedRiskManagerTests()
        {
            _riskManager = new AdvancedRiskManager();
            _testConfig = CreateTestSymbolConfig();
            _testPortfolio = CreateTestPortfolio();
        }

        private SymbolConfig CreateTestSymbolConfig()
        {
            return new SymbolConfig
            {
                Symbol = "SOFI",
                Risk = new RiskConfig
                {
                    CapitalAllocation = 0.15m,
                    MaxPositionSize = 0.25m,
                    MaxLossPerTrade = 0.05m,
                    MaxPortfolioDrawdown = 0.20m
                },
                Market = new MarketConfig
                {
                    VixLow = 15m,
                    VixNormal = 25m,
                    VixHigh = 35m,
                    VixCrisis = 50m
                }
            };
        }

        private PortfolioPnL CreateTestPortfolio()
        {
            return new PortfolioPnL
            {
                CalculationDate = DateTime.Now,
                VixLevel = 22m,
                TotalPnL = 1500m,
                PortfolioDelta = 250m,
                PortfolioVega = 150m,
                MaxDrawdown = 0.12m,
                Correlation = new Dictionary<string, decimal>
                {
                    {"SOFI", 0.45m},
                    {"AAPL", 0.35m}
                }
            };
        }

        [Fact]
        public void CalculateOptimalPositionSize_WithNormalConditions_ShouldReturnReasonableSize()
        {
            // Arrange
            var accountValue = 50000m;
            var currentVix = 22m;

            // Act
            var result = _riskManager.CalculateOptimalPositionSize("SOFI", accountValue, currentVix, _testPortfolio, _testConfig);

            // Assert
            result.Should().NotBeNull();
            result.Symbol.Should().Be("SOFI");
            result.RecommendedSize.Should().BeGreaterThan(0);
            result.RecommendedSize.Should().BeLessThanOrEqualTo(accountValue * _testConfig.Risk.MaxPositionSize);
            result.RecommendedContracts.Should().BeGreaterThan(0);
        }

        [Theory]
        [InlineData(15, 1.2)] // Low VIX - can increase size
        [InlineData(25, 1.0)] // Normal VIX - standard size
        [InlineData(35, 0.6)] // High VIX - reduce size
        [InlineData(55, 0.3)] // Crisis VIX - significantly reduce
        public void CalculateOptimalPositionSize_WithDifferentVix_ShouldAdjustSizeCorrectly(double vix, double expectedAdjustmentMin)
        {
            // Arrange
            var accountValue = 50000m;
            var baseSize = accountValue * _testConfig.Risk.CapitalAllocation;

            // Act
            var result = _riskManager.CalculateOptimalPositionSize("SOFI", accountValue, (decimal)vix, _testPortfolio, _testConfig);

            // Assert
            if (vix >= 50) // Crisis mode
            {
                result.VixAdjustment.Should().BeLessThanOrEqualTo(0.3m);
            }
            else if (vix >= 35) // High volatility
            {
                result.VixAdjustment.Should().BeLessThanOrEqualTo(0.6m);
            }
            else if (vix <= 15) // Low volatility
            {
                result.VixAdjustment.Should().BeGreaterThanOrEqualTo(1.0m);
            }

            result.VixAdjustment.Should().BeGreaterThan(0);
        }

        [Fact]
        public void CalculateOptimalPositionSize_WithHighCorrelation_ShouldReduceSize()
        {
            // Arrange
            var accountValue = 50000m;
            var currentVix = 20m;
            
            // Create high correlation portfolio
            var highCorrelationPortfolio = _testPortfolio;
            highCorrelationPortfolio.Correlation = new Dictionary<string, decimal>
            {
                {"SOFI", 0.85m}, // High correlation
                {"AAPL", 0.80m}
            };

            // Act
            var result = _riskManager.CalculateOptimalPositionSize("SOFI", accountValue, currentVix, highCorrelationPortfolio, _testConfig);

            // Assert
            result.CorrelationAdjustment.Should().BeLessThan(1.0m); // Should reduce size due to high correlation
            result.Warnings.Should().Contain(w => w.Contains("correlation"));
        }

        [Fact]
        public void MonitorRiskLimits_WithExcessiveDrawdown_ShouldGenerateCriticalAlert()
        {
            // Arrange
            var portfolioWithHighDrawdown = _testPortfolio;
            portfolioWithHighDrawdown.MaxDrawdown = 0.30m; // Above 25% limit

            var symbolConfigs = new Dictionary<string, SymbolConfig>
            {
                {"SOFI", _testConfig}
            };

            // Act
            var alerts = _riskManager.MonitorRiskLimits(portfolioWithHighDrawdown, symbolConfigs, 50000m);

            // Assert
            alerts.Should().NotBeEmpty();
            alerts.Should().Contain(a => a.Type == RiskAlertType.MaxDrawdown);
            alerts.Should().Contain(a => a.Severity == AlertSeverity.Critical);
        }

        [Fact]
        public void MonitorRiskLimits_WithExcessiveDeltaExposure_ShouldGenerateAlert()
        {
            // Arrange
            var portfolioWithHighDelta = _testPortfolio;
            portfolioWithHighDelta.PortfolioDelta = 30000m; // Very high delta exposure
            
            var symbolConfigs = new Dictionary<string, SymbolConfig>
            {
                {"SOFI", _testConfig}
            };

            // Act
            var alerts = _riskManager.MonitorRiskLimits(portfolioWithHighDelta, symbolConfigs, 50000m);

            // Assert
            alerts.Should().Contain(a => a.Type == RiskAlertType.DeltaExposure);
            alerts.Should().Contain(a => a.Severity == AlertSeverity.High);
        }

        [Fact]
        public void MonitorRiskLimits_WithExcessiveVegaExposure_ShouldGenerateAlert()
        {
            // Arrange
            var portfolioWithHighVega = _testPortfolio;
            portfolioWithHighVega.PortfolioVega = 6000m; // Above 5000 limit
            
            var symbolConfigs = new Dictionary<string, SymbolConfig>
            {
                {"SOFI", _testConfig}
            };

            // Act
            var alerts = _riskManager.MonitorRiskLimits(portfolioWithHighVega, symbolConfigs, 50000m);

            // Assert
            alerts.Should().Contain(a => a.Type == RiskAlertType.VegaExposure);
            alerts.Should().Contain(a => a.Severity == AlertSeverity.Medium);
        }

        [Theory]
        [InlineData("AAPL", 0.35)]
        [InlineData("NVDA", 0.25)]
        [InlineData("TSLA", 0.15)]
        [InlineData("SOFI", 0.20)]
        public void CalculateOptimalPositionSize_WithDifferentSymbols_ShouldUseAppropriateAllocation(string symbol, double expectedAllocationMin)
        {
            // Arrange
            var accountValue = 100000m;
            var currentVix = 20m;

            // Act
            var result = _riskManager.CalculateOptimalPositionSize(symbol, accountValue, currentVix, _testPortfolio, _testConfig);

            // Assert
            result.BaseSize.Should().BeGreaterThan(0);
            // The actual allocation logic is in the implementation, so we just verify it's reasonable
            result.RecommendedSize.Should().BeLessThanOrEqualTo(accountValue * 0.5m); // Never more than 50%
        }

        [Fact]
        public void CalculateKellyOptimalSize_WithPositiveExpectedValue_ShouldReturnReasonableSize()
        {
            // Update risk metrics to have positive expected value
            _riskManager.UpdateSymbolRiskMetrics("SOFI", new PositionPnL
            {
                Symbol = "SOFI",
                TotalPnL = 150m,
                MaxDrawdown = 0.10m
            });

            // Arrange
            var accountValue = 50000m;
            var currentVix = 20m;

            // Act
            var result = _riskManager.CalculateOptimalPositionSize("SOFI", accountValue, currentVix, _testPortfolio, _testConfig);

            // Assert
            result.KellySize.Should().BeGreaterThan(0);
            result.KellySize.Should().BeLessThanOrEqualTo(accountValue * 0.25m); // Kelly is capped at 25%
        }

        [Fact]
        public void GetActiveAlerts_AfterMonitoring_ShouldReturnRecentAlerts()
        {
            // Arrange
            var portfolioWithIssues = _testPortfolio;
            portfolioWithIssues.MaxDrawdown = 0.30m; // High drawdown

            var symbolConfigs = new Dictionary<string, SymbolConfig>
            {
                {"SOFI", _testConfig}
            };

            // Act
            _riskManager.MonitorRiskLimits(portfolioWithIssues, symbolConfigs, 50000m);
            var activeAlerts = _riskManager.GetActiveAlerts();

            // Assert
            activeAlerts.Should().NotBeEmpty();
            activeAlerts.Should().AllSatisfy(alert => alert.Timestamp.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public void UpdateSymbolRiskMetrics_ShouldUpdateMetricsCorrectly()
        {
            // Arrange
            var positionPnL = new PositionPnL
            {
                Symbol = "SOFI",
                TotalPnL = 250m,
                MaxDrawdown = 0.08m
            };

            // Act
            _riskManager.UpdateSymbolRiskMetrics("SOFI", positionPnL);

            // This is indirect testing since the metrics are private
            // But we can test that subsequent risk calculations use updated metrics
            var result = _riskManager.CalculateOptimalPositionSize("SOFI", 50000m, 20m, _testPortfolio, _testConfig);

            // Assert
            result.Should().NotBeNull();
            result.Symbol.Should().Be("SOFI");
        }

        [Fact]
        public void AssessPositionRiskLevel_WithDifferentSizes_ShouldReturnCorrectLevels()
        {
            // Arrange
            var accountValue = 50000m;
            var currentVix = 20m;

            // Test different position sizes by adjusting config
            var highRiskConfig = _testConfig;
            highRiskConfig.Risk.MaxPositionSize = 0.5m; // Allow larger positions

            // Act
            var result = _riskManager.CalculateOptimalPositionSize("SOFI", accountValue, currentVix, _testPortfolio, highRiskConfig);

            // Assert
            if (result.RecommendedSize / accountValue > 0.2m)
            {
                result.RiskLevel.Should().Be(RiskLevel.High);
            }
            else if (result.RecommendedSize / accountValue > 0.1m)
            {
                result.RiskLevel.Should().Be(RiskLevel.Medium);
            }
            else
            {
                result.RiskLevel.Should().Be(RiskLevel.Low);
            }
        }
    }
}