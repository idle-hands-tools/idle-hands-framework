using System;
using BreakInfinity;
using IncrementalFramework;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class NumberPolicyTests
    {
        [TestCase(1.234567890123, 1000L, 9.8765432101234, 2000L)]
        [TestCase(5, long.MinValue, 2, -1L)]
        [TestCase(9, long.MaxValue, 1, 0L)]
        public void GuardedMultiplicationUsesTheActualPortResult(double a, long ae, double b, long be)
        {
            var left = new BigDouble(a, ae);
            var right = new BigDouble(b, be);
            BigDouble expected = left * right;
            BigDouble actual = Number.Multiply(left, right);
            Assert.That(actual.Exponent, Is.EqualTo(expected.Exponent));
            Assert.That(BitConverter.DoubleToInt64Bits(actual.Mantissa),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(expected.Mantissa)));
        }
        [Test]
        public void ExponentOverflowIsRejectedAndUnderflowIsZero()
        {
            Assert.Throws<ArithmeticException>(() => Number.Multiply(new BigDouble(9, long.MaxValue), 2));
            Assert.That(Number.Multiply(new BigDouble(1, long.MinValue), new BigDouble(1, -1)), Is.EqualTo(BigDouble.Zero));
        }
    }
}
