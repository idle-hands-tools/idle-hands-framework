using BreakInfinity;
using IncrementalFramework.Core;

namespace IncrementalFramework.Economy
{
    public sealed class BigDoubleNumeric : INumeric<BigDouble>
    {
        public static void Register() { Numeric<BigDouble>.Operations = new BigDoubleNumeric(); }
        public BigDouble Zero => BigDouble.Zero;
        public BigDouble One => BigDouble.One;
        public BigDouble Add(BigDouble a, BigDouble b) => Number.AddSigned(a,b);
        public BigDouble Multiply(BigDouble a, BigDouble b) => a*b;
        public BigDouble Negate(BigDouble value) => -value;
        public int Compare(BigDouble a, BigDouble b) => a.CompareTo(b);
    }
}
