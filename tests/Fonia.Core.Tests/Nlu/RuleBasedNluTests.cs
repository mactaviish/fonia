using Fonia.Core.Atc;
using Fonia.Core.Nlu;

namespace Fonia.Core.Tests.Nlu;

public class RuleBasedNluTests
{
    private const string Own = "PRABC";

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBCT", null);

    private static readonly FlightContext AwaitingReadback =
        new(Own, FlightPhase.Parked, "SBCT", new TaxiClearance("15"));

    private readonly RuleBasedNlu _nlu = new();

    // The manual's and the practice's pilot phrases (docs/phraseology/solo-pt-br.md), with the callsign adapted.
    [Theory]
    [InlineData("Solo Curitiba, PR-ABC, box 3, IFR para Brasília, solicita acionar.", "box 3")]
    [InlineData("Solo Curitiba, PRABC, solicito acionamento", null)]
    [InlineData("solo curitiba papa romeo alfa bravo charlie solicita partida dos motores", null)]
    public void A_request_to_start_up_is_understood(string said, string? stand)
    {
        _nlu.Interpret(said, Parked).ShouldBe(new RequestStartup(Own, stand));
    }

    [Theory]
    [InlineData("Solo Curitiba, PRABC, box 3, IFR para Brasília, solicita pushback.", false, "box 3")]
    [InlineData("Solo Curitiba, PRABC, pátio 1, posição 14, solicita autorização para pushback e acionamento.", true, "patio 1")]
    [InlineData("PRABC solicita pushback e acionamento", true, null)]
    [InlineData("solicito acionamento e pushback PRABC", true, null)]
    [InlineData("PRABC pede para empurrar", false, null)]
    public void A_request_for_pushback_is_understood_with_or_without_startup(string said, bool withStartup, string? stand)
    {
        _nlu.Interpret(said, Parked).ShouldBe(new RequestPushback(Own, withStartup, stand));
    }

    [Theory]
    [InlineData("Solo Curitiba, PRABC, solicita instruções de táxi.", null)]
    [InlineData("Solo Curitiba, PRABC, pronto para o táxi.", null)]
    [InlineData("pronto para taxiar, PRABC", null)]
    [InlineData("PRABC, táxi para a pista zero nove", "09")]
    [InlineData("PRABC solicita táxi, pista quinze", "15")]
    [InlineData("PRABC solicita táxi pista dois três esquerda", "23L")]
    [InlineData("PRABC solicita táxi pista 33 R", "33R")]
    [InlineData("PRABC solicito táxi pista vinte e nove direita", "29R")]
    public void A_request_for_taxi_is_understood_and_keeps_the_runway_if_one_was_said(string said, string? runway)
    {
        _nlu.Interpret(said, Parked).ShouldBe(new RequestTaxi(Own, runway));
    }

    [Theory]
    [InlineData("Bom dia, Solo Curitiba, PRABC, solicito acionamento, obrigado")]
    [InlineData("ok, PRABC, solicito acionamento")]
    [InlineData("PRABC, solicito acionamento, Solo Curitiba")]
    [InlineData("Solo, alfa bravo charlie, solicito acionamento")]
    [InlineData("SOLO CURITIBA ABC SOLICITA ACIONAMENTO")]
    public void Politeness_order_and_an_abbreviated_callsign_do_not_matter(string said)
    {
        _nlu.Interpret(said, Parked).ShouldBe(new RequestStartup(Own, null));
    }

    [Fact]
    public void A_request_without_a_callsign_is_still_understood_but_has_none()
    {
        _nlu.Interpret("Solo Curitiba, solicito acionamento", Parked).ShouldBe(new RequestStartup(null, null));
    }

    [Fact]
    public void Another_aircrafts_callsign_is_not_taken_for_this_one()
    {
        _nlu.Interpret("Solo Curitiba, GOL 1419, solicita acionar", Parked).Callsign.ShouldBeNull();
    }

    [Fact]
    public void The_digits_of_the_callsign_are_not_taken_for_a_stand_or_a_runway()
    {
        var flight = Parked with { Callsign = "GLO1419" };

        _nlu.Interpret("Solo Curitiba, GLO 1419, solicita táxi", flight).ShouldBe(new RequestTaxi("GLO1419", null));
    }

