using System.Text;
using Fonia.App;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Data;
using Fonia.Sim.Msfs;
using Microsoft.Extensions.Logging;

// Composition root: reads the command line, wires the pieces and handles Ctrl+C. The logic lives in
// ArgumentParser, AppRunner and StateFormatter, which are tested without a console.

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var settings = SettingsLoader.Load(AppContext.BaseDirectory, Path.GetDirectoryName(SettingsLoader.UserFile)!, SettingsLoader.ProcessEnvironment());

// A terminal in both directions: only then can the app ask the user something, or rewrite a line of progress.
var terminal = !Console.IsInputRedirected && !Console.IsOutputRedirected;

var parsed = ArgumentParser.Parse(args);

if (parsed.Options is { ShowConfig: true })
{
    ConfigCommands.Show(settings, Console.Out);

    if (settings.Error is null)
    {
        return 0;
    }

    Console.Error.WriteLine($"fonia: {settings.Error}");
    return 2;
}

if (parsed.Options is { ShowCases: true })
{
    CaseLog.List(CaseLog.DefaultRoot, Console.Out);
    return 0;
}

if (parsed.Options is { InitConfig: true })
{
    return ConfigCommands.Init(SettingsLoader.UserFile, Console.Out, Console.Error);
}

if (settings.Error is not null)
{
    Console.Error.WriteLine($"fonia: {settings.Error}");
    Console.Error.WriteLine("Corrija o arquivo, ou veja \"fonia --config\".");
    return 2;
}

if (parsed.ShowHelp)
{
    Console.Out.WriteLine(ArgumentParser.HelpText);
    return 0;
}

if (parsed.ShowVersion)
{
    Console.Out.WriteLine($"fonia {AppVersion.Current}");
    return 0;
}

if (parsed.Error is not null)
{
    Console.Error.WriteLine($"fonia: {parsed.Error}");
    Console.Error.WriteLine("Use --help para ver as opções.");
    return 2;
}

var options = parsed.Options!;

var applied = SettingsApplier.Apply(options, settings.Settings, ArgumentParser.OptionsGiven(args));

if (applied.Error is not null)
{
    Console.Error.WriteLine($"fonia: {applied.Error}");
    return 2;
}

options = applied.Options;

// Logs go to the error output so that the state shown on the standard output stays clean.
using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(options.LogLevel)
    .AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace));

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // let the app end cleanly: the recording stays valid and the connection is closed
    cancellation.Cancel();
};

if (options.DumpLayoutAirport is { } dumpAirport)
{
    return await LayoutCommands.DumpAsync(dumpAirport, new MsfsFacilitySource(), AirportCatalog.Load(), LayoutCommands.DefaultRoot, TimeProvider.System, Console.Out, Console.Error, cancellation.Token);
}

if (options.DownloadModels)
{
    return await ModelCommands.DownloadAsync(options, Console.In, Console.Out, Console.Error, terminal, cancellation.Token);
}

if (options.ListMics)
{
    return MicCommands.List(Console.Out, Console.Error);
}

if (options.MicTestPath is not null)
{
    return await MicCommands.TestAsync(options, Console.Out, Console.Error, cancellation.Token);
}

if (options.TranscribePath is not null)
{
    return await SttCommands.TranscribeAsync(options, Console.In, Console.Out, Console.Error, terminal, cancellation.Token);
}

if (options.SpeakText is not null)
{
    return await TtsCommands.SpeakAsync(options, Console.In, Console.Out, Console.Error, terminal, cancellation.Token);
}

var interactive = !Console.IsOutputRedirected;
var sourceFactory = new SimSourceFactory(loggerFactory, TimeProvider.System);

// The connection to the simulator lives as long as the console: it gives the registration (and the airport, at the start) and then the
// radio, at every line.
await using var session = new SimSession(sourceFactory, TimeProvider.System, TimeSpan.FromSeconds(10), Console.Error.WriteLine);

// Opened without arguments on a terminal: the callsign and the airport come from the simulator, and what it cannot give is asked.
if (StartupWizard.ShouldAsk(args, terminal))
{
    var catalog = AirportCatalog.Load();
    var chosen = await StartupWizard.RunAsync(
        Console.In,
        Console.Out,
        catalog,
        new WizardDefaults(settings.Settings.Airport ?? LastValueStore.Airport.Read(), settings.Settings.Callsign ?? LastValueStore.Callsign.Read()),
        async ct => await session.ReadFirstStateAsync(options, TimeSpan.FromSeconds(5), ct) is { } state
            ? new SimulatorFix(state.Callsign, state.LatitudeDegrees, state.LongitudeDegrees)
            : null,
        catalog.Nearest,
        LastValueStore.Airport.Remember,
        LastValueStore.Callsign.Remember,
        cancellation.Token);

    if (chosen is null)
    {
        return 0;
    }

    options = options with { AtcAirport = chosen.AtcAirport, Voice = true, Callsign = chosen.Callsign };
}

if (options.AtcAirport is not null)
{
    // What the controller could not follow is kept on this computer (never sent), to improve the app: --no-cases turns it off.
    Func<AirportContext, string, CaseLog?>? cases = null;

    if (!options.NoCases)
    {
        CaseLog.Prune(CaseLog.DefaultRoot, TimeProvider.System, TimeSpan.FromDays(30), 200L * 1024 * 1024);
        cases = (airport, callsign) => new CaseLog(CaseLog.DefaultRoot, TimeProvider.System, new PortuguesePhraseology(Companies.From(options)), airport, callsign, options.SaveAudio);
    }

    VoiceModeHandle? voice = null;

    if (options.Voice)
    {
        voice = await VoiceCommands.TryCreateAsync(options, Console.In, Console.Out, Console.Error, terminal, () => options.Com1 ?? session.Com1, session.States, cases, () => session.Latest, cancellation.Token);

        if (voice is null)
        {
            return 1;
        }
    }

    await using var voiceResources = voice;

    // The weather is an extra: the METAR of the airport from aviationweather.gov (only the ICAO code goes out), kept on disk for a while.
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Fonia (+https://github.com/mactaviish/fonia)");
    var metar = new CachedMetarSource(
        new AviationWeatherMetarSource(http),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fonia", "cache", "metar"),
        TimeProvider.System,
        freshFor: TimeSpan.FromMinutes(30),
        staleFor: TimeSpan.FromHours(3));

    var airports = AirportCatalog.Load();
    var companies = Companies.From(options);

    var atc = new AtcConsole(
        airports,
        new RuleBasedNlu(companies),
        new AtcEngine(),
        new PortuguesePhraseology(companies),
        Console.In,
        Console.Out,
        Console.Error,
        session.ReadCallsignAsync,
        interactive: !Console.IsInputRedirected && interactive,
        voiceMode: voice?.Mode,
        liveCom1: () => session.Com1,
        metarSource: metar,
        cases: cases,
        nearestAirport: airports.Nearest,
        states: session.States,
        liveAircraft: () => session.Latest,
        runwayUse: RunwayUseTable.Load().RulesFor,
        layouts: new CachedAirportLayoutSource(
            options.Source == SimSourceKind.Msfs ? new MsfsAirportLayoutSource() : null,
            LayoutCommands.DefaultRoot));

    return await atc.RunAsync(options, cancellation.Token);
}

var runner = new AppRunner(
    sourceFactory,
    Console.Out,
    Console.Error,
    interactive,
    interactive ? new ConsoleTerminal() : null);

return await runner.RunAsync(options, cancellation.Token);
