using System;

namespace Autoclicker
{
    internal static partial class Verification
    {
        private static double VerifyClickTiming()
        {
            bool down = false;
            int presses = 0, releases = 0;
            var engine = new ClickEngine(delegate
            {
                Check(!down, "A press must follow a release.");
                down = true; presses++;
            }, delegate
            {
                Check(down, "Only an accepted press should be released.");
                down = false; releases++;
            });
            engine.Tick(0, false);
            Check(presses == 0, "Idle must not send input.");
            engine.Start(0, 50, 3000);
            engine.Tick(2999, false);
            Check(presses == 0, "A countdown must not click early.");
            engine.Tick(3000, false);
            engine.Tick(3019, false);
            Check(down && presses == 1 && engine.Count == 1, "The first press must be immediate and remain held for 20 ms.");
            engine.Tick(3020, false);
            Check(!down && releases == 1, "Release must happen on a later tick.");
            engine.Tick(3049, false);
            Check(presses == 1, "The interval is measured between presses.");
            engine.Tick(3050, false);
            engine.Stop();
            engine.Stop();
            Check(!down && !engine.Active && presses == releases, "Stopping mid-press must release exactly once.");
            engine.Tick(9000, false);
            Check(presses == 2, "Stopping cancels pending clicks.");

            engine.Start(10000, 20, 0);
            engine.Tick(10000, false);
            Check(engine.Count == 1 && down, "Restart resets the count and presses immediately.");
            engine.Tick(20000, false);
            engine.Tick(20000, false);
            Check(engine.Count == 1 && !down, "A delayed release must not be followed by a same-tick catch-up press.");
            engine.Tick(20004, false);
            Check(!down, "A delayed release needs a real button-up interval.");
            engine.Tick(20005, false);
            Check(down && engine.Count == 2, "Clicking resumes after the release gap.");
            engine.Tick(20006, true);
            Check(!down && !engine.Active, "Moving onto this app stops and releases before the next click is due.");

            engine.Start(30000, 50, 0, false, 10);
            engine.Tick(30000, false);
            engine.Tick(30010, false);
            Check(engine.TimeLimitReached && !engine.Active && !down && engine.Count == 1,
                "Expiry during a press must release without another press.");
            engine.Start(40000, 50, 0, true, 1000);
            for (int t = 40000; t <= 41000; t++) engine.Tick(t, false);
            Check(!engine.Active && !down && engine.TimeLimitReached, "Variation must respect timed expiry.");
            engine.Start(42000, 50, 100, false, 1000);
            engine.Stop();
            engine.Tick(45000, false);
            Check(!engine.TimeLimitReached && engine.Count == 0, "Manual stop cancels countdown and expiry.");
            engine.Start(46000, 50, 0);
            engine.Tick(46000, false);
            Check(engine.StopsAt == 0 && down, "Disabling the timer allows continuous clicking.");
            engine.Start(47000, 50, 0);
            Check(!down && engine.Count == 0, "Rearming a running engine releases the previous press first.");
            engine.Stop();
            foreach (int invalid in new[] { 0, 19, 2001 })
            {
                bool rejected = false;
                try { engine.Start(0, invalid, 0); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "Invalid intervals must be rejected.");
            }
            bool durationRejected = false;
            try { engine.Start(0, 50, 0, false, -1); } catch (ArgumentOutOfRangeException) { durationRejected = true; }
            Check(durationRejected, "Negative durations must be rejected.");

            var cadence = new ClickEngine(delegate { }, delegate { });
            cadence.Start(0, 50, 0);
            for (int t = 0; t < 10000; t += 16) cadence.Tick(t, false);
            Check(cadence.Count == 200, "Separate press/release phases must preserve 20 clicks/sec at 16 ms timer granularity.");
            cadence.Stop();

            // A game polling button state at ~60 Hz must observe the hold,
            // independent of cursor movement. Back-to-back down/up fails this.
            int sampledPresses = 0;
            bool sampledDown = false;
            down = false;
            var sampled = new ClickEngine(delegate { down = true; }, delegate { down = false; });
            sampled.Start(0, 50, 0);
            for (int t = 0; t < 10000; t++)
            {
                sampled.Tick(t, false);
                if (t % 17 != 0) continue;
                if (down && !sampledDown) sampledPresses++;
                sampledDown = down;
            }
            Check(sampledPresses == sampled.Count && sampled.Count == 200, "A stationary, frame-polled target must observe each press.");
            sampled.Stop();

            long now = 0, previous = -1, minGap = long.MaxValue, maxGap = 0;
            var varied = new ClickEngine(delegate
            {
                if (previous >= 0)
                {
                    long gap = now - previous;
                    minGap = Math.Min(minGap, gap); maxGap = Math.Max(maxGap, gap);
                    Check(gap >= 45 && gap <= 55, "Variation must stay within +/-10 percent.");
                }
                previous = now;
            }, delegate { }, new Random(182).Next);
            varied.Start(0, 50, 0, true);
            for (now = 0; now < 60000; now++) varied.Tick(now, false);
            double average = previous / (double)(varied.Count - 1);
            Check(minGap == 45 && maxGap == 55 && average > 49.5 && average < 50.5, "Variation must preserve its bounds and average cadence.");
            varied.Stop();
            var fixedTiming = new ClickEngine(delegate { }, delegate { }, delegate(int min, int max)
            { throw new Exception("Disabled variation must not request randomness."); });
            fixedTiming.Start(0, 50, 0);
            for (int t = 0; t <= 50; t++) fixedTiming.Tick(t, false);
            Check(fixedTiming.Count == 2, "Fixed timing must retain its cadence.");
            fixedTiming.Stop();
            var coarse = new ClickEngine(delegate { }, delegate { }, new Random(184).Next);
            coarse.Start(0, 50, 0, true);
            for (int t = 0; t < 60000; t += 16) coarse.Tick(t, false);
            Check(coarse.Count >= 1188 && coarse.Count <= 1212, "Varied timing must retain its average on a coarse timer.");
            coarse.Stop();

            var failure = new ClickEngine(delegate { throw new InvalidOperationException(); },
                delegate { throw new Exception("Rejected presses must not send a release."); });
            failure.Start(0, 50, 0);
            try { failure.Tick(0, false); } catch (InvalidOperationException) { }
            Check(!failure.Active && !failure.ButtonDown && failure.Count == 0, "A rejected press must stop without a count or pulse.");
            int attempts = 0;
            var releaseFailure = new ClickEngine(delegate { }, delegate
            { if (++attempts <= 2) throw new InvalidOperationException(); });
            releaseFailure.Start(0, 50, 0);
            releaseFailure.Tick(0, false);
            try { releaseFailure.Tick(20, false); } catch (InvalidOperationException) { }
            Check(!releaseFailure.Active && releaseFailure.ButtonDown && attempts == 2, "Failed release must stop new presses and retain the pending release.");
            releaseFailure.Stop();
            Check(!releaseFailure.ButtonDown && attempts == 3, "A subsequent stop must retry a failed release.");
            return average;
        }
    }
}
