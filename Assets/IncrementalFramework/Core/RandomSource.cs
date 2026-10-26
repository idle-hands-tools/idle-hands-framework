using System;

namespace IncrementalFramework
{
    public interface IRandomSource
    {
        string Algorithm { get; }
        ulong State { get; }
        ulong NextUInt64();
    }

    /// <summary>
    /// xorshift64 (13, 7, 17) using unsigned 64-bit shifts/XOR, independent of .NET runtime
    /// and System.Random. Zero seeds normalize to one. Snapshots persist the algorithm ID
    /// and complete 64-bit state; injected instances belong to exactly one simulation.
    /// </summary>
    public sealed class XorShift64 : IRandomSource
    {
        public const string AlgorithmId = "xorshift64-13-7-17";
        public string Algorithm => AlgorithmId;
        public ulong State { get; private set; }
        public XorShift64(ulong seed)
        { State = seed == 0 ? 1 : seed; }
        public ulong NextUInt64()
        {
            ulong value = State;
            value ^= value << 13; value ^= value >> 7; value ^= value << 17;
            State = value; return value;
        }
    }
}
