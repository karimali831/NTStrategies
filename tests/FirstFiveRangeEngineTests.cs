// Portable no-NinjaTrader-reference contract tests:
// csc /out:RangeEngineTests.exe src\Shared\NinjexFirstFiveRangeEngine.cs tests\FirstFiveRangeEngineTests.cs
using System;
using NinjaTrader.NinjaScript.Ninjex;

internal static class FirstFiveRangeEngineTests
{
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    public static void Main()
    {
        DateTime day = new DateTime(2026, 10, 16, 9, 30, 0);
        var one = new NinjexFirstFiveRangeEngine();
        for (int i = 1; i <= 5; i++)
        {
            one.Accept(day.AddMinutes(i), 100 + i, 98 - i, 1);
            Check(one.Ready == (i == 5), "1m range cannot be ready before 09:35");
        }
        Check(one.High == 105 && one.Low == 93, "Aggregated 1m high/low incorrect");
        one.Accept(day.AddMinutes(6), 999, 1, 1);
        Check(one.High == 105 && one.Low == 93, "Range changed after 09:35");

        var missing = new NinjexFirstFiveRangeEngine();
        missing.Accept(day.AddMinutes(1), 101, 99, 1);
        missing.Accept(day.AddMinutes(2), 102, 98, 1);
        missing.Accept(day.AddMinutes(4), 104, 96, 1);
        missing.Accept(day.AddMinutes(5), 105, 95, 1);
        Check(!missing.Ready, "Missing 09:33 bar incorrectly produced ready range");

        var five = new NinjexFirstFiveRangeEngine();
        five.Accept(day.AddMinutes(5), 110, 90, 5);
        Check(five.Ready && five.High == 110 && five.Low == 90, "Completed 5m candle not accepted");
        five.Accept(day.AddDays(1).AddMinutes(5), 200, 150, 5);
        Check(five.Ready && five.ActiveDate == day.AddDays(1).Date
            && five.High == 200 && five.Low == 150, "Next ET day did not reset");

        var late = new NinjexFirstFiveRangeEngine();
        late.Accept(day.AddMinutes(7), 150, 50, 1);
        Check(!late.Ready, "Late first bar incorrectly invented range");

        Console.WriteLine("PASS: strict first-five-minute 1m/5m range, missing data, no drift and daily reset");
    }
}