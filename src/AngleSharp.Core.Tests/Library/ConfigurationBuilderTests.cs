namespace AngleSharp.Core.Tests.Library
{
    using AngleSharp.Benchmarks.ConfigurationExperiment;
    using AngleSharp.Css;
    using AngleSharp.Dom;
    using AngleSharp.Io;
    using AngleSharp.Scripting;
    using AngleSharp.Svg.Dom;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    [TestFixture]
    public class ConfigurationBuilderTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void JintSequencePreservesOrderInstancesAndCreatorShapes(Boolean scripting)
        {
            var page = new JintConfigurationScenario.PageServices();
            var defaults = Configuration.Default;
            var current = JintConfigurationScenario.Create(false, page, defaults, scripting);
            var builder = JintConfigurationScenario.Create(true, page, defaults, scripting);
            var left = current.Services.ToArray();
            var right = builder.Services.ToArray();
            CollectionAssert.AreEqual(left.Select(x => x.GetType()), right.Select(x => x.GetType()));
            Assert.AreEqual(0, page.Creations, "Configuration construction must not invoke page creators.");

            foreach (var instance in new Object[] { page.Documents, page.Selectors, page.Styling, page.Loader,
                page.FrameCreator, page.DeviceCreator, page.CustomElementCreator, page.FileInputCreator })
            {
                Assert.AreSame(instance, right[Array.IndexOf(left, instance)]);
            }

            Assert.AreEqual(scripting, right.Any(x => x is IScriptingService));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RealParseResolvesObserversAndPreservesSvgCase(Boolean builder)
        {
            var page = new JintConfigurationScenario.PageServices();
            var configuration = JintConfigurationScenario.Create(builder, page, Configuration.Default, true);
            Assert.AreEqual(21, await JintConfigurationScenario.Parse(configuration).ConfigureAwait(false));
            Assert.Greater(page.Notifications, 0);
            Assert.GreaterOrEqual(JintConfigurationScenario.Resolve(configuration), 4);

            using (var context = BrowsingContext.New(configuration))
            using (var document = await context.OpenAsync(request => request.Content("<!doctype html><body>")).ConfigureAwait(false))
            {
                var factory = context.GetFactory<IElementFactory<Document, SvgElement>>();
                Assert.AreEqual("SVG", factory.Create((Document)document, "SVG").LocalName);
            }
        }

        [TestCase(false, "text/xml")]
        [TestCase(true, "text/xml")]
        [TestCase(false, "application/xhtml+xml")]
        [TestCase(true, "application/xhtml+xml")]
        [TestCase(false, "application/example+xml")]
        [TestCase(true, "application/example+xml")]
        public async Task XmlCreatorsRemainOnTheReplacementDocumentFactory(Boolean builder, String contentType)
        {
            var configuration = JintConfigurationScenario.Create(builder);

            using (var context = BrowsingContext.New(configuration))
            using (var document = await context.OpenAsync(request => request
                .Content("<root><child /></root>").Header("Content-Type", contentType)).ConfigureAwait(false))
            {
                Assert.AreEqual("root", document.DocumentElement.LocalName);
            }
        }

        [Test]
        public void SnapshotOwnsItsStorageAndDoesNotFreezeTheBuilder()
        {
            var source = new List<Object> { "old" };
            var builder = new ConfigurationBuilder(new Configuration(source));
            source[0] = "external edit";
            var snapshot = builder.Build();
            builder.Add("new");
            CollectionAssert.AreEqual(new Object[] { "old" }, snapshot.Services);
            CollectionAssert.AreEqual(new Object[] { "new", "old" }, builder.Build().Services);
            Assert.IsFalse(snapshot.Services is Object[]);
            Assert.Throws<NotSupportedException>(() => ((IList<Object>)snapshot.Services)[0] = "edit");
        }

        [Test]
        public void ReplacementRemovesInstancesAndCovariantCreatorsWithoutEqualityCalls()
        {
            var other = new EqualityTrap();
            var builder = new ConfigurationBuilder(new Configuration(Array.Empty<Object>()))
                .Add(other).Add(other).Add(new Service()).AddFactory<Service>(_ => new Service());
            var replacement = new Service();
            builder.Replace<IService>(replacement);
            var entries = builder.Build().Services.ToArray();
            Assert.AreEqual(3, entries.Length);
            Assert.AreSame(replacement, entries[0]);
            Assert.AreSame(other, entries[1]);
            Assert.AreSame(other, entries[2]);
        }

        [Test]
        public void FactoriesStayLazyAndAreCachedPerContext()
        {
            var calls = 0;
            var configuration = new ConfigurationBuilder(new Configuration(Array.Empty<Object>()))
                .AddFactory<IService>(_ => { calls++; return new Service(); }).Build();
            Assert.AreEqual(0, calls);

            using (var first = BrowsingContext.New(configuration))
            using (var second = BrowsingContext.New(configuration))
            {
                var service = first.GetService<IService>();
                Assert.AreSame(service, first.GetService<IService>());
                Assert.AreNotSame(service, second.GetService<IService>());
                Assert.AreEqual(2, calls);
            }
        }

        [Test]
        public void ImportEnumeratesOnceAndConstructionSurfacesSourceExceptions()
        {
            var enumerations = 0;
            IEnumerable<Object> Source()
            {
                enumerations++;
                yield return new Object();
            }
            var builder = new ConfigurationBuilder(new Configuration(Source()));
            builder.Build().Services.ToArray();
            builder.Build().Services.ToArray();
            Assert.AreEqual(1, enumerations);

            IEnumerable<Object> Throws()
            {
                yield return new Object();
                throw new InvalidOperationException();
            }
            Assert.Throws<InvalidOperationException>(() => new ConfigurationBuilder(new Configuration(Throws())));
        }

        private interface IService { }
        private sealed class Service : IService { }
        private sealed class EqualityTrap
        {
            public override Boolean Equals(Object other) => throw new InvalidOperationException("Must not compare services.");
            public override Int32 GetHashCode() => throw new InvalidOperationException("Must not hash services.");
        }
    }
}
