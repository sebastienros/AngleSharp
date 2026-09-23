# Jint configuration API experiment

This is a **managed AngleSharp component benchmark** comparing the current lazy
configuration API with the proposed mutable-builder / immutable-snapshot API.
The builder is internal to the benchmark assembly. Nothing changes the shipping
library or its legacy observable behavior.

## Reproduce

Build and test in Release. The tests link the same prototype and scenario source
files as the benchmark, so tests do not exercise a second implementation.

```sh
dotnet test src/AngleSharp.Core.Tests/AngleSharp.Core.Tests.csproj -c Release -f net10.0 --filter FullyQualifiedName~ConfigurationBuilderTests
dotnet run -c Release -f net10.0 --project src/AngleSharp.Benchmarks -- --configuration-smoke
dotnet run -c Release -f net10.0 --project src/AngleSharp.Benchmarks -- --configuration --filter '*' --exporters json csv
```

The class uses BenchmarkDotNet's default iteration policy, three independent
launches and MemoryDiagnoser. Tiering/PGO stay at production defaults. Do not quote
Dry or Short job timings. The unchanged control is parameterized deliberately:
its code ignores the API parameter and provides an A/A observation.
Use the dedicated `--configuration` or `--configuration-api` entry points:
assembly-wide discovery initializes unrelated downloading benchmarks even when a
later name filter excludes them. The dedicated entry points discover only this
offline fixture.

For paired measurements, select one arm per invocation with
`--configuration-api Current` or `--configuration-api Builder`. Use six or more
rounds, alternate Current/Builder and Builder/Current order, keep method filters
identical, and use separate `--artifacts` directories. For example:

```sh
dotnet run -c Release -f net10.0 --project src/AngleSharp.Benchmarks -- --configuration-api Current --filter '*JintConfigurationBenchmark*' --exporters json csv --artifacts artifacts/configuration/round-1-current
dotnet run -c Release -f net10.0 --project src/AngleSharp.Benchmarks -- --configuration-api Builder --filter '*JintConfigurationBenchmark*' --exporters json csv --artifacts artifacts/configuration/round-1-builder
```

On the shared Jint campaign host, every build, test and measurement batch must
hold an exclusive `fcntl.flock` on an open descriptor for
`/tmp/jint-browser-improvements.measurement.lock`. Never replace/delete that file.
Bracket measurements with the campaign's machine-idle validation; the lock alone
does not establish an idle host. Do not disable guards or production tiering.
Report absolute costs, allocation, per-round paired deltas/intervals and the
unchanged control. Do not infer a browser speedup from these component numbers.
The smoke command runs all four methods in both arms and checks results without
printing timings; it is not a shortened performance job.

## Boundaries

| Method | Included | Excluded |
|---|---|---|
| Construction | Fresh default configuration, SVG-resolution context, host service substitutes, complete registration chain, real CSS/XML extensions, XML creator adjustment, all builder imports/copies and final snapshot | Final context and parse |
| MaterializeAndResolve | New context, consumption of the configured service list, parser/document/styling/render-device resolution and observer materialization | Configuration construction, document parsing |
| ConstructionContextAndParse | All construction plus a new context, real HTML parse, selector query, SVG-name checksum, disposal | Jint engine, page loop, network and script execution |
| UntouchedControl | Default AngleSharp configuration and the same parse/query/checksum | Both custom configuration paths |

Construction deliberately includes all eager work in the builder arm; the current
arm is allowed to return its lazy chain. Materialization alone cannot establish a
total win. Every complete parse invocation creates fresh services, context and
document. The materialization-only row reuses registrations, with fresh context
resolution slots each time; it never retains a DOM. No unrelated benchmark warms
another row's engine or context.

## Source and scenario fidelity

The reference is Jint commit `6363436848365979b2201ed4ec9892ac140c13bc`:

- `Jint.Browser/Runtime/Parsing/ParserDriver.cs`, `Run`;
- `Jint.Browser/Dom/CaseSensitiveSvgFactory.cs`, `Configure`;
- `Jint.Browser/Runtime/Parsing/PageDocumentFactory.cs`.

The investigation checkout `4a68b51677bd9df1dd6dcde603cf64e9041bd870` has identical
relevant source. AngleSharp baseline `35b26db83557a6a74ce286907833e3b17806d9eb`
matches the installed 1.8.2 package's recorded source commit. The benchmark pins
the same CSS 1.1.2 and XML 1.2.0 packages and references the in-repository core.

Both arms use the same sequence: default services; temporary SVG factory
resolution and case-sensitive adapter; document-factory replacement; WithCss;
pseudo-class factory replacement; WithXml; culture; styling replacement; resource
loader; frame observer creator; render-device creator; custom-element observer
creator; file-input observer creator; scripting instance; document XML creator
adjustment; final context and parse. Tests also cover scripting disabled.

