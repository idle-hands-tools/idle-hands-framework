using IncrementalFramework;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class TypedStateTests
    {
        [Test]
        public void TypedWritesAreStagedAndCommittedTogether()
        {
            var store = new DenseStateStore<int>();
            int first = store.Register("first", 1), second = store.Register("second", 2);
            var transaction = new StateTransaction();
            transaction.Set(store, first, 10);
            transaction.Set(store, second, 20);
            transaction.Set(store, first, 11);
            Assert.That(store[first], Is.EqualTo(1));
            Assert.That(transaction.Commit(), Is.True);
            Assert.That(store[first], Is.EqualTo(11));
            Assert.That(store[second], Is.EqualTo(20));
            Assert.That(store.IndexOf("second"), Is.EqualTo(second));
            Assert.That(transaction.Commit(), Is.False);
        }

        [Test]
        public void StaleTransactionDoesNotPartiallyWriteOtherStores()
        {
            var first = new DenseStateStore<int>();
            var second = new DenseStateStore<int>();
            int a = first.Register("a", 1), b = second.Register("b", 2);
            var stale = new StateTransaction();
            stale.Set(first, a, 10); stale.Set(second, b, 20);
            var newer = new StateTransaction();
            newer.Set(second, b, 99);
            Assert.That(newer.Commit(), Is.True);
            Assert.That(stale.Validate(), Is.False);
            Assert.That(stale.Commit(), Is.False);
            Assert.That(first[a], Is.EqualTo(1));
            Assert.That(second[b], Is.EqualTo(99));
        }
    }
}