    [Theory]
    [InlineData("Autorizado pushback e acionamento, chamo pronto para o táxi, PRABC")]
    [InlineData("autorizado táxi para o ponto de espera da pista um cinco, PRABC")]
    [InlineData("PRABC, ciente")]
    [InlineData("pista um cinco, PRABC")]
    public void An_answer_to_an_instruction_is_a_readback_when_one_is_waiting(string said)
    {
        var intent = _nlu.Interpret(said, AwaitingReadback);

        intent.ShouldBeOfType<Readback>().Callsign.ShouldBe(Own);
    }

    [Fact]
    public void A_readback_keeps_what_was_said_without_the_callsign_so_the_engine_can_check_it()
    {
        var intent = _nlu.Interpret("Autorizado táxi, pista um cinco, PRABC", AwaitingReadback);

        intent.ShouldBeOfType<Readback>().Text.ShouldBe("autorizado taxi pista um cinco");
    }

    [Fact]
    public void A_readback_is_recognized_even_when_no_instruction_is_waiting()
    {
        // The engine decides what to do with it; the NLU only says what it heard.
        _nlu.Interpret("Pushback e acionamento autorizados, chamo pronto, PRABC", Parked).ShouldBeOfType<Readback>();
    }

    [Fact]
    public void A_new_request_while_waiting_for_a_readback_is_still_a_request()
    {
        _nlu.Interpret("PRABC, solicito táxi", AwaitingReadback).ShouldBe(new RequestTaxi(Own, null));
    }

    [Fact]
    public void Naming_something_else_than_the_pending_instruction_is_a_request_not_a_readback()
    {
        var startupWaiting = Parked with { PendingReadback = new StartupApproved() };

        _nlu.Interpret("Solo, PRABC, taxi", startupWaiting).ShouldBe(new RequestTaxi(Own, null));
    }

    [Fact]
    public void Naming_the_pending_instruction_without_a_request_word_is_a_readback()
    {
        var startupWaiting = Parked with { PendingReadback = new StartupApproved() };

        _nlu.Interpret("acionamento aprovado, PRABC", startupWaiting).ShouldBeOfType<Readback>();
    }

    [Theory]
    [InlineData("Repita, PRABC")]
    [InlineData("PRABC, pode repetir?")]
    [InlineData("não copiei, PRABC")]
    [InlineData("repete por favor")]
    [InlineData("diga novamente")]
    public void Asking_to_say_it_again_is_understood(string said)
    {
        _nlu.Interpret(said, Parked).ShouldBeOfType<SayAgain>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("blá blá blá")]
    [InlineData("qual é o tempo hoje")]
    public void What_cannot_be_understood_is_unknown_and_keeps_the_text(string said)
    {
        _nlu.Interpret(said, Parked).ShouldBe(new Unknown(said));
    }

    [Theory]
    [InlineData("Solo Curitiba, PRABC")]
    [InlineData("PRABC")]
    [InlineData("alfa bravo charlie, Solo")]
    public void Only_calling_the_controller_with_the_callsign_is_understood(string said)
    {
        _nlu.Interpret(said, Parked).ShouldBe(new CallsignOnly(Own));
    }

    [Fact]
    public void The_callsign_with_numbers_while_an_instruction_waits_is_still_a_readback()
    {
        _nlu.Interpret("pista um cinco, PRABC", AwaitingReadback).ShouldBeOfType<Readback>();
    }

    [Fact]
    public void The_callsign_alone_after_being_asked_for_it_is_not_a_readback_even_with_an_instruction_waiting()
    {
        var asked = AwaitingReadback with { AwaitingCallsign = new RequestTaxi(null, null) };

        _nlu.Interpret("PRABC", asked).ShouldBe(new CallsignOnly(Own));
    }

    [Fact]
    public void Null_text_is_unknown()
    {
        _nlu.Interpret(null, Parked).ShouldBe(new Unknown(string.Empty));
    }
}
