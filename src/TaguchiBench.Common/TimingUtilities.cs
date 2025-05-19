// TaguchiBench.Common/TimingUtilities.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization; // For CultureInfo in FormatElapsedTime, if needed for specific formatting.

namespace TaguchiBench.Common {
    /// <summary>
    /// Provides utilities for measuring and estimating execution times.
    /// Elegance in timekeeping.
    /// </summary>
    public static class TimingUtilities {
        private static readonly Dictionary<string, Stopwatch> _timers = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Starts a named timer. If the timer already exists, it is restarted.
        /// </summary>
        /// <param name="timerName">A unique, descriptive name for the timer.</param>
        public static void StartTimer(string timerName) {
            if (string.IsNullOrWhiteSpace(timerName)) {
                throw new ArgumentException("Timer name cannot be null or whitespace.", nameof(timerName));
            }

            lock (_lock) {
                if (_timers.TryGetValue(timerName, out Stopwatch existingTimer)) {
                    existingTimer.Restart();
                } else {
                    Stopwatch timer = Stopwatch.StartNew();
                    _timers[timerName] = timer;
                }
            }
        }

        /// <summary>
        /// Retrieves the elapsed time for a named timer without stopping it.
        /// </summary>
        /// <param name="timerName">The name of the timer.</param>
        /// <returns>The elapsed time as a <see cref="TimeSpan"/>. Returns <see cref="TimeSpan.Zero"/> if the timer is not found.</returns>
        public static TimeSpan GetElapsedTime(string timerName) {
            if (string.IsNullOrWhiteSpace(timerName)) {
                // Or throw, depending on desired strictness. Returning Zero is perhaps more forgiving.
                return TimeSpan.Zero;
            }

            lock (_lock) {
                if (_timers.TryGetValue(timerName, out Stopwatch timer)) {
                    return timer.Elapsed;
                }
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Stops a named timer and returns its elapsed time.
        /// </summary>
        /// <param name="timerName">The name of the timer.</param>
        /// <returns>The elapsed time as a <see cref="TimeSpan"/>. Returns <see cref="TimeSpan.Zero"/> if the timer is not found or already stopped.</returns>
        public static TimeSpan StopTimer(string timerName) {
            if (string.IsNullOrWhiteSpace(timerName)) {
                return TimeSpan.Zero;
            }

            lock (_lock) {
                if (_timers.TryGetValue(timerName, out Stopwatch timer)) {
                    if (timer.IsRunning) { // Only stop if running
                        timer.Stop();
                    }
                    return timer.Elapsed;
                }
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Formats a <see cref="TimeSpan"/> into a human-readable string, adapting to the magnitude of the duration.
        /// </summary>
        /// <param name="span">The <see cref="TimeSpan"/> to format.</param>
        /// <returns>A string representation of the duration (e.g., "2.34 days", "15.67 hours", "5.89 minutes", "30.12 seconds", "750 ms").</returns>
        public static string FormatElapsedTime(TimeSpan span) {
            if (span.TotalDays >= 1.0) {
                return $"{span.TotalDays.ToString("F2", CultureInfo.InvariantCulture)} days";
            }
            if (span.TotalHours >= 1.0) {
                return $"{span.TotalHours.ToString("F2", CultureInfo.InvariantCulture)} hours";
            }
            if (span.TotalMinutes >= 1.0) {
                return $"{span.TotalMinutes.ToString("F2", CultureInfo.InvariantCulture)} minutes";
            }
            if (span.TotalSeconds >= 1.0) {
                return $"{span.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} seconds";
            }
            return $"{span.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms";
        }

        /// <summary>
        /// Estimates the remaining time and completion datetime for a task given its progress.
        /// </summary>
        /// <param name="elapsed">The time elapsed so far.</param>
        /// <param name="percentComplete">The percentage of the task completed (0.0 to 100.0).</param>
        /// <returns>
        /// A tuple containing the estimated remaining time (<see cref="TimeSpan"/>)
        /// and the estimated completion time (<see cref="DateTime"/>).
        /// Returns (<see cref="TimeSpan.MaxValue"/>, <see cref="DateTime.MaxValue"/>) if percentComplete is zero or less.
        /// Returns (<see cref="TimeSpan.Zero"/>, current <see cref="DateTime"/>) if percentComplete is 100 or more.
        /// </returns>
        public static (TimeSpan remaining, DateTime completionTime) EstimateCompletion(TimeSpan elapsed, double percentComplete) {
            if (percentComplete <= 0.0) {
                return (TimeSpan.MaxValue, DateTime.MaxValue);
            }
            if (percentComplete >= 100.0) {
                return (TimeSpan.Zero, DateTime.Now);
            }

            double totalSecondsElapsed = elapsed.TotalSeconds;
            double estimatedTotalSeconds = totalSecondsElapsed * (100.0 / percentComplete);
            double remainingSeconds = estimatedTotalSeconds - totalSecondsElapsed;

            TimeSpan remainingSpan = TimeSpan.FromSeconds(remainingSeconds);
            DateTime completionTime = DateTime.Now + remainingSpan;

            return (remainingSpan, completionTime);
        }
    }
}