using System;
using Xunit;
using GsPlugin.Models;

namespace GsPlugin.Tests {
    public class GsTimeTests {
        // FormatElapsed tests

        [Fact]
        public void FormatElapsed_LessThan1Minute_ReturnsJustNow() {
            Assert.Equal("just now", GsTime.FormatElapsed(TimeSpan.FromSeconds(30)));
        }

        [Fact]
        public void FormatElapsed_Zero_ReturnsJustNow() {
            Assert.Equal("just now", GsTime.FormatElapsed(TimeSpan.Zero));
        }

        [Fact]
        public void FormatElapsed_1Minute_ReturnsSingular() {
            Assert.Equal("1 minute ago", GsTime.FormatElapsed(TimeSpan.FromMinutes(1)));
        }

        [Fact]
        public void FormatElapsed_MultipleMinutes_ReturnsPlural() {
            Assert.Equal("45 minutes ago", GsTime.FormatElapsed(TimeSpan.FromMinutes(45)));
        }

        [Fact]
        public void FormatElapsed_1Hour_ReturnsSingular() {
            Assert.Equal("1 hour ago", GsTime.FormatElapsed(TimeSpan.FromHours(1)));
        }

        [Fact]
        public void FormatElapsed_MultipleHours_ReturnsPlural() {
            Assert.Equal("5 hours ago", GsTime.FormatElapsed(TimeSpan.FromHours(5)));
        }

        [Fact]
        public void FormatElapsed_1Day_ReturnsSingular() {
            Assert.Equal("1 day ago", GsTime.FormatElapsed(TimeSpan.FromDays(1)));
        }

        [Fact]
        public void FormatElapsed_MultipleDays_ReturnsPlural() {
            Assert.Equal("3 days ago", GsTime.FormatElapsed(TimeSpan.FromDays(3)));
        }

        // FormatRemaining tests

        [Fact]
        public void FormatRemaining_LessThan1Minute_ReturnsLessThanAMinute() {
            Assert.Equal("less than a minute", GsTime.FormatRemaining(TimeSpan.FromSeconds(30)));
        }

        [Fact]
        public void FormatRemaining_Zero_ReturnsLessThanAMinute() {
            Assert.Equal("less than a minute", GsTime.FormatRemaining(TimeSpan.Zero));
        }

        [Fact]
        public void FormatRemaining_1Minute_ReturnsSingular() {
            Assert.Equal("1 minute", GsTime.FormatRemaining(TimeSpan.FromMinutes(1)));
        }

        [Fact]
        public void FormatRemaining_MultipleMinutes_ReturnsPlural() {
            Assert.Equal("45 minutes", GsTime.FormatRemaining(TimeSpan.FromMinutes(45)));
        }

        [Fact]
        public void FormatRemaining_ExactHours_ReturnsHoursOnly() {
            Assert.Equal("2 hours", GsTime.FormatRemaining(TimeSpan.FromHours(2)));
        }

        [Fact]
        public void FormatRemaining_1Hour_ReturnsSingular() {
            Assert.Equal("1 hour", GsTime.FormatRemaining(TimeSpan.FromHours(1)));
        }

        [Fact]
        public void FormatRemaining_HoursAndMinutes_ReturnsBoth() {
            Assert.Equal("1 hour 30 minutes", GsTime.FormatRemaining(new TimeSpan(1, 30, 0)));
        }

        [Fact]
        public void FormatRemaining_1HourAnd1Minute_ReturnsSingularBoth() {
            Assert.Equal("1 hour 1 minute", GsTime.FormatRemaining(new TimeSpan(1, 1, 0)));
        }

        [Fact]
        public void FormatRemaining_MultipleHoursAndMinutes_ReturnsPluralBoth() {
            Assert.Equal("2 hours 15 minutes", GsTime.FormatRemaining(new TimeSpan(2, 15, 0)));
        }
    }
}
