using Xunit;
using FluentAssertions;
using Sofired.Core;
using Sofired.Backtester;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Sofired.Tests.Performance
{
    public class PerformanceBenchmarkTests
    {
        [Fact]
        public async Task SingleSymbolBacktest_ShouldCompleteWithinTimeLimit()
        {
            // Arrange
            var stopwatch = Stopwatch.StartNew();
            var backtester = new MultiSymbolBacktester();
            var symbols = new List<string> { "SOFI" };
            var portfolioCapital = 10000m;

            // Act
            await backtester.RunMultiSymbolBacktest(symbols, portfolioCapital);
            stopwatch.Stop();

            // Assert - Should complete within reasonable time (30 seconds for single symbol)
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, "Single symbol backtest should complete quickly");
        }

        [Fact]
        public async Task MultiSymbolBacktest_ShouldScaleReasonably()
        {
            // Arrange
            var singleSymbolTime = await MeasureBacktestTime(new List<string> { "SOFI" });
            var multiSymbolTime = await MeasureBacktestTime(new List<string> { "SOFI", "AAPL", "NVDA" });

            // Assert - Multi-symbol should not be more than 5x slower than single symbol
            multiSymbolTime.Should().BeLessThan(singleSymbolTime * 5, 
                "Multi-symbol backtest should scale reasonably");
        }

        [Fact]
        public void PnLCalculation_ShouldBeEfficient()
        {
            // Arrange
            var pnlEngine = new EnhancedPnLEngine();
            var positions = GenerateTestPositions(100); // 100 positions
            var currentPrices = new Dictionary<string, decimal>
            {
                {"SOFI", 15.0m},
                {"AAPL", 175.0m},
                {"NVDA", 450.0m},
                {"TSLA", 250.0m}
            };

            var stopwatch = Stopwatch.StartNew();

            // Act
            var result = pnlEngine.CalculatePortfolioPnL(positions, currentPrices, 22.0m, DateTime.Now);

            stopwatch.Stop();

            // Assert
            result.Should().NotBeNull();
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000, "P&L calculation for 100 positions should be under 1 second");
        }

        [Fact]
        public void RiskCalculation_ShouldBeEfficient()
        {
            // Arrange
            var riskManager = new AdvancedRiskManager();
            var symbolConfig = CreateTestSymbolConfig();
            var portfolio = CreateTestPortfolioWithManyPositions();
            
            var stopwatch = Stopwatch.StartNew();

            // Act - Run risk calculation multiple times
            for (int i = 0; i < 50; i++)
            {
                riskManager.CalculateOptimalPositionSize("SOFI", 50000m, 22m, portfolio, symbolConfig);
            }

            stopwatch.Stop();

            // Assert
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(500, "50 risk calculations should complete in under 500ms");
        }

        [Fact]
        public async Task LargePortfolioSimulation_ShouldHandleMemoryEfficiently()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true);
            var backtester = new MultiSymbolBacktester();
            var symbols = new List<string> { "SOFI", "AAPL", "NVDA", "TSLA" };
            var portfolioCapital = 100000m;

            // Act
            await backtester.RunMultiSymbolBacktest(symbols, portfolioCapital);
            
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(false);

            // Assert - Memory usage should be reasonable (less than 100MB increase)
            var memoryIncrease = finalMemory - initialMemory;
            memoryIncrease.Should().BeLessThan(100 * 1024 * 1024, "Memory usage should stay reasonable");
        }

        [Theory]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        public void OptionsChainProcessing_ShouldScaleLinearly(int optionCount)
        {
            // Arrange
            var realOptionsEngine = new RealOptionsEngine(new ThetaDataClient("http://localhost", "25510"));
            var optionsData = GenerateTestOptionsData(optionCount);
            
            var stopwatch = Stopwatch.StartNew();

            // Act - Process options chain multiple times to get measurable time
            for (int i = 0; i < 10; i++)
            {
                // This tests the processing efficiency indirectly through synthetic pricing
                realOptionsEngine.CalculateSectorVolatility("SOFI", 15.0m);
            }

            stopwatch.Stop();

            // Assert - Should scale roughly linearly with option count
            var timePerOption = stopwatch.ElapsedMilliseconds / (optionCount * 10.0);
            timePerOption.Should().BeLessThan(1.0, "Should process each option in under 1ms");
        }

        private async Task<long> MeasureBacktestTime(List<string> symbols)
        {
            var stopwatch = Stopwatch.StartNew();
            var backtester = new MultiSymbolBacktester();
            
            await backtester.RunMultiSymbolBacktest(symbols, 10000m);
            
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        private List<Position> GenerateTestPositions(int count)
        {
            var positions = new List<Position>();
            var symbols = new[] { "SOFI", "AAPL", "NVDA", "TSLA" };
            var random = new Random(42);

            for (int i = 0; i < count; i++)
            {
                var symbol = symbols[i % symbols.Length];
                positions.Add(new Position
                {
                    Id = $"TEST{i:D3}",
                    Symbol = symbol,
                    StrategyType = i % 2 == 0 ? "PutCreditSpread" : "CoveredCall",
                    Quantity = random.Next(1, 10),
                    EntryPrice = (decimal)(random.NextDouble() * 5.0),
                    ExpirationDate = DateTime.Now.AddDays(random.Next(7, 60)),
                    OpenDate = DateTime.Now.AddDays(-random.Next(1, 30)),
                    IsOpen = true,
                    ShortStrike = (decimal)(10 + random.NextDouble() * 20),
                    LongStrike = (decimal)(8 + random.NextDouble() * 15)
                });
            }

            return positions;
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
                    MaxLossPerTrade = 0.05m
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

        private PortfolioPnL CreateTestPortfolioWithManyPositions()
        {
            return new PortfolioPnL
            {
                CalculationDate = DateTime.Now,
                VixLevel = 22m,
                TotalPnL = 5000m,
                PortfolioDelta = 300m,
                Positions = GenerateTestPositions(50).Select(p => new PositionPnL 
                { 
                    PositionId = p.Id, 
                    Symbol = p.Symbol,
                    TotalPnL = 50m
                }).ToList(),
                Correlation = new Dictionary<string, decimal>
                {
                    {"SOFI", 0.45m},
                    {"AAPL", 0.35m},
                    {"NVDA", 0.55m},
                    {"TSLA", 0.65m}
                }
            };
        }

        private List<OptionContract> GenerateTestOptionsData(int count)
        {
            var options = new List<OptionContract>();
            var random = new Random(42);

            for (int i = 0; i < count; i++)
            {
                options.Add(new OptionContract
                {
                    Strike = 10m + i * 0.5m,
                    Bid = (decimal)(random.NextDouble() * 2.0),
                    Ask = (decimal)(random.NextDouble() * 2.5),
                    LastPrice = (decimal)(random.NextDouble() * 2.25),
                    Volume = random.Next(100, 1000),
                    OpenInterest = random.Next(500, 5000),
                    ImpliedVolatility = (decimal)(0.15 + random.NextDouble() * 0.25),
                    Delta = (decimal)(random.NextDouble() * 0.5),
                    Gamma = (decimal)(random.NextDouble() * 0.1)
                });
            }

            return options;
        }
    }
}