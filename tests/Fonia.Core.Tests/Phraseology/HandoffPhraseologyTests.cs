using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Phraseology;

public class HandoffPhraseologyTests
{
    private const string Callsign = "PAPA ROMEO ALFA BRAVO CHARLIE";

    private readonly PortuguesePhraseology _phraseology = new();

    private static readonly PhraseContext Solo = new(
        "PRABC",
        AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Ground, AirportFrequencyType.Tower)));

    private string? Say(AtcInstruction instruction) => _phraseology.Speak(instruction, Solo)?.Text;

    // Art. 125, II ("próximo do ponto de espera chame Torre 118,1"), with the name of the Torre as in the practice of VATSIM Brasil.
    [Fact]
    public void The_handoff_names_the_torre_and_says_its_frequency_digit_by_digit()
    {
        Say(new HandoffToTower(new Frequency(118550))).ShouldBe($"{Callsign}, chame Torre Curitiba UNO UNO OITO DECIMAL CINCO CINCO.");
        Say(new HandoffToTower(new Frequency(118100))).ShouldBe($"{Callsign}, chame Torre Curitiba UNO UNO OITO DECIMAL UNO.");
    }

    [Fact]
    public void The_messages_of_the_app_about_the_frequency_are_notices_not_radio()
    {
        foreach (var instruction in new AtcInstruction[] { new NoTowerAvailable(), new NoOneOnFrequency(new Frequency(121500)), new TowerPhraseologyPending() })
        {
            _phraseology.Speak(instruction, Solo)!.Kind.ShouldBe(UtteranceKind.Notice, instruction.GetType().Name);
        }
    }

    [Fact]
    public void Nobody_on_the_frequency_says_which_one_and_which_ones_the_airport_has()
    {
        var text = Say(new NoOneOnFrequency(new Frequency(121500)));

        text.ShouldNotBeNull();
        text.ShouldContain("121.5");
        text.ShouldContain("SBXX");
        text.ShouldContain("Solo 118.0");
        text.ShouldContain("Torre 118.1");
    }

    [Fact]
    public void Without_a_torre_the_notice_says_there_is_none()
    {
        Say(new NoTowerAvailable()).ShouldNotBeNull().ShouldContain("SBXX");
    }

    [Fact]
    public void The_torre_that_cannot_speak_yet_says_so_and_points_back_to_the_solo()
    {
        var onTower = new PhraseContext("PRABC", FrequencyResolver.Apply(Solo.Airport, Solo.Airport.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency));

        var text = _phraseology.Speak(new TowerPhraseologyPending(), onTower)!.Text;

        text.ShouldContain("TORRE Curitiba");
        text.ShouldContain("Solo 118.0");
    }
}
