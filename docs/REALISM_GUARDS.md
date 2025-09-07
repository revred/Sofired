+# Sofired — Realism Guards (Anti-Fantasy Backtests)
+
+**Purpose:** make sure our backtests and live decisions reflect *tradable reality* — no phantom fills, no over-sized trades in scary regimes, and no earnings-gap roulette. This module adds **code-level guards + tests** to validate assumptions *before* any order is placed and to keep the backtest honest.
+
+## What this patch adds
+
+1. **Core realism checks** (`Sofired.Core`)
+   - `RealityCheck` — 12 assertions run pre-trade: spread width, OI, quote age, venue count, delta-in-band, earnings sizing, VIX inverse scaling applied, daily kill switch, gamma cap, NBBO sanity, macro-event handling, time-window gating.
+   - `SlippageModel` — a simple, conservative fill ladder (mid → mid−tick → mid−10% of width), used in sims & live.
+   - `AssumptionGuards` — tiny helpers that encode the *policy* (e.g., what counts as “inverse scaling”).
+
+2. **Tests** (`Sofired.Core.Tests`)
+   - Unit tests for spread/oi/age/venues rules, delta-band logic, earnings de-risk, VIX inverse scaling, and slippage ladder.
+
+3. **(Optional) CI** — your existing CI will run the tests. If you don’t have CI yet, add a workflow to run `dotnet test`.
+
+## Configuration knobs (defaults shown)
+
+| Setting | Meaning | Default |
+|---|---|---|
+| `MaxSpreadPct` | Max allowed `(ask-bid)/mid` | `0.12` |
+| `MinOpenInterest` | Minimum OI at the short strike | `250` |
+| `MaxQuoteAgeSec` | Staleness threshold of NBBO | `2` |
+| `MinVenueCount` | Number of distinct quoting venues | `2` |
+| `VixBounds` | Regime thresholds for VIX | `<15, >25` |
+| `ScaleLow/Normal/High` | Inverse VIX scaling multipliers | `1.0 / 0.9 / 0.7` |
+| `EarningsSizeCutMin` | Minimum pre-earnings size reduction | `0.30` (−30%) |
+| `DeltaBands` | CC Δ ranges by regime | `Low/Normal 0.10–0.15`, `High 0.08–0.12` |
+| `DailyStopPct` | Kill switch daily loss | `0.01` (−1%) |
+
+> Tune these in your `CONFIG.yaml` or in the call sites that pass thresholds into `RealityCheck`.
+
+## How this is used
+
+Before placing an order, call:
+```csharp
+var ok = RealityCheck.All(
+    bid: q.Bid, ask: q.Ask, oi: q.OpenInterest, quoteAgeSec: q.QuoteAgeSec, venueCount: q.Venues,
+    delta: q.Delta, deltaMin: 0.10, deltaMax: 0.15,
+    vix: macro.VIX, scaleUsed: size.ScaleApplied, scaleExpectedHigh: 0.7,
+    earningsDays: macro.EarningsDays, size: size.Contracts, baselineSize: size.Baseline,
+    dailyLossPct: risk.DailyLossPct, dailyStopPct: 0.01,
+    timeOk: clock.Between(entryStart, entryEnd), nbboSane: q.NbboSane
+);
+if (!ok.Ok) Exceptions.Log(tradeId, ok.Reasons);
+```
+If `ok.Ok == false`, **do not trade**. The Reasons list becomes a machine-parseable trail in `exceptions.csv`.
+
+## Tests included
+Run:
+```bash
+dotnet test tests/Sofired.Core.Tests -c Release
+```
+
+What is covered:
+- Spread too wide → reject
+- OI too low → reject
+- Quote stale or single venue → reject
+- Delta outside band → reject
+- Earnings sizing not reduced → reject
+- VIX scaling not inverse → reject
+- Kill switch breached → reject new entries
+- Slippage ladder produces non-increasing limit prices and stops after 3 tries
+
+## Why this prevents fantasy backtests
+
+- **Liquidity-gated**: no fills through 20–25% spreads or dusty chains with no OI.
+- **Staleness-aware**: quotes older than 2 seconds are treated as stale and blocked.
+- **Inverse risk**: we *reduce* size in high VIX regimes and shrink again into earnings.
+- **Conservative fills**: simulations use mid−tick and mid−10% of width ladders — optimistic backtests are reined in.
+- **Documented exceptions**: every block produces a reason code → zero ambiguity in review.
+
+## Extending realism
+
+- Plug NBBO *age from Theta* + venues count; if unavailable, set `venueCount=1` to force caution.
+- For **assignment realism**, prohibit holding uncovered short calls through earnings; prefer bounded spreads.
+- On **macro days** (CPI/FOMC/NFP), halve size and shift farther OTM; block new shorts after realized vol spikes.
+
+---
+
+## Quick checklist (put it in code review)
+
+- [ ] All orders pass `RealityCheck.All(...)`
+- [ ] Exceptions are written with clear reasons
+- [ ] Slippage ladder invoked in both sim & live execution paths
+- [ ] Regime/earnings adjustments proved in logs (scale, delta, size)
+- [ ] CI green on realism tests
+
+---
+
+**Owner:** Sofired Team • **Status:** Active • **Scope:** Backtester + Live Execution
+
diff --git a/src/Sofired.Core/RealityCheck.cs b/src/Sofired.Core/RealityCheck.cs
new file mode 100644
--- /dev/null
+++ b/src/Sofired.Core/RealityCheck.cs