using IncrementalFramework;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class SchedulerCancellationTests
    {
        [Test]
        public void LazyCancellationCompactionAndSnapshotsNeverResurrectCancelledWork()
        {
            var scheduler = new EventScheduler<int>();
            var tokens = new EventToken[200];
            for (int i = 0; i < tokens.Length; i++) tokens[i] = scheduler.Schedule(100 - i % 100, 0, i);
            for (int i = 0; i < 180; i++)
            {
                Assert.That(scheduler.Cancel(tokens[i]), Is.True);
                Assert.That(scheduler.TryGet(tokens[i], out _), Is.False);
            }
            Assert.That(scheduler.Count, Is.EqualTo(20));
            var restored = new EventScheduler<int>();
            restored.Restore(scheduler.Export(), scheduler.NextSequence);
            Assert.That(restored.Count, Is.EqualTo(20));
            Assert.That(restored.Cancel(tokens[0]), Is.False);
            int count = 0;
            while (restored.TryDequeue(out var entry))
            {
                Assert.That(entry.Payload, Is.GreaterThanOrEqualTo(180));
                count++;
            }
            Assert.That(count, Is.EqualTo(20));
            Assert.That(restored.Count, Is.Zero);
        }
    }
}
