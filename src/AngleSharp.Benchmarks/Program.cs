using System;
using System.Text;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Running;

namespace AngleSharp.Benchmarks
{
    static class Program
    {
        static void Main(String[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

#if NET8_0_OR_GREATER
            if (args.Length == 4 && args[0] == "--configuration-phases")
            {
                ConfigurationExperiment.ConfigurationPhaseProbe.Run(args[1], args[2], Int32.Parse(args[3]));
                return;
            }
#endif

            if (args.Length == 1 && args[0] == "--configuration-smoke")
            {
                foreach (var api in new[] { JintConfigurationBenchmark.ConfigurationApi.Current, JintConfigurationBenchmark.ConfigurationApi.Builder })
                {
                    var benchmark = new JintConfigurationBenchmark { Api = api };
                    var configuration = benchmark.Construction();
                    benchmark.SetupResolution();

                    if (!configuration.Services.Any() || benchmark.MaterializeAndResolve() < 4 ||
                        benchmark.ConstructionContextAndParse().GetAwaiter().GetResult() != 21 ||
                        benchmark.UntouchedControl().GetAwaiter().GetResult() != 21)
                    {
                        throw new InvalidOperationException("Configuration benchmark checksum failed: " + api);
                    }

                    Console.WriteLine("Configuration benchmark smoke passed: " + api);
                }

                return;
            }

            if (
                args.Length == 1
                && args[0].Equals("--text-source-dispatch", StringComparison.OrdinalIgnoreCase)
            )
            {
                BenchmarkRunner.Run<TextSourceDispatchBenchmark>();
                return;
            }

            if (
                args.Length == 1
                && args[0].Equals("--scan-data-text", StringComparison.OrdinalIgnoreCase)
            )
            {
                BenchmarkRunner.Run<ScanDataTextBenchmark>();
                return;
            }

            // Select one parameter arm per process for alternating-order paired runs.
            var apiIndex = Array.IndexOf(args, "--configuration-api");

            if (apiIndex >= 0)
            {
                if (apiIndex + 1 >= args.Length ||
                    !Enum.TryParse<JintConfigurationBenchmark.ConfigurationApi>(args[apiIndex + 1], out var api) ||
                    !Enum.IsDefined(typeof(JintConfigurationBenchmark.ConfigurationApi), api))
                {
                    throw new ArgumentException("--configuration-api requires Current or Builder.");
                }

                var config = ManualConfig.Create(DefaultConfig.Instance).AddFilter(new SimpleFilter(benchmark =>
                    benchmark.Descriptor.Type == typeof(JintConfigurationBenchmark) &&
                    benchmark.Parameters.Items.Any(parameter => parameter.Name == "Api" && parameter.Value.Equals(api))));
                args = args.Where((_, index) => index != apiIndex && index != apiIndex + 1).ToArray();
                BenchmarkSwitcher.FromTypes(new[] { typeof(JintConfigurationBenchmark) }).Run(args, config);
                return;
            }

            var configurationIndex = Array.IndexOf(args, "--configuration");

            if (configurationIndex >= 0)
            {
                args = args.Where((_, index) => index != configurationIndex).ToArray();
                BenchmarkSwitcher.FromTypes(new[] { typeof(JintConfigurationBenchmark) }).Run(args);
                return;
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
