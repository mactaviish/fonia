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

var parsed = ArgumentParser.Parse(args);

if (parsed.ShowHelp)
{
    Console.Out.WriteLine(ArgumentParser.HelpText);
    return 0;
}

if (parsed.Error is not null)
{
    Console.Error.WriteLine($"fonia: {parsed.Error}");
    Console.Error.WriteLine("Use --help para ver as opções.");
    return 2;
}

var options = parsed.Options!;

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

var interactive = !Console.IsOutputRedirected;
var sourceFactory = new SimSourceFactory(loggerFactory, TimeProvider.System);

if (options.AtcAirport is not null)
{
    var callsignReader = new SimCallsignReader(sourceFactory, TimeProvider.System, TimeSpan.FromSeconds(10), Console.Error.WriteLine);
    var atc = new AtcConsole(
        AirportCatalog.Load(),
        new RuleBasedNlu(),
        new AtcEngine(),
        new PortuguesePhraseology(),
        Console.In,
        Console.Out,
        Console.Error,
        callsignReader.ReadAsync,
        interactive: !Console.IsInputRedirected && interactive);

    return await atc.RunAsync(options, cancellation.Token);
}

var runner = new AppRunner(
    sourceFactory,
    Console.Out,
    Console.Error,
    interactive,
    interactive ? new ConsoleTerminal() : null);

return await runner.RunAsync(options, cancellation.Token);
