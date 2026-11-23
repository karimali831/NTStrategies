using System;
using NinjaTrader.NinjaScript.Strategies;

// Compile this file together with CompressionHistory.cs using the Framework
// compiler, no NinjaTrader assemblies required:
// csc.exe /nologo /out:CompressionHistory.Tests.exe CompressionHistory.cs CompressionHistory.Tests.cs
internal static class CompressionHistoryTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static NinjexOvernightStructureCompressionHistory.Observation Observe(
        string group, DateTime date, string instrument, double width,
        bool valid = true, bool realtime = false, string instanceId = "default")
    {
        return NinjexOvernightStructureCompressionHistory.Observe(
            group, instanceId, date, instrument, width,
            valid, 20, 35, realtime);
    }

    private static void RunCrossContractTest()
    {
        var group = "Test-CrossContract-" + Guid.NewGuid().ToString("N");
        var first = new DateTime(2024, 9, 1);
        for (var i = 0; i < 20; i++)
            Observe(group, first.AddDays(i), "ES 09-24", 100.0 + i);

        var date = first.AddDays(20);
        var incoming = Observe(group, date, "ES 12-24", 80.0);

        Assert(incoming.AvailableSessions == 20, "Missing old-contract history");
        Assert(incoming.SelectedSessions == 20, "Wrong lookback size");
        Assert(incoming.CrossContractSessions == 20, "Rollover source not recorded");
        Assert(Math.Abs(incoming.ThresholdTicks - 106.0) < 0.001,
            "Nearest-rank 35th percentile is incorrect");
        Assert(incoming.Compressed, "Expected compressed incoming session");

        // Today cannot enter its own percentile, even if this instrument
        // receives a second completed premarket callback for the same date.
        var sameDay = Observe(group, date, "ES 12-24", 80.0);
        Assert(Math.Abs(sameDay.ThresholdTicks - 106.0) < 0.001,
            "Current session leaked into its own percentile");
        Assert(sameDay.AvailableSessions == 20,
            "Duplicate date counted twice");

        var differentGroup = Observe(
            group + "-Independent", date, "ES 12-24", 80.0);
        Assert(differentGroup.SelectedSessions == 0,
            "Independent group inherited previous run history");
    }

    private static void RunConflictTest()
    {
        var group = "Test-Conflict-" + Guid.NewGuid().ToString("N");
        var first = new DateTime(2025, 3, 1);
        for (var i = 0; i < 21; i++)
            Observe(group, first.AddDays(i), "ES 03-25", 100.0 + i);

        var sameDate = first.AddDays(20);
        var conflict = Observe(
            group, sameDate, "ES 06-25", 90.0);

        Assert(conflict.ConflictingCurrentSession,
            "Conflicting rollover data should be quarantined");
        Assert(!conflict.Compressed,
            "Conflicted current date must fail closed");

        var nextDate = first.AddDays(21);
        var next = Observe(group, nextDate, "ES 06-25", 150.0);
        Assert(next.ConflictingPriorSessions == 1,
            "Previous conflicting session not identified");
        Assert(next.AvailableSessions == 20,
            "Conflict date should not be counted in history");
        Assert(next.CrossContractSessions == 20,
            "Prior contract provenance lost after conflict");
    }

    private static void RunFutureAndRewindTest()
    {
        var group = "Test-Rewind-" + Guid.NewGuid().ToString("N");
        var first = new DateTime(2026, 1, 1);

        // Preloaded future samples must never influence an earlier signal.
        Observe(group, first.AddDays(30), "ES 03-26", 5.0);
        for (var i = 0; i < 20; i++)
            Observe(group, first.AddDays(i), "ES 12-25", 100.0 + i);

        var current = Observe(
            group, first.AddDays(20), "ES 03-26", 80.0);
        Assert(current.AvailableSessions == 20,
            "A future-dated width contaminated lookback");
        Assert(Math.Abs(current.ThresholdTicks - 106.0) < 0.001,
            "Future observation changed causal percentile");

        Observe(group, first.AddDays(30), "ES 03-26", 5.0, true, true);
        var rewound = Observe(
            group, first.AddDays(23), "ES 03-26", 100.0, true, true);
        Assert(rewound.RewindDetected,
            "Backward Playback jump should reset future cached records");
        Assert(rewound.SelectedSessions == 20,
            "Earlier valid sessions should survive a rewind");
    }

    private static void RunMixedContractInitializationTest()
    {
        // Reproduces actual 1.0.2 failure: ES 09-26 advanced through
        // September 11 before the ES 12-26 instance delivered its partial
        // September 1 preloading callbacks. This is NOT a Playback rewind.
        var group = "Test-September-2026-Rollover-"
            + Guid.NewGuid().ToString("N");
        var oldInstance = "old-contract-strategy-run";
        var newInstance = "new-contract-strategy-run";

        int[] outgoingDays = { 31 };
        Observe(group, new DateTime(2026, 8, outgoingDays[0]),
            "ES 09-26", 108, true, true, oldInstance);
        int[] septemberDays = { 1, 2, 3, 4, 7, 8, 9, 10, 11 };
        foreach (int day in septemberDays)
        {
            Observe(group, new DateTime(2026, 9, day),
                "ES 09-26", 100 + day, true, true, oldInstance);
        }

        // December contract preloads August 31 and September 1, but its
        // PM ranges are invalid before the September 13 front-month start.
        Observe(group, new DateTime(2026, 8, 31),
            "ES 12-26", 100, false, false, newInstance);
        var partial = Observe(group, new DateTime(2026, 9, 1),
            "ES 12-26", 257, false, true, newInstance);
        Assert(!partial.RewindDetected,
            "Cross-instance historical preload mistaken for a rewind");

        var start = Observe(group, new DateTime(2026, 9, 14),
            "ES 12-26", 96, true, true, newInstance);
        Assert(!start.RewindDetected, "Incoming contract triggered false rewind");
        Assert(start.AvailableSessions == 10,
            "Incoming ES 12-26 did not inherit ten ES 09-26 sessions");
        Assert(start.ConflictingPriorSessions == 0,
            "Valid front-month history was marked as conflicting");

        int[] incomingDays = { 15, 16, 17, 18, 21, 22, 23, 24, 25 };
        foreach (int day in incomingDays)
        {
            Observe(group, new DateTime(2026, 9, day),
                "ES 12-26", 95 + day, true, true, newInstance);
        }

        var ready = Observe(group, new DateTime(2026, 9, 28),
            "ES 12-26", 99, true, true, newInstance);
        Assert(ready.AvailableSessions == 20,
            "September 28 should have twenty preceding PM sessions");
        Assert(ready.SelectedSessions == 20,
            "September 28 ONL compression warmup is unexpectedly incomplete");
        Assert(ready.CrossContractSessions == 10,
            "September 28 compression window should retain 10 ES 09-26 dates");
        Assert(ready.ConflictingPriorSessions == 0,
            "Rollover produced spurious prior-session conflicts");
        Assert(!ready.RewindDetected,
            "Normal ES12 chronological processing falsely detected rewind");
    }

    public static int Main()
    {
        try
        {
            RunCrossContractTest();
            RunConflictTest();
            RunFutureAndRewindTest();
            RunMixedContractInitializationTest();
            Console.WriteLine(
                "Overnight Structure CompressionHistory tests PASSED.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED: " + ex);
            return 1;
        }
    }
}
