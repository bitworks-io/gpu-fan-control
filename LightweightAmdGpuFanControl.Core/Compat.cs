// netstandard2.0 / .NET Framework 4.8 compatibility shims.
//
// These let the shared Core library (and the net48 app that references it) use modern C#
// (records, init-only members) and a couple of BCL helpers that only exist on .NET 5+,
// without pulling a third-party polyfill package into a publicly distributed binary.

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Marker type required by the compiler to emit <c>init</c> accessors and positional
    /// records on frameworks whose BCL predates it (netstandard2.0, .NET Framework 4.8).
    /// Internal so each assembly carries its own copy without cross-assembly conflicts.
    /// </summary>
    internal static class IsExternalInit { }
}

namespace LightweightAmdGpuFanControl
{
    /// <summary>
    /// <c>Math.Clamp</c> was added in .NET Core 2.0 / .NET Standard 2.1 and is absent from
    /// netstandard2.0 and .NET Framework 4.8. This provides the same behaviour for the int and
    /// double values this project clamps. Public so the net48 app can share the one definition.
    /// </summary>
    public static class MathCompat
    {
        public static int Clamp(int value, int min, int max)
        {
            if (min > max) throw new ArgumentException($"min ({min}) cannot be greater than max ({max}).");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static double Clamp(double value, double min, double max)
        {
            if (min > max) throw new ArgumentException($"min ({min}) cannot be greater than max ({max}).");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