The real plugin calls are important: CSS 1.1.2 performs conditional registrations,
wraps the pseudo-class factory, and replaces its observer and styling service;
XML mutates the replacement document factory's creator
table. CSS's shared factory objects and XML's document creators are private to
their packages. `UseCss` and `UseXml` therefore use an explicit compatibility
bridge: build a snapshot, call the actual extension, and import its returned
sequence once. All this work is timed. This prototype does **not** yet measure
direct builder-aware CSS/XML plugin implementations.
The pinned package records CSS source commit
`c5ddd09b54605551583d058bf37a31b82c8932f4`; a newer CSS checkout may omit that
pseudo-class wrapper, which is another reason to execute the pinned extension
rather than copy its registration code into this fixture.

### Host substitutions and limits

This repository does not contain Jint's page runtime. Its internal services
cannot be instantiated independently here. This fixture preserves their service
interfaces, order, instance-versus-creator registrations and per-navigation
lifetime, but deliberately substitutes:

- a delegating pseudo-class factory without Jint's page-state selector fixes;
- a delegating CSS styling service without page-load notifications;
- a resource loader and scripting service that throw if used (the fixture has
  neither external resources nor scripts);
- three distinct observer creators with a page-state counter instead of frame,
  custom-element and file-input behavior;
- a default render device instead of Jint's live viewport/preferences;
- invariant culture instead of the engine's configurable culture.

SVG creation follows Jint's adapter. The document factory retains the XML creator,
removes the XHTML HTML mapping and widens +xml routing; it is a fixture adaptation,
not Jint's full MIME parser. The HTML checksum is 21 (two matching elements and
the preserved `linearGradient` name); a separate test checks a +xml document.
Service-order tests compare concrete registration types and the identities of
host instances/delegates across the arms. Resolution tests check lazy factories,
per-context identity and attribute callbacks.

The holder for substituted page services and its captured delegates are fresh in
every construction invocation, in both arms. Their allocation/construction is
included, but is not asserted to equal Jint's real host services. This is much
closer than repeated marker `WithOnly` calls; it is still **not** an exact Jint
integration measurement. It makes no navigation, NativeAOT, Lightpanda, retained
memory, or historical 94%-of-allocation speedup claim.

## Proposed contract being tested

Add prepends in resolution order. Replace removes matching instances and variant
creator delegates by type, without calling user equality or deduplicating
unrelated registrations. Factories remain lazy and context-local. Import reads
external services once, immediately; Build copies registrations and exposes a
read-only view, so neither external-list nor later builder mutations change the
snapshot. Service instances themselves are not cloned or made immutable. The
builder is not thread-safe, and page-affine snapshots must not be shared across
pages. Legacy configuration APIs keep all their current deferred semantics.

The straightforward implementation uses amortized O(1) additions, O(S) removal /
replacement, and O(S) Build for S registrations. Plugin bridges still execute
their own bounded lazy chains. This establishes a bounded construction design;
it does not prove that the prototype is globally optimal.

## Validation of this change

Release net10.0 builds of the test and benchmark projects completed without
warnings on macOS arm64. The `ConfigurationBuilderTests` cover the prototype and
the separate phase probe. The
smoke command passed all four methods in both API arms, and dedicated discovery
listed only those methods for the combined, Current and Builder entry points.
The complete repository test suite and paired performance runs were not run.
There are no timing or allocation improvement claims in this PR; the commands
above are the reproduction path for collecting them.

## Configuration share of the complete parse

Run the attribution probe in separate processes from BenchmarkDotNet:

```sh
dotnet run -c Release -f net10.0 --project src/AngleSharp.Benchmarks -- --configuration-phases Current instrumented 10
dotnet run -c Release -f net10.0 --project src/AngleSharp.Benchmarks -- --configuration-phases Builder instrumented 10
dotnet run -c Release -f net10.0 --project src/AngleSharp.Benchmarks -- --configuration-phases Current plain 10
dotnet run -c Release -f net10.0 --project src/AngleSharp.Benchmarks -- --configuration-phases Builder plain 10
```

Each process warms its own API/mode five times, then measures ten fresh
configurations, contexts and documents. The instrumented mode brackets
configuration construction, `BrowsingContext.New` (including service-list
materialization), and the remaining parse/query/document-and-context disposal.
Its checksum and registration pipeline match the complete benchmark. It retains
the async result-task shape of the original parse method. The plain mode calls
the original, untouched complete operation with only outer observations.

For each launch, configuration setup's share is
`sum(construction + context) / sum(total)`, computed separately for elapsed time
and allocated bytes. Do not sum means from separate benchmark rows to construct
this fraction. Factory activation during parsing remains in the parse phase;
this is a boundary-based setup share, not a stack-based classification of every
service-related instruction. In particular it is not the original Jint browser
allocation profile's approximately 94% figure or a wall-time interpretation of it.

Allocation reads use process-wide `GC.GetTotalAllocatedBytes(true)` to include
work that crosses threads. They measure allocated bytes, not retained memory,
and can include runtime/background allocations inside that process. Phase
observations add overhead; compare instrumented and plain launches, alternate
their order, and report the difference without subtracting it as a correction.
The five-warmup diagnostic timing has a different JIT/GC history from BDN's long
warmup, so report its time shares separately from BDN means. GC counts are provided
per sample. Keep the idle checks and shared lock around these processes too.
