using System;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.UnitTests.Services
{
    [TestFixture]
    public sealed class EmailAgePolicyTests
    {
        private static DateTimeOffset CurrentTime => new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        [Test]
        public void GivenARecentCheckpoint_WhenGettingTheCutoff_ThenTheCheckpointIsUsed()
        {
            PollingSettings settings = new() { MaximumEmailAgeDays = 30 };
            DateTimeOffset checkpoint = CurrentTime.AddDays(-2);

            Assert.That(
                EmailAgePolicy.GetCutoff(settings, CurrentTime, checkpoint),
                Is.EqualTo(checkpoint));
        }

        [Test]
        public void GivenAnOlderCheckpoint_WhenGettingTheCutoff_ThenTheConfiguredAgeIsUsed()
        {
            PollingSettings settings = new() { MaximumEmailAgeDays = 30 };
            DateTimeOffset checkpoint = CurrentTime.AddDays(-31);

            Assert.That(
                EmailAgePolicy.GetCutoff(settings, CurrentTime, checkpoint),
                Is.EqualTo(CurrentTime.AddDays(-30)));
        }

        [Test]
        public void GivenAnInitialDateLaterThanTheCheckpoint_WhenGettingTheCutoff_ThenTheInitialDateIsUsed()
        {
            PollingSettings settings = new()
            {
                MaximumEmailAgeDays = 30,
                InitialProcessingDate = CurrentTime.AddDays(-5)
            };

            Assert.That(
                EmailAgePolicy.GetCutoff(settings, CurrentTime, CurrentTime.AddDays(-10)),
                Is.EqualTo(CurrentTime.AddDays(-5)));
        }
    }
}