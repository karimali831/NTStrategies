// Standalone pure price guard tests; no NinjaTrader assemblies required.
// Compile together with src/Shared/NinjexOpeningBracketMath.cs on a .NET C# compiler.
using System;
using NinjaTrader.NinjaScript.Ninjex;

internal static class OpeningBracketMathTests
{
    static void Check(bool expected, bool value, string label)
    {
        if (value != expected) throw new Exception("FAIL " + label);
    }

    public static void Main()
    {
        double target;
        string reason;

        // A short with a stale buy-stop at/below market MUST flatten, never submit stop.
        Check(false, NinjexOpeningBracketMath.TryBracket(-1, 6892, 6891.5, 6892,
            0.25, 2, out target, out reason), "short fill crossed candle stop");
        Check(false, NinjexOpeningBracketMath.TryBracket(-1, 6890, 6891.5, 6892,
            0.25, 2, out target, out reason), "short current ask crossed candle stop");

        Check(true, NinjexOpeningBracketMath.TryBracket(-1, 6888.5, 6891.5, 6888.5,
            0.25, 2, out target, out reason), "short bracket good");
        if (Math.Abs(target - 6882.5) > 0.00001) throw new Exception("Short 2R target wrong");

        Check(true, NinjexOpeningBracketMath.TryBracket(+1, 6002, 6000, 6002,
            0.25, 2, out target, out reason), "long bracket good");
        if (Math.Abs(target - 6006) > 0.00001) throw new Exception("Long 2R target wrong");

        Check(false, NinjexOpeningBracketMath.TryBracket(+1, 6002, 6000, 5999.5,
            0.25, 2, out target, out reason), "long current bid crossed candle stop");
        Check(false, NinjexOpeningBracketMath.TryBracket(+1, 6000, 6000, 6000,
            0.25, 2, out target, out reason), "long fill at candle stop");
        Console.WriteLine("PASS: strict candle stop legality, adverse gap handling and 2R target.");
    }
}
