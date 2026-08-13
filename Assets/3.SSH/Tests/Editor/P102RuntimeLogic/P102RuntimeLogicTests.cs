using NUnit.Framework;
using VRProject.LeakResponse.Logic;

namespace VRProject.LeakResponse.Tests
{
    public sealed class P102RuntimeLogicTests
    {
        [Test]
        public void BootstrapConfigurationGate_AllowsOnlyOneEntry()
        {
            var gate = new SingleExecutionGate();

            Assert.That(gate.TryEnter(), Is.True);
            Assert.That(gate.TryEnter(), Is.False);
            Assert.That(gate.HasEntered, Is.True);
        }

        [Test]
        public void GazeAtTwoPointNineSeconds_IsIncomplete()
        {
            var timer = new ContinuousGazeTimer(3f);
            Assert.That(timer.Tick(true, 2.9f), Is.False);
            Assert.That(timer.IsComplete, Is.False);
        }

        [Test]
        public void GazeAtThreeSeconds_CompletesOnce()
        {
            var timer = new ContinuousGazeTimer(3f);
            Assert.That(timer.Tick(true, 3f), Is.True);
            Assert.That(timer.Tick(true, 1f), Is.False);
            Assert.That(timer.IsComplete, Is.True);
        }

        [Test]
        public void GazeExit_ResetsContinuousTime()
        {
            var timer = new ContinuousGazeTimer(3f);
            timer.Tick(true, 2.9f);
            timer.Tick(false, 0.01f);
            Assert.That(timer.ElapsedSeconds, Is.EqualTo(0f));
            Assert.That(timer.Tick(true, 0.2f), Is.False);
        }

        [Test]
        public void SevenUniqueDeposits_AreRequiredAndDuplicatesDoNotCount()
        {
            var gate = new UniqueDepositGate(7);
            for (int id = 1; id <= 6; ++id)
                Assert.That(gate.TryDeposit(id), Is.True);

            Assert.That(gate.TryDeposit(6), Is.False);
            Assert.That(gate.DepositedCount, Is.EqualTo(6));
            Assert.That(gate.TryLock(), Is.False);
            Assert.That(gate.TryDeposit(7), Is.True);
            Assert.That(gate.TryLock(), Is.True);
            Assert.That(gate.IsLocked, Is.True);
            Assert.That(gate.TryDeposit(8), Is.False);
            Assert.That(gate.TryLock(), Is.False);
        }
    }
}
