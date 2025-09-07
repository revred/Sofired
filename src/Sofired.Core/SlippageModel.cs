using System;
using System.Collections.Generic;
using System.Linq;

namespace Sofired.Core;

public static class SlippageModel
{
    /// <summary>
    /// Conservative price ladder for selling options:
    /// Try mid, then mid - 1 tick, then mid - 10% of width.
    /// Returns a non-increasing sequence of target prices.
    /// </summary>
    public static IEnumerable<double> SellLadder(double bid, double ask, double tick = 0.01)
    {
        if (bid <= 0 || ask <= 0 || ask < bid) yield break;
        var mid = (bid + ask) / 2.0;
        var width = ask - bid;
        var step3 = Math.Max(bid, mid - 0.10 * width);
        var step2 = Math.Max(step3, mid - tick); // ensure non-increasing
        var step1 = Math.Max(step2, mid);
        yield return step1;
        yield return step2;
        yield return step3;
    }

    /// <summary>
    /// Apply slippage to a target price for conservative execution
    /// </summary>
    public static double ApplySlippage(double bid, double ask, int attemptNumber = 1)
    {
        var ladder = SellLadder(bid, ask).ToArray();
        if (attemptNumber <= 0 || attemptNumber > ladder.Length) return bid;
        return ladder[attemptNumber - 1];
    }

    /// <summary>
    /// Calculate realistic execution price based on market conditions
    /// </summary>
    public static double GetRealisticFillPrice(double bid, double ask, double requestedPrice)
    {
        var mid = (bid + ask) / 2.0;
        var ladder = SellLadder(bid, ask).ToArray();
        
        // Find the most aggressive price we can get that's still realistic
        foreach (var price in ladder)
        {
            if (requestedPrice >= price) return price;
        }
        
        return bid; // Worst case - fill at bid
    }
}