using System;
using System.Collections.Generic;

namespace Sofired.Core;

public static class RealityCheck
{
    public record Result(bool Ok, List<string> Reasons);

    /// <summary>
    /// Run core realism assertions. Any failure returns Ok=false and reasons.
    /// Keep this dependency-light so unit tests are trivial.
    /// </summary>
    public static Result All(
        double bid, double ask, int oi, double quoteAgeSec, int venueCount,
        double delta, double deltaMin, double deltaMax,
        double vix, double scaleUsed, double scaleExpectedHigh,
        int earningsDays, int size, int baselineSize,
        double dailyLossPct, double dailyStopPct,
        bool timeOk, bool nbboSane,
        double maxSpreadPct = 0.12, int minOi = 250, double maxQuoteAgeSec = 2.0, int minVenues = 2
    )
    {
        var reasons = new List<string>();

        // 1) NBBO sanity
        if (!nbboSane) reasons.Add("NBBO_CROSSED_OR_LOCKED");

        // 2) Liquidity: bid/ask width vs mid
        if (!Liquidity.Ok(bid, ask, maxSpreadPct)) reasons.Add("SPREAD_TOO_WIDE");

        // 3) OI threshold
        if (oi < minOi) reasons.Add("OPEN_INTEREST_TOO_LOW");

        // 4) Quote age
        if (quoteAgeSec > maxQuoteAgeSec) reasons.Add("QUOTE_TOO_STALE");

        // 5) Venue diversity
        if (venueCount < minVenues) reasons.Add("INSUFFICIENT_VENUES");

        // 6) Delta within regime band
        if (!(delta >= deltaMin && delta <= deltaMax)) reasons.Add("DELTA_OUT_OF_BAND");

        // 7) Earnings: if within 2 trading days, ensure smaller size
        if (earningsDays <= 2)
        {
            if (!(size <= Math.Max(1, (int)Math.Round(baselineSize * 0.7)))) // at least 30% cut
                reasons.Add("EARNINGS_SIZE_NOT_REDUCED");
        }

        // 8) Inverse VIX scaling must be applied in high regimes
        if (vix > 25.0) // high regime
        {
            if (!(scaleUsed <= scaleExpectedHigh + 1e-9))
                reasons.Add("VIX_SCALING_NOT_INVERSE");
        }

        // 9) Daily kill switch
        if (dailyLossPct <= -Math.Abs(dailyStopPct) + 1e-12)
            reasons.Add("DAILY_KILL_SWITCH_BREACHED");

        // 10) Time window gating
        if (!timeOk) reasons.Add("OUTSIDE_EXECUTION_WINDOW");

        return new Result(reasons.Count == 0, reasons);
    }
}