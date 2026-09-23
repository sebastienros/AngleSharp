namespace AngleSharp.Benchmarks.ConfigurationExperiment
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // Benchmark-only API prototype. No changes to the shipping Configuration contract.
    internal sealed class ConfigurationBuilder
    {
        // Append in registration order; export in resolution order (newest first).
        private readonly List<Object> _registrations = new List<Object>();

        public ConfigurationBuilder(IConfiguration source)
        {
            ImportSnapshot(source);
        }

        public ConfigurationBuilder Add(Object instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            _registrations.Add(instance);
            return this;
        }

        public ConfigurationBuilder AddFactory<T>(Func<IBrowsingContext, T> creator)
        {
            if (creator == null)
            {
                throw new ArgumentNullException(nameof(creator));
            }

            return Add(creator);
        }

        public ConfigurationBuilder Remove<T>()
        {
            // No user-defined equality, hash sets, closures, or deferred source walks.
            var write = 0;

            for (var read = 0; read < _registrations.Count; read++)
            {
                var entry = _registrations[read];

                if (!(entry is T) && !(entry is Func<IBrowsingContext, T>))
                {
                    _registrations[write++] = entry;
                }
            }

            _registrations.RemoveRange(write, _registrations.Count - write);
            return this;
        }

        public ConfigurationBuilder Replace<T>(T instance)
        {
            if (instance is null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            return Remove<T>().Add(instance);
        }

        public ConfigurationBuilder ReplaceFactory<T>(Func<IBrowsingContext, T> creator)
        {
            if (creator == null)
            {
                throw new ArgumentNullException(nameof(creator));
            }

            return Remove<T>().AddFactory(creator);
        }

        public IConfiguration Build()
        {
            var entries = _registrations.ToArray();
            Array.Reverse(entries);
            return new Configuration(Array.AsReadOnly(entries));
        }

        // CSS's shared factories and XML's document creators are internal to their packages.
        // Run the real extension over a snapshot, then import its result exactly once.
        // Both copies and all plugin enumeration are included in the measured operation.
        public ConfigurationBuilder UseCss() => ImportSnapshot(Build().WithCss());

        public ConfigurationBuilder UseXml() => ImportSnapshot(Build().WithXml());

        private ConfigurationBuilder ImportSnapshot(IConfiguration source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var entries = source.Services.ToArray();
            Array.Reverse(entries);
            _registrations.Clear();
            _registrations.AddRange(entries);
            return this;
        }
    }
}
