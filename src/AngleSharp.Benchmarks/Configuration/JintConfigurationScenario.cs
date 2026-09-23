namespace AngleSharp.Benchmarks.ConfigurationExperiment
{
    using AngleSharp.Css;
    using AngleSharp.Css.Dom;
    using AngleSharp.Dom;
    using AngleSharp.Html.Parser;
    using AngleSharp.Io;
    using AngleSharp.Scripting;
    using AngleSharp.Svg.Dom;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    // Registration order follows Jint ParserDriver.Run and CaseSensitiveSvgFactory.Configure
    // at 6363436848365979b2201ed4ec9892ac140c13bc. See README for host substitutions.
    internal static class JintConfigurationScenario
    {
        internal const String Markup = "<!doctype html><html><body><main id='host'><p class='item'>one</p><p class='item'>two</p><svg><linearGradient id='gradient'></linearGradient></svg></main></body></html>";

        internal static IConfiguration Create(Boolean builder, Boolean scripting = true)
        {
            return Create(builder, new PageServices(), Configuration.Default, scripting);
        }

        internal static IConfiguration Create(Boolean builder, PageServices page, IConfiguration defaults, Boolean scripting)
        {
            IElementFactory<Document, SvgElement> svg;

            using (var context = BrowsingContext.New(defaults))
            {
                svg = new CaseSensitiveSvgFactory(context.GetFactory<IElementFactory<Document, SvgElement>>());
            }

            IConfiguration configuration;

            if (builder)
            {
                var registrations = new ConfigurationBuilder(defaults)
                    .Replace<IElementFactory<Document, SvgElement>>(svg)
                    .Replace<IDocumentFactory>(page.Documents)
                    .UseCss()
                    .Replace<IPseudoClassSelectorFactory>(page.Selectors)
                    .UseXml()
                    .Add(CultureInfo.InvariantCulture)
                    .Replace<IStylingService>(page.Styling)
                    .Add(page.Loader)
                    .AddFactory<IAttributeObserver>(page.FrameCreator)
                    .AddFactory<IRenderDevice>(page.DeviceCreator)
                    .AddFactory<IAttributeObserver>(page.CustomElementCreator)
                    .AddFactory<IAttributeObserver>(page.FileInputCreator);

                if (scripting)
                {
                    registrations.Add(page.Scripting);
                }

                page.Documents.ReadXmlWithTheXmlParser();
                configuration = registrations.Build();
            }
            else
            {
                configuration = defaults
                    .WithOnly<IElementFactory<Document, SvgElement>>(svg)
                    .WithOnly<IDocumentFactory>(page.Documents)
                    .WithCss()
                    .WithOnly<IPseudoClassSelectorFactory>(page.Selectors)
                    .WithXml()
                    .WithCulture(CultureInfo.InvariantCulture)
                    .WithOnly<IStylingService>(page.Styling)
                    .With(page.Loader)
                    .With<IAttributeObserver>(page.FrameCreator)
                    .With<IRenderDevice>(page.DeviceCreator)
                    .With<IAttributeObserver>(page.CustomElementCreator)
                    .With<IAttributeObserver>(page.FileInputCreator);

                if (scripting)
                {
                    configuration = configuration.With(page.Scripting);
                }

                page.Documents.ReadXmlWithTheXmlParser();
            }

            return configuration;
        }

        internal static Int32 Resolve(IConfiguration configuration)
        {
            using (var context = BrowsingContext.New(configuration))
            {
                if (context.GetService<IHtmlParser>() == null || context.GetService<IDocumentFactory>() == null ||
                    context.GetService<IStylingService>() == null || context.GetService<IRenderDevice>() == null)
                {
                    throw new InvalidOperationException("Missing parse service.");
                }

                return context.GetServices<IAttributeObserver>().Count();
            }
        }

        internal static async Task<Int32> Parse(IConfiguration configuration)
        {
            using (var context = BrowsingContext.New(configuration))
            using (var document = await context.OpenAsync(request => request.Content(Markup)).ConfigureAwait(false))
            {
                return document.QuerySelectorAll("#host > p.item").Length * 10 +
                    (document.GetElementById("gradient").LocalName == "linearGradient" ? 1 : 0);
            }
        }

        internal sealed class PageServices
        {
            internal readonly PageDocumentFactory Documents = new PageDocumentFactory();
            internal readonly IPseudoClassSelectorFactory Selectors = new PageSelectors();
            internal readonly IStylingService Styling = new PageStyling();
            internal readonly IResourceLoader Loader = new PageLoader();
            internal readonly IScriptingService Scripting = new PageScripting();
            internal readonly Func<IBrowsingContext, IAttributeObserver> FrameCreator;
            internal readonly Func<IBrowsingContext, IRenderDevice> DeviceCreator;
            internal readonly Func<IBrowsingContext, IAttributeObserver> CustomElementCreator;
            internal readonly Func<IBrowsingContext, IAttributeObserver> FileInputCreator;
            internal Int32 Creations;
            internal Int32 Notifications;

            internal PageServices()
            {
                FrameCreator = _ => { Creations++; return new FrameObserver(this); };
                DeviceCreator = _ => { Creations++; return new DefaultRenderDevice(); };
                CustomElementCreator = _ => { Creations++; return new CustomElementObserver(this); };
                FileInputCreator = _ => { Creations++; return new FileInputObserver(this); };
            }
        }

        private sealed class CaseSensitiveSvgFactory : IElementFactory<Document, SvgElement>
        {
            private readonly IElementFactory<Document, SvgElement> _native;
            internal CaseSensitiveSvgFactory(IElementFactory<Document, SvgElement> native) { _native = native; }
            public SvgElement Create(Document document, String localName, String prefix = null, NodeFlags flags = NodeFlags.None)
            {
                var element = _native.Create(document, localName, prefix, flags);
                return element.LocalName == localName ? element : new SvgElement(document, localName, prefix, flags);
            }
        }

        internal sealed class PageDocumentFactory : DefaultDocumentFactory
        {
            private Creator _xml;
            internal void ReadXmlWithTheXmlParser()
            {
                _xml = Unregister(MimeTypeNames.Xml);

                if (_xml != null)
                {
                    Register(MimeTypeNames.Xml, _xml);
                    Unregister(MimeTypeNames.ApplicationXHtml);
                }
            }

            protected override Task<IDocument> CreateDefaultAsync(IBrowsingContext context, CreateDocumentOptions options, CancellationToken cancel)
            {
                var type = options.ContentType?.Content;
                return _xml != null && type != null && type.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
                    ? _xml(context, options, cancel)
                    : base.CreateDefaultAsync(context, options, cancel);
            }
        }

        private sealed class PageSelectors : IPseudoClassSelectorFactory
        {
            private readonly DefaultPseudoClassSelectorFactory _inner = new DefaultPseudoClassSelectorFactory();
            public ISelector Create(String name) => _inner.Create(name);
        }

        private sealed class PageStyling : IStylingService
        {
            private readonly IStylingService _inner = new CssStylingService();
            public Boolean SupportsType(String mimeType) => _inner.SupportsType(mimeType);
            public Task<IStyleSheet> ParseStylesheetAsync(IResponse response, StyleOptions options, CancellationToken cancel)
                => _inner.ParseStylesheetAsync(response, options, cancel);
        }

        private sealed class PageLoader : IResourceLoader
        {
            public IEnumerable<IDownload> GetDownloads() => Array.Empty<IDownload>();
            public IDownload FetchAsync(ResourceRequest request) => throw new InvalidOperationException("Fixture must not fetch resources.");
        }

        private sealed class PageScripting : IScriptingService
        {
            public Boolean SupportsType(String mimeType) => mimeType == "text/javascript" || mimeType == "application/javascript";
            public Task EvaluateScriptAsync(IResponse response, ScriptOptions options, CancellationToken cancel)
                => throw new InvalidOperationException("Fixture must not execute scripts.");
        }

        private abstract class Observer : IAttributeObserver
        {
            private readonly PageServices _page;
            protected Observer(PageServices page) { _page = page; }
            public void NotifyChange(IElement host, String name, String value) { _page.Notifications++; }
        }
        private sealed class FrameObserver : Observer { internal FrameObserver(PageServices page) : base(page) { } }
        private sealed class CustomElementObserver : Observer { internal CustomElementObserver(PageServices page) : base(page) { } }
        private sealed class FileInputObserver : Observer { internal FileInputObserver(PageServices page) : base(page) { } }
    }
}
