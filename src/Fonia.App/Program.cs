using System.Text;
using Fonia.App;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Data;
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

using var asking = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    asking.Cancel();
};

// Opened without arguments on a terminal: ask for the airport and go into the voice mode.
if (StartupWizard.ShouldAsk(args, terminal))
{
    var chosen = await StartupWizard.RunAsync(
        Console.In,
        Console.Out,
        AirportCatalog.Load(),
        settings.Settings.Airport ?? LastAirportStore.Default.Read(),
        LastAirportStore.Default.Remember,
        asking.Token);

    if (chosen is null)
    {
        return 0;
    }

    options = chosen;
}

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

if (options.AtcAirport is not null)
{
    // The connection to the simulator lives as long as the console: it gives the registration and then the radio, at every line.
    await using var session = new SimSession(sourceFactory, TimeProvider.System, TimeSpan.FromSeconds(10), Console.Error.WriteLine);

    VoiceModeHandle? voice = null;

    if (options.Voice)
    {
        voice = await VoiceCommands.TryCreateAsync(options, Console.In, Console.Out, Console.Error, terminal, () => options.Com1 ?? session.Com1, session.States, cancellation.Token);

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

    var atc = new AtcConsole(
        AirportCatalog.Load(),
        new RuleBasedNlu(),
        new AtcEngine(),
        new PortuguesePhraseology(),
        Console.In,
        Console.Out,
        Console.Error,
        session.ReadCallsignAsync,
        interactive: !Console.IsInputRedirected && interactive,
        voiceMode: voice?.Mode,
        liveCom1: () => session.Com1,
        metarSource: metar);

    return await atc.RunAsync(options, cancellation.Token);
}

var runner = new AppRunner(
    sourceFactory,
    Console.Out,
    Console.Error,
    interactive,
    interactive ? new ConsoleTerminal() : null);

return await runner.RunAsync(options, cancellation.Token);
