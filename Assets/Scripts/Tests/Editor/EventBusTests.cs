using NUnit.Framework;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class EventBusTests
    {
        private struct TestEvent
        {
            public int Value;
        }

        private struct OtherEvent { }

        [Test]
        public void Subscribe_And_Publish_ReceivesEvent()
        {
            var bus = new EventBus();
            int received = -1;
            bus.Subscribe<TestEvent>(e => received = e.Value);

            bus.Publish(new TestEvent { Value = 42 });

            Assert.AreEqual(42, received);
        }

        [Test]
        public void Publish_WithNoSubscribers_DoesNotThrow()
        {
            var bus = new EventBus();

            Assert.DoesNotThrow(() => bus.Publish(new TestEvent { Value = 1 }));
        }

        [Test]
        public void Subscribe_MultipleHandlers_AllReceive()
        {
            var bus = new EventBus();
            int count = 0;
            bus.Subscribe<TestEvent>(e => count++);
            bus.Subscribe<TestEvent>(e => count++);
            bus.Subscribe<TestEvent>(e => count++);

            bus.Publish(new TestEvent { Value = 1 });

            Assert.AreEqual(3, count);
        }

        [Test]
        public void Unsubscribe_StopsReceiving()
        {
            var bus = new EventBus();
            int count = 0;
            void Handler(TestEvent e) => count++;

            bus.Subscribe<TestEvent>(Handler);
            bus.Publish(new TestEvent());
            Assert.AreEqual(1, count);

            bus.Unsubscribe<TestEvent>(Handler);
            bus.Publish(new TestEvent());
            Assert.AreEqual(1, count); // Still 1, not 2
        }

        [Test]
        public void DifferentEventTypes_AreIndependent()
        {
            var bus = new EventBus();
            int testCount = 0;
            int otherCount = 0;
            bus.Subscribe<TestEvent>(e => testCount++);
            bus.Subscribe<OtherEvent>(e => otherCount++);

            bus.Publish(new TestEvent());

            Assert.AreEqual(1, testCount);
            Assert.AreEqual(0, otherCount);
        }

        [Test]
        public void Clear_RemovesAllSubscriptions()
        {
            var bus = new EventBus();
            int count = 0;
            bus.Subscribe<TestEvent>(e => count++);

            bus.Clear();
            bus.Publish(new TestEvent());

            Assert.AreEqual(0, count);
        }
    }
}
