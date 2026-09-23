#if NET8_0_OR_GREATER
namespace AngleSharp.Benchmarks.ConfigurationExperiment
{
    using System;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Threading.Tasks;

    // Attribution runs are separate from BenchmarkDotNet timings. The plain mode measures
    // the original complete operation to expose the overhead of the extra phase observations.
    internal static class ConfigurationPhaseProbe
    {
        internal static void Run(String api, String mode, Int32 repetitions)
        {
            if ((api != "Current" && api != "Builder") ||
                (mode != "instrumented" && mode != "plain") || repetitions <= 0)
            {
                throw new ArgumentException("Expected Current|Builder, instrumented|plain, and a positive repetition count.");
            }

            var builder = api == "Builder";
            var instrumented = mode == "instrumented";

            for (var i = 0; i < 5; i++)
            {
                Measure(builder, instrumented);
            }

            var samples = new Sample[repetitions];

            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = Measure(builder, instrumented);
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Api = api,
                Mode = mode,
                Warmups = 5,
                Repetitions = repetitions,
                Runtime = Environment.Version.ToString(),
                AllocationScope = "process-wide GC.GetTotalAllocatedBytes(true); allocated, not retained bytes",
                Samples = samples,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        internal static Sample Measure(Boolean builder, Boolean instrumented)
        {
            var boundary = instrumented ? new ContextBoundary() : null;
            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var bytes0 = GC.GetTotalAllocatedBytes(true);
            var time0 = Stopwatch.GetTimestamp();
            var time1 = time0;
            var time2 = time0;
            var bytes1 = bytes0;
            var bytes2 = bytes0;
            Int32 checksum;

            if (instrumented)
            {
                var configuration = JintConfigurationScenario.Create(builder);
                time1 = Stopwatch.GetTimestamp();
                bytes1 = GC.GetTotalAllocatedBytes(true);

                checksum = ParseObserved(configuration, boundary).GetAwaiter().GetResult();
                time2 = boundary.Timestamp;
                bytes2 = boundary.Bytes;
            }
            else
            {
                checksum = JintConfigurationScenario.Parse(JintConfigurationScenario.Create(builder)).GetAwaiter().GetResult();
            }

            var time3 = Stopwatch.GetTimestamp();
            var bytes3 = GC.GetTotalAllocatedBytes(true);

            if (checksum != 21)
            {
                throw new InvalidOperationException("Configuration phase checksum failed.");
            }

            var scale = 1_000_000_000.0 / Stopwatch.Frequency;
            return new Sample
            {
                ConstructionNs = instrumented ? (time1 - time0) * scale : null,
                ContextNs = instrumented ? (time2 - time1) * scale : null,
                ParseAndDisposeNs = instrumented ? (time3 - time2) * scale : null,
                TotalNs = (time3 - time0) * scale,
                ConstructionBytes = instrumented ? bytes1 - bytes0 : null,
                ContextBytes = instrumented ? bytes2 - bytes1 : null,
                ParseAndDisposeBytes = instrumented ? bytes3 - bytes2 : null,
                TotalBytes = bytes3 - bytes0,
                Gen0 = GC.CollectionCount(0) - gc0,
                Gen1 = GC.CollectionCount(1) - gc1,
                Gen2 = GC.CollectionCount(2) - gc2,
                Checksum = checksum,
            };
        }

        // Keep the same async operation and result-task shape as the untouched scenario.
        // This observer-bearing copy is only used by the separate attribution process.
        private static async Task<Int32> ParseObserved(IConfiguration configuration, ContextBoundary boundary)
        {
            using (var context = BrowsingContext.New(configuration))
            {
                boundary.Timestamp = Stopwatch.GetTimestamp();
                boundary.Bytes = GC.GetTotalAllocatedBytes(true);

                using (var document = await context.OpenAsync(request => request.Content(JintConfigurationScenario.Markup)).ConfigureAwait(false))
                {
                    return document.QuerySelectorAll("#host > p.item").Length * 10 +
                        (document.GetElementById("gradient").LocalName == "linearGradient" ? 1 : 0);
                }
            }
        }

        private sealed class ContextBoundary
        {
            internal Int64 Timestamp;
            internal Int64 Bytes;
        }

        internal sealed class Sample
        {
            public Double? ConstructionNs { get; set; }
            public Double? ContextNs { get; set; }
            public Double? ParseAndDisposeNs { get; set; }
            public Double TotalNs { get; set; }
            public Int64? ConstructionBytes { get; set; }
            public Int64? ContextBytes { get; set; }
            public Int64? ParseAndDisposeBytes { get; set; }
            public Int64 TotalBytes { get; set; }
            public Int32 Gen0 { get; set; }
            public Int32 Gen1 { get; set; }
            public Int32 Gen2 { get; set; }
            public Int32 Checksum { get; set; }
        }
    }
}
#endif
