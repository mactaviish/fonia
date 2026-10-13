using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Fonia.App;

internal enum SimSourceKind
{
    Msfs,
    Mock,
    Replay,
}

/// <summary>What the command line asks for.</summary>
/// <param name="Speed">Replay speed: 1 is real time, <see cref="double.PositiveInfinity"/> is "as fast as possible".</param>
internal sealed record AppOptions(
    SimSourceKind Source = SimSourceKind.Msfs,
    string? ReplayPath = null,
    double Speed = 1.0,
    string? RecordPath = null,
    TimeSpan? Duration = null,
    LogLevel LogLevel = LogLevel.Warning);

/// <summary>The outcome of reading the command line: options, a message about what is wrong, or a request for help.</summary>
internal sealed record ParsedArguments(AppOptions? Options, string? Error, bool ShowHelp);

/// <summary>Reads the command line by hand: the options are few and a parsing library would be the heaviest part of the app.</summary>
internal static class ArgumentParser
{
    public const string HelpText = """
        Fonia: estado da aeronave em tempo real, vindo do simulador.

        Uso: fonia [opções]

          --sim msfs|mock        Fonte dos dados. msfs (padrão) lê o Microsoft Flight Simulator 2020;
                                 mock mostra um taxi simulado, para testar sem simulador.
          --replay <arquivo>     Reproduz uma gravação (.jsonl) em vez de ler um simulador.
          --speed <n|max>        Velocidade da reprodução: 1 = tempo real (padrão), 2 = o dobro,
                                 max = sem esperar (a tela mostra só alguns snapshots, mas
                                 o arquivo gravado com --record não é afetado). Só vale com --replay.
          --record <arquivo>     Grava a sessão em um arquivo .jsonl, além de mostrá-la.
          --duration <segundos>  Encerra sozinho depois desse tempo.
          --log-level <nível>    trace, debug, information, warning (padrão), error, critical ou none.
                                 Os logs vão para a saída de erro.
          --help, -h             Mostra esta ajuda.

        Ctrl+C encerra com limpeza (a gravação fica válida).
        Códigos de saída: 0 normal, 1 erro durante a execução, 2 uso incorreto.
        """;

    private static readonly string[] LogLevelNames = ["trace", "debug", "information", "warning", "error", "critical", "none"];

    public static ParsedArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        SimSourceKind? source = null;
        string? replay = null;
        string? record = null;
        double? speed = null;
        TimeSpan? duration = null;
        var logLevel = LogLevel.Warning;

        for (var i = 0; i < args.Length; i++)
        {
            var (name, inlineValue) = Split(args[i]);

            if (name is "--help" or "-h" or "/?")
            {
                return new ParsedArguments(null, null, ShowHelp: true);
            }

            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                return Failure($"argumento inesperado: '{args[i]}'. Use --help para ver as opções.");
            }

            if (name is not ("--sim" or "--replay" or "--speed" or "--record" or "--duration" or "--log-level"))
            {
                return Failure($"opção desconhecida: '{name}'. Use --help para ver as opções.");
            }

            var value = inlineValue ?? (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null);

            if (value is null)
            {
                return Failure($"{name} precisa de um valor.");
            }

            string? problem = null;

            switch (name)
            {
                case "--sim":
                    source = ParseSource(value, out problem);
                    break;
                case "--replay":
                    replay = value;
                    break;
                case "--record":
                    record = value;
                    break;
                case "--speed":
                    speed = ParseSpeed(value, out problem);
                    break;
                case "--duration":
                    duration = ParseDuration(value, out problem);
                    break;
                case "--log-level":
                    logLevel = ParseLogLevel(value, out problem);
                    break;
            }

            if (problem is not null)
            {
                return Failure(problem);
            }
        }

        return Validate(source, replay, record, speed, duration, logLevel);
    }

    private static ParsedArguments Validate(SimSourceKind? source, string? replay, string? record, double? speed, TimeSpan? duration, LogLevel logLevel)
    {
        if (replay is not null && source is SimSourceKind.Msfs or SimSourceKind.Mock)
        {
            return Failure("--replay não pode ser usado com --sim msfs ou --sim mock; use só --replay <arquivo>.");
        }

        if (replay is null && source == SimSourceKind.Replay)
        {
            return Failure("--sim replay precisa de --replay <arquivo>.");
        }

        var effective = replay is not null ? SimSourceKind.Replay : source ?? SimSourceKind.Msfs;

        if (speed is not null && effective != SimSourceKind.Replay)
        {
            return Failure("--speed só vale para a reprodução: use também --replay <arquivo>.");
        }

        return new ParsedArguments(new AppOptions(effective, replay, speed ?? 1.0, record, duration, logLevel), null, ShowHelp: false);
    }

    private static (string Name, string? Value) Split(string argument)
    {
        var equals = argument.IndexOf('=', StringComparison.Ordinal);
        return argument.StartsWith("--", StringComparison.Ordinal) && equals > 0
            ? (argument[..equals], argument[(equals + 1)..])
            : (argument, null);
    }

    private static SimSourceKind? ParseSource(string value, out string? problem)
    {
        problem = null;

        switch (value.ToLowerInvariant())
        {
            case "msfs":
                return SimSourceKind.Msfs;
            case "mock":
                return SimSourceKind.Mock;
            case "replay":
                return SimSourceKind.Replay;
            default:
                problem = $"--sim '{value}' não existe. Use msfs ou mock (ou --replay <arquivo>).";
                return null;
        }
    }

    private static double? ParseSpeed(string value, out string? problem)
    {
        problem = null;

        if (value.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return double.PositiveInfinity;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) && double.IsFinite(speed) && speed > 0)
        {
            return speed;
        }

        problem = $"--speed '{value}' é inválido: use um número maior que zero (ex.: 1, 2, 0.5) ou max.";
        return null;
    }

    private static TimeSpan? ParseDuration(string value, out string? problem)
    {
        problem = null;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        problem = $"--duration '{value}' é inválido: use um número de segundos maior que zero.";
        return null;
    }

    private static LogLevel ParseLogLevel(string value, out string? problem)
    {
        problem = null;
        var index = Array.IndexOf(LogLevelNames, value.ToLowerInvariant());

        if (index >= 0)
        {
            return (LogLevel)index;
        }

        problem = $"--log-level '{value}' não existe. Use: {string.Join(", ", LogLevelNames)}.";
        return LogLevel.Warning;
    }

    private static ParsedArguments Failure(string message) => new(null, message, ShowHelp: false);
}
