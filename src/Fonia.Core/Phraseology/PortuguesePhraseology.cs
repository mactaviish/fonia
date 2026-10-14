using Fonia.Core.Airports;
using Fonia.Core.Atc;

namespace Fonia.Core.Phraseology;

/// <summary>
/// The controller's phrases in Brazilian Portuguese, from the MCA 100-16 (edition 2025); the catalog with the article
/// of each one is in docs/phraseology/solo-pt-br.md. Numbers, letters and callsigns come from <see cref="SpokenText"/>
/// and are written in capitals, the words of the phrase in lowercase.
/// </summary>
public sealed class PortuguesePhraseology : IPhraseology
{
    public Utterance? Speak(AtcInstruction instruction, PhraseContext context)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(context);

        var callsign = SpokenText.Callsign(context.Callsign);

        return instruction switch
        {
            NoResponse => null,
            SayAgainRequest => new Utterance($"{callsign}, repita."),
            // TODO(fraseologia): the manual has no phrase for asking the callsign again; this one is a proposal to confirm.
            RequestCallsign => new Utterance($"Estação chamando {UnitName(context.Airport)}, repita o indicativo."),
            GoAhead => new Utterance($"{callsign}, prossiga."),
            // TODO(fraseologia): the phrase of the AFIS saying it only informs is a proposal to confirm.
            NoControlService { Unit: ServiceUnit.Afis } => new Utterance($"{callsign}, {UnitName(context.Airport)}, sem serviço de controle neste aeródromo, apenas informações de voo."),
            NoControlService => new Utterance($"Sem serviço de controle em {context.Airport.Airport.Ident}: ninguém responde neste aeródromo.", UtteranceKind.Notice),
            HandoffToTower handoff => new Utterance($"{callsign}, chame Torre {context.Airport.Airport.SpokenName} {SpokenText.Frequency(handoff.Frequency)}."),
            NoTowerAvailable => new Utterance($"Sem Torre em {context.Airport.Airport.Ident}: não há para onde passar o voo.", UtteranceKind.Notice),
            NoOneOnFrequency silent => new Utterance(
                $"Ninguém responde em {Mhz(silent.Frequency)}. Sintonize uma frequência de {context.Airport.Airport.Ident}: {FrequenciesOf(context.Airport)}.",
                UtteranceKind.Notice),
            CallTheTower tower => new Utterance(
                $"{UnitName(context.Airport)}: o Solo não trata de chegada neste app: chame a Torre em {Mhz(tower.Frequency)}.",
                UtteranceKind.Notice),
            CallTheSolo solo => new Utterance(
                $"{UnitName(context.Airport)}: a Torre não trata de acionamento, pushback nem táxi neste app: chame o Solo em {Mhz(solo.Frequency)}.",
                UtteranceKind.Notice),
            NoRunwayAvailable => new Utterance($"Os dados de {context.Airport.Airport.Ident} não têm pista utilizável: o controlador não pode autorizar o táxi.", UtteranceKind.Notice),
            RequestReadbackCorrection correction => new Utterance($"{callsign}, negativo, {Body(correction.Expected)}"),
            _ => new Utterance($"{callsign}, {Body(instruction)}"),
        };
    }

    // What the instruction says without the callsign in front, so a correction can repeat it after "negativo".
    private static string Body(AtcInstruction instruction) => instruction switch
    {
        StartupApproved => "acionamento aprovado, chame pronto para o táxi.",
        PushbackApproved { WithStartup: false } => "pushback aprovado, reporte pronto para o táxi.",

        // The manual has no combined phrase; this is the practice of VATSIM Brasil (P-ACN-PSH-OK).
        PushbackApproved { WithStartup: true } => "autorizado pushback e acionamento, chame pronto para o táxi.",

        // MCA 100-16 Art. 125, II, without the taxiway: the airport data has none. Without the altimeter setting too:
        // TODO(fraseologia): say the QNH ("ajuste de altímetro") when there is a source for it.
        TaxiClearance taxi => $"autorizado táxi para o ponto de espera da {SpokenText.Runway(taxi.Runway)}.",

        // MCA 100-16 Art. 126, II: the runway, the clearance and then the wind. "Alinhe e mantenha" is composed from the manual's
        // examples (see LineUpAndWait). Art. 50: "decolagem" only in the clearance (or its cancellation).
        LineUpAndWait lineUp => $"{SpokenText.Runway(lineUp.Runway)}, alinhe e mantenha.",
        // Art. 129 to 132: the circuit clearance says the clearance first and then the runway; the landing, the runway first. Both end with the wind.
        CircuitClearance circuit => $"autorizado para o circuito de tráfego, {SpokenText.Runway(circuit.Runway)}{WindPart(circuit.Wind)}, reporte perna do vento.",
        ReportBase => "reporte perna base.",
        ReportOnFinal => "reporte na final.",
        LandingClearance landing => $"{SpokenText.Runway(landing.Runway)}, pouso autorizado{WindPart(landing.Wind)}.",
        GoAroundNoted => "ciente.",

        // Art. 127: the minutes only, digit by digit (Art. 25).
        AirborneNotice airborne => $"decolado aos {SpokenText.Digits(airborne.Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture))}.",
        TakeoffClearance takeoff => $"{SpokenText.Runway(takeoff.Runway)}, autorizado decolagem{(takeoff.Wind is { } wind ? $", {SpokenText.Wind(wind)}" : string.Empty)}.",
        _ => throw new ArgumentOutOfRangeException(nameof(instruction), instruction, "There is no phrase for this instruction."),
    };

    private static string WindPart(Sim.Wind? wind) => wind is { } known ? $", {SpokenText.Wind(known)}" : string.Empty;

    // For the messages of the app, which are read on the screen: "118.55", not digit by digit.
    private static string Mhz(Sim.Frequency frequency) => frequency.MHz.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);

    private static string FrequenciesOf(AirportContext airport)
    {
        var listed = new[] { (AirportFrequencyType.Ground, "Solo"), (AirportFrequencyType.Tower, "Torre"), (AirportFrequencyType.Afis, "Rádio") }
            .Select(unit => airport.Airport.FrequencyOf(unit.Item1) is { } f ? $"{unit.Item2} {Mhz(f.Frequency)}" : null)
            .OfType<string>()
            .ToList();

        return listed.Count > 0 ? string.Join(", ", listed) : "o aeroporto não tem frequência nos dados";
    }

    private static string BackToSolo(AirportContext airport) =>
        airport.Airport.FrequencyOf(AirportFrequencyType.Ground) is { } ground ? $" Volte para o Solo {Mhz(ground.Frequency)}." : string.Empty;

    // "SOLO Curitiba", "TORRE Guarulhos", "RÁDIO Marte" (MCA 100-16 Art. 37).
    public string UnitName(AirportContext airport)
    {
        ArgumentNullException.ThrowIfNull(airport);

        return $"{UnitWord(airport.Unit)} {airport.Airport.SpokenName}";
    }

    private static string UnitWord(ServiceUnit unit) => unit switch
    {
        ServiceUnit.Ground => "SOLO",
        ServiceUnit.Tower => "TORRE",
        _ => "RÁDIO",
    };

}
