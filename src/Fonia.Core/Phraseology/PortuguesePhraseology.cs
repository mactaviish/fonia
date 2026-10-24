using Fonia.Core.Airports;
using Fonia.Core.Atc;

namespace Fonia.Core.Phraseology;

/// <summary>
/// The controller's phrases in Brazilian Portuguese, from the MCA 100-16 (edition 2025); the catalog with the article
/// of each one is in docs/phraseology/solo-pt-br.md. Numbers, letters and callsigns come from <see cref="SpokenText"/>
/// and are written in capitals, the words of the phrase in lowercase.
/// </summary>
public sealed class PortuguesePhraseology(TelephonyTable? telephony = null) : IPhraseology
{
    public Utterance? Speak(AtcInstruction instruction, PhraseContext context)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(context);

        var callsign = SpokenText.Callsign(context.Callsign, telephony: telephony);

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
            // Art. 759: the unit informs the altimeter setting; Art. 767: an outdated information is corrected at once.
            AtisReceived { Current: { } current } outdated when current != outdated.Said
                => new Utterance($"{callsign}, informação {SpokenText.Spell(current.ToString())} em vigor{AltimeterPart(outdated.Qnh)}."),
            AtisReceived { Qnh: { } qnh } when qnh is >= 900 and <= 1099 => new Utterance($"{callsign}, ajuste de altímetro {SpokenText.Qnh(qnh)}."),
            AtisReceived => null,
            DepartureInformation information => new Utterance($"{callsign}, {Conditions(information, context.Now)}."),
            CallTheTower tower when context.Airport.Unit == ServiceUnit.Approach => new Utterance(
                $"{UnitName(context.Airport)}: o Controle não atende este pedido neste app: chame a Torre em {Mhz(tower.Frequency)}.",
                UtteranceKind.Notice),
            CallTheTower tower => new Utterance(
                $"{UnitName(context.Airport)}: o Solo não trata de chegada neste app: chame a Torre em {Mhz(tower.Frequency)}.",
                UtteranceKind.Notice),
            // Art. 127: "decolado aos 26, chame o Controle Rio, 119,0" (the name is the airport's, as for the Torre and the Solo).
            AirborneNotice { Approach: { } approach } airborne => new Utterance(
                $"{callsign}, decolado aos {Minutes(airborne.Minute)}, chame Controle {context.Airport.Airport.SpokenName} {SpokenText.Frequency(approach)}."),
            // Art. 133: "no solo aos 02, após livrar a pista chame Solo ..." (the name of the Solo is the practice of VATSIM Brasil, as for the Torre).
            LandedNotice { Solo: { } solo } landed => new Utterance(
                $"{callsign}, no solo aos {Minutes(landed.Minute)}, após livrar a pista chame Solo {context.Airport.Airport.SpokenName} {SpokenText.Frequency(solo)}."),
            LandedNotice landed => new Utterance($"{callsign}, no solo aos {Minutes(landed.Minute)}."),
            HandoffToGround ground => new Utterance($"{callsign}, chame Solo {context.Airport.Airport.SpokenName} {SpokenText.Frequency(ground.Frequency)}."),
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

        // MCA 100-16 Art. 125, II, without the taxiway: the airport data has none. The altimeter setting comes when there is a METAR.
        TaxiClearance taxi => $"autorizado táxi para o ponto de espera da {SpokenText.Runway(taxi.Runway)}{ViaPart(taxi.Taxiways)}{AltimeterPart(taxi.Qnh)}.",

        // MCA 100-16 Art. 126, II: the runway, the clearance and then the wind. "Alinhe e mantenha" is composed from the manual's
        // examples (see LineUpAndWait). Art. 50: "decolagem" only in the clearance (or its cancellation).
        LineUpAndWait lineUp => $"{SpokenText.Runway(lineUp.Runway)}, alinhe e mantenha.",
        // Art. 129 to 132: the circuit clearance says the clearance first and then the runway; the landing, the runway first. Both end with the wind.
        CircuitClearance circuit => $"autorizado para o circuito de tráfego, {SpokenText.Runway(circuit.Runway)}{WindPart(circuit.Wind)}{AltimeterPart(circuit.Qnh)}, reporte perna do vento.",
        ReportBase => "reporte perna base.",
        ReportOnFinal => "reporte na final.",
        // Art. 129: "autorizada aproximação direta, pista 11, vento 150 graus, 15 kt, QNH 1013. Reporte final."
        StraightInClearance straightIn => $"autorizada aproximação direta, {SpokenText.Runway(straightIn.Runway)}{WindPart(straightIn.Wind)}{AltimeterPart(straightIn.Qnh)}, reporte final.",
        LandingClearance landing => $"{SpokenText.Runway(landing.Runway)}, pouso autorizado{WindPart(landing.Wind)}.",
        GoAroundNoted => "ciente.",

