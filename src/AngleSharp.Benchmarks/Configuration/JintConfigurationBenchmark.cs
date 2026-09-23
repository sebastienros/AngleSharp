namespace AngleSharp.Benchmarks
{
    using AngleSharp.Benchmarks.ConfigurationExperiment;
    using BenchmarkDotNet.Attributes;
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// A fresh Jint-shaped configuration per construction/parse invocation. Materialization alone
    /// deliberately reuses registrations, never a context or document. Plugins and snapshot copies
    /// stay inside construction. This is a managed AngleSharp component benchmark, not Jint navigation.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 3)]
    public class JintConfigurationBenchmark
    {
        public enum ConfigurationApi { Current, Builder }

        [Params(ConfigurationApi.Current, ConfigurationApi.Builder)]
        public ConfigurationApi Api { get; set; }

        private IConfiguration _configuration;

        [GlobalSetup(Target = nameof(MaterializeAndResolve))]
        public void SetupResolution()
        {
            _configuration = JintConfigurationScenario.Create(Api == ConfigurationApi.Builder);
        }

        [Benchmark]
        public IConfiguration Construction() => JintConfigurationScenario.Create(Api == ConfigurationApi.Builder);

        [Benchmark]
        public Int32 MaterializeAndResolve() => JintConfigurationScenario.Resolve(_configuration);

        [Benchmark]
        public Task<Int32> ConstructionContextAndParse()
            => JintConfigurationScenario.Parse(JintConfigurationScenario.Create(Api == ConfigurationApi.Builder));

        // Identical work in both parameter arms. No prototype builder or service replacement.
        [Benchmark]
        public Task<Int32> UntouchedControl() => JintConfigurationScenario.Parse(Configuration.Default);
    }
}
