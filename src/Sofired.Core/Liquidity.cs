namespace Sofired.Core;

public static class Liquidity
{
    /// <summary>
    /// Check if bid/ask spread is reasonable relative to mid price
    /// </summary>
    /// <param name="bid">Bid price</param>
    /// <param name="ask">Ask price</param>
    /// <param name="maxSpreadPct">Maximum allowed spread as percentage of mid price</param>
    /// <returns>True if spread is acceptable</returns>
    public static bool Ok(double bid, double ask, double maxSpreadPct = 0.12)
    {
        if (bid <= 0 || ask <= 0 || ask <= bid) return false;
        
        var mid = (bid + ask) / 2.0;
        var spread = ask - bid;
        var spreadPct = spread / mid;
        
        return spreadPct <= maxSpreadPct;
    }
    
    /// <summary>
    /// Calculate spread percentage
    /// </summary>
    public static double SpreadPercentage(double bid, double ask)
    {
        if (bid <= 0 || ask <= 0 || ask <= bid) return double.MaxValue;
        var mid = (bid + ask) / 2.0;
        return (ask - bid) / mid;
    }
}