        // The practice the maintainer gave, not the manual (see CrossRunway): "cruze a pista uno zero esquerda, após chame Solo em ...", without the airport.
        CrossRunway { Solo: { } solo } crossing => $"cruze a {SpokenText.Runway(crossing.Runway)}, após chame Solo em {SpokenText.Frequency(solo)}.",
        Standby => "aguarde.",

        CrossRunway { LineUpRunway: { } lineUp } crossing => $"cruze a {SpokenText.Runway(crossing.Runway)}, alinhe e mantenha {SpokenText.Runway(lineUp)}.",
        CrossRunway { HoldAfter: true } crossing => $"cruze a {SpokenText.Runway(crossing.Runway)} e mantenha antes da barra de parada.",
        CrossRunway crossing => $"cruze a {SpokenText.Runway(crossing.Runway)}.",

        // Art. 134, II: "taxie para o pátio" (without the apron and the taxiway).
        TaxiToApron => "taxie para o pátio.",

        // Art. 127: the minutes only, digit by digit (Art. 25).
        AirborneNotice airborne => $"decolado aos {Minutes(airborne.Minute)}.",
        TakeoffClearance takeoff => $"{SpokenText.Runway(takeoff.Runway)}, autorizado decolagem{(takeoff.Wind is { } wind ? $", {SpokenText.Wind(wind)}" : string.Empty)}.",
        _ => throw new ArgumentOutOfRangeException(nameof(instruction), instruction, "There is no phrase for this instruction."),
    };

    // The minutes only, digit by digit (Art. 25): "ZERO DOIS".
    private static string Minutes(int minute) => SpokenText.Digits(minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture));

    // Art. 122, I: the runway in use, the wind, the altimeter setting, the temperature, the visibility and the time check, in that order.
    private static string Conditions(DepartureInformation information, DateTimeOffset? now)
    {
        var parts = new List<string>();

        if (information.Runway is { } runway)
        {
            parts.Add(SpokenText.Runway(runway));
        }

        if (information.Wind is { } wind)
        {
            parts.Add(SpokenText.Wind(wind));
        }

        if (information.Qnh is { } qnh && qnh is >= 900 and <= 1099)
        {
            parts.Add($"ajuste de altímetro {SpokenText.Qnh(qnh)}");
        }

        if (information.TemperatureCelsius is { } temperature)
        {
            parts.Add($"temperatura {(temperature < 0 ? "MENOS " : string.Empty)}{SpokenText.Digits(Math.Abs(temperature).ToString("00", System.Globalization.CultureInfo.InvariantCulture))}");
        }

        if (information.VisibilityMeters is { } visibility)
        {
            parts.Add($"visibilidade {SpokenText.Visibility(visibility)}");
        }

        if (now is { } time)
        {
            parts.Add($"hora certa {Minutes(time.UtcDateTime.Minute)}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "sem informações disponíveis";
    }

    // "ajuste de altímetro UNO ZERO UNO OITO" (Art. 29), said in full: the abbreviation is no word for the voice.
    private static string AltimeterPart(int? qnh) => qnh is { } hectopascals && hectopascals is >= 900 and <= 1099 ? $", ajuste de altímetro {SpokenText.Qnh(hectopascals)}" : string.Empty;

    // Art. 125, II: ", via taxiway ALFA, BRAVO" (the letters in the phonetic alphabet, in the order of the route).
    private static string ViaPart(IReadOnlyList<string>? taxiways) =>
        taxiways is { Count: > 0 } ? $", via taxiway {string.Join(", ", taxiways.Select(SpokenText.Spell))}" : string.Empty;

    private static string WindPart(Sim.Wind? wind) => wind is { } known ? $", {SpokenText.Wind(known)}" : string.Empty;

    // For the messages of the app, which are read on the screen: "118.55", not digit by digit.
    private static string Mhz(Sim.Frequency frequency) => frequency.MHz.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);

    private static string FrequenciesOf(AirportContext airport)
    {
        var listed = new[] { (AirportFrequencyType.Ground, "Solo"), (AirportFrequencyType.Tower, "Torre"), (AirportFrequencyType.Afis, "Rádio"), (AirportFrequencyType.Atis, "ATIS") }
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
        ServiceUnit.Approach => "CONTROLE",
        _ => "RÁDIO",
    };

}
