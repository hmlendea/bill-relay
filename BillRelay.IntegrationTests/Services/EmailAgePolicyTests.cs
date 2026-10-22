using System;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services;

namespace BillRelay.IntegrationTests.Services
{
    [TestFixture]
    public sealed class EmailAgePolicyTests
    {
        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public void GivenAnArrivalTime_WhenCheckingTheAgeBoundary_ThenTheExactCutoffIsEnforced(
            int secondsFromCutoff,
            bool isExpectedEligible)
        {
            DateTimeOffset cutoff = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

            Assert.That(
                EmailAgePolicy.IsEligible(cutoff.AddSeconds(secondsFromCutoff), cutoff),
                Is.EqualTo(isExpectedEligible));
        }

        [Test]
        public void GivenAnEmailReceivedDuringDowntime_WhenWithinTheWindow_ThenItRemainsEligible()
        {
            DateTimeOffset currentTime = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
            PollingSettings settings = new() { MaximumEmailAgeDays = 7 };
            DateTimeOffset cutoff = EmailAgePolicy.GetCutoff(settings, currentTime);

            Assert.That(cutoff, Is.EqualTo(currentTime.AddDays(-7)));
            Assert.That(EmailAgePolicy.IsEligible(currentTime.AddDays(-2), cutoff));
            Assert.That(EmailAgePolicy.IsEligible(currentTime.AddDays(-8), cutoff), Is.False);
            Assert.That(EmailAgePolicy.IsEligible(null, cutoff), Is.False);
        }

        [TestCase(-10, -7)]
        [TestCase(-2, -2)]
        public void GivenAnInitialDate_WhenCalculatingTheCutoff_ThenTheStricterLimitWins(
            int initialDaysOffset,
            int expectedDaysOffset)
        {
            DateTimeOffset currentTime = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
            PollingSettings settings = new()
            {
                MaximumEmailAgeDays = 7,
                InitialProcessingDate = currentTime.AddDays(initialDaysOffset)
            };

            Assert.That(
                EmailAgePolicy.GetCutoff(settings, currentTime),
                Is.EqualTo(currentTime.AddDays(expectedDaysOffset)));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void GivenAnInvalidAge_WhenCalculatingTheCutoff_ThenItIsRejected(int maximumAgeDays)
        {
            PollingSettings settings = new() { MaximumEmailAgeDays = maximumAgeDays };

            Assert.That(
                () => EmailAgePolicy.GetCutoff(settings, DateTimeOffset.UtcNow),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}