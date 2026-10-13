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
            SayAgainRequest => new Utterance("Repita."),
            // TODO(fraseologia): the manual has no phrase for asking the callsign again; this one is a proposal to confirm.
            RequestCallsign => new Utterance($"Estação chamando {UnitName(context.Airport)}, repita o indicativo."),
            GoAhead => new Utterance($"{callsign}, prossiga."),
            // TODO(fraseologia): the phrase of the AFIS saying it only informs is a proposal to confirm.
            NoControlService { Unit: ServiceUnit.Afis } => new Utterance($"{callsign}, {UnitName(context.Airport)}, sem serviço de controle neste aeródromo, apenas informações de voo."),
            NoControlService => new Utterance($"Sem serviço de controle em {context.Airport.Airport.Ident}: ninguém responde neste aeródromo.", UtteranceKind.Notice),
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
        _ => throw new ArgumentOutOfRangeException(nameof(instruction), instruction, "There is no phrase for this instruction."),
    };

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
