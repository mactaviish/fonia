using Fonia.Core.Speech;
using Microsoft.Extensions.Logging;

namespace Fonia.App.Tests;

public class ArgumentParserTests
{
    private static AppOptions Ok(params string[] args)
    {
        var parsed = ArgumentParser.Parse(args);

        parsed.Error.ShouldBeNull();
        parsed.ShowHelp.ShouldBeFalse();
        return parsed.Options.ShouldNotBeNull();
    }

    private static string Error(params string[] args)
    {
        var parsed = ArgumentParser.Parse(args);

        parsed.Options.ShouldBeNull();
        return parsed.Error.ShouldNotBeNull();
    }

    [Fact]
    public void Without_arguments_it_reads_the_real_msfs_quietly()
    {
        var options = Ok();

        options.Source.ShouldBe(SimSourceKind.Msfs);
        options.ReplayPath.ShouldBeNull();
        options.RecordPath.ShouldBeNull();
        options.Duration.ShouldBeNull();
        options.Speed.ShouldBe(1.0);
        options.LogLevel.ShouldBe(LogLevel.Warning);
    }

    [Theory]
    [InlineData("msfs", "Msfs")]
    [InlineData("MOCK", "Mock")]
    [InlineData("Mock", "Mock")]
    public void The_simulator_source_is_chosen_with_sim(string value, string expected)
    {
        // The enum is internal, so a public test method compares its name.
        Ok("--sim", value).Source.ToString().ShouldBe(expected);
    }

    [Fact]
    public void Replay_with_a_file_selects_the_replay_source()
    {
        var options = Ok("--replay", "session.jsonl");

        options.Source.ShouldBe(SimSourceKind.Replay);
        options.ReplayPath.ShouldBe("session.jsonl");
    }

    [Fact]
    public void Sim_replay_together_with_a_file_is_accepted()
    {
        Ok("--sim", "replay", "--replay", "s.jsonl").Source.ShouldBe(SimSourceKind.Replay);
    }

    [Fact]
    public void Options_can_be_written_with_an_equals_sign()
    {
        var options = Ok("--sim=mock", "--record=out.jsonl", "--duration=30", "--log-level=debug");

        options.Source.ShouldBe(SimSourceKind.Mock);
        options.RecordPath.ShouldBe("out.jsonl");
        options.Duration.ShouldBe(TimeSpan.FromSeconds(30));
        options.LogLevel.ShouldBe(LogLevel.Debug);
    }

    [Theory]
    [InlineData("2", 2.0)]
    [InlineData("0.5", 0.5)]
    [InlineData("1.5", 1.5)]
    [InlineData("max", double.PositiveInfinity)]
    [InlineData("MAX", double.PositiveInfinity)]
    public void Speed_is_a_positive_number_or_max(string value, double expected)
    {
        Ok("--replay", "s.jsonl", "--speed", value).Speed.ShouldBe(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("fast")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void An_invalid_speed_is_rejected(string value)
    {
        Error("--replay", "s.jsonl", "--speed", value).ShouldContain("--speed");
    }

    [Fact]
    public void Speed_only_makes_sense_for_a_replay()
    {
        Error("--sim", "mock", "--speed", "2").ShouldContain("--speed");
        Error("--speed", "2").ShouldContain("--replay");
    }

    [Fact]
    public void Record_works_with_every_source()
    {
        Ok("--record", "a.jsonl").RecordPath.ShouldBe("a.jsonl");
        Ok("--sim", "mock", "--record", "a.jsonl").RecordPath.ShouldBe("a.jsonl");
        Ok("--replay", "s.jsonl", "--record", "a.jsonl").RecordPath.ShouldBe("a.jsonl");
    }

    [Fact]
    public void Duration_is_a_positive_number_of_seconds()
    {
        Ok("--duration", "90").Duration.ShouldBe(TimeSpan.FromSeconds(90));
        Ok("--duration", "0.5").Duration.ShouldBe(TimeSpan.FromSeconds(0.5));
        Error("--duration", "0").ShouldContain("--duration");
        Error("--duration", "-3").ShouldContain("--duration");
        Error("--duration", "soon").ShouldContain("--duration");
    }

    [Theory]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("information", LogLevel.Information)]
    [InlineData("warning", LogLevel.Warning)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("critical", LogLevel.Critical)]
    [InlineData("none", LogLevel.None)]
    public void The_log_level_accepts_the_usual_names(string value, LogLevel expected)
    {
        Ok("--log-level", value).LogLevel.ShouldBe(expected);
    }

    [Fact]
    public void An_unknown_log_level_is_rejected_and_the_valid_ones_are_listed()
    {
        var error = Error("--log-level", "loud");

        error.ShouldContain("--log-level");
        error.ShouldContain("warning");
    }

    [Fact]
    public void An_unknown_simulator_is_rejected_and_the_valid_ones_are_listed()
    {
        var error = Error("--sim", "xplane");

        error.ShouldContain("xplane");
        error.ShouldContain("msfs");
        error.ShouldContain("mock");
    }

    [Fact]
    public void Sim_replay_needs_a_file()
    {
        Error("--sim", "replay").ShouldContain("--replay");
    }

    [Theory]
    [InlineData("msfs")]
    [InlineData("mock")]
    public void A_replay_file_cannot_be_combined_with_another_source(string sim)
    {
        Error("--sim", sim, "--replay", "s.jsonl").ShouldContain("--replay");
    }

    [Theory]
    [InlineData("--sim")]
    [InlineData("--replay")]
    [InlineData("--record")]
    [InlineData("--speed")]
    [InlineData("--duration")]
    [InlineData("--log-level")]
    public void An_option_without_its_value_is_rejected(string option)
    {
        Error(option).ShouldContain(option);
        Error(option, "--help").ShouldContain(option); // the next token is another option, not a value
    }

    [Fact]
    public void An_unknown_option_or_a_loose_word_is_rejected()
    {
        Error("--colour", "red").ShouldContain("--colour");
        Error("whatever").ShouldContain("whatever");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void Help_is_requested_by_any_usual_flag(string flag)
    {
        var parsed = ArgumentParser.Parse(["--sim", "mock", flag]);

        parsed.ShowHelp.ShouldBeTrue();
        parsed.Error.ShouldBeNull();
    }

    [Fact]
    public void The_help_text_describes_every_option()
    {
        foreach (var option in new[] { "--sim", "--replay", "--speed", "--record", "--duration", "--log-level", "--atc", "--runway", "--callsign", "--mic-test", "--mic-list", "--mic-device", "--ptt-key", "--transcribe", "--stt-dir", "--stt-model", "--stt-gpu", "--speak", "--speak-out", "--tts-voice", "--tts-dir", "--radio-effect", "--help" })
        {
            ArgumentParser.HelpText.ShouldContain(option);
        }
    }

    [Fact]
    public void Later_options_override_earlier_ones_of_the_same_kind()
    {
        Ok("--sim", "mock", "--sim", "msfs").Source.ShouldBe(SimSourceKind.Msfs);
    }

    [Fact]
    public void The_atc_mode_takes_the_airport_in_capitals_and_reads_the_simulator_by_default()
    {
        var options = Ok("--atc", "sbct");

        options.AtcAirport.ShouldBe("SBCT");
        options.Runway.ShouldBeNull();
        options.Callsign.ShouldBeNull();
        options.Source.ShouldBe(SimSourceKind.Msfs);
    }

    [Fact]
    public void The_atc_mode_accepts_runway_callsign_and_log_level()
    {
        var options = Ok("--atc=SBCT", "--runway", "15", "--callsign", "PR-ABC", "--log-level", "error");

        options.AtcAirport.ShouldBe("SBCT");
        options.Runway.ShouldBe("15");
        options.Callsign.ShouldBe("PR-ABC");
        options.LogLevel.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public void The_atc_mode_can_read_the_callsign_from_the_mock_simulator()
    {
        Ok("--atc", "SBCT", "--sim", "mock").Source.ShouldBe(SimSourceKind.Mock);
    }

    [Theory]
    [InlineData("--runway", "15")]
    [InlineData("--callsign", "PRABC")]
    public void Runway_and_callsign_only_make_sense_in_the_atc_mode(string option, string value)
    {
        Error(option, value).ShouldContain("--atc");
    }

    [Theory]
    [InlineData("--replay", "x.jsonl")]
    [InlineData("--record", "x.jsonl")]
    [InlineData("--speed", "2")]
    [InlineData("--duration", "5")]
    public void The_atc_mode_does_not_mix_with_the_telemetry_options(string option, string value)
    {
        Error("--atc", "SBCT", option, value).ShouldContain("--atc não combina");
    }

    [Fact]
    public void A_callsign_makes_the_simulator_option_pointless_and_is_refused_to_avoid_confusion()
    {
        Error("--atc", "SBCT", "--callsign", "PRABC", "--sim", "mock").ShouldContain("--callsign dispensa o simulador");
    }

    [Fact]
    public void The_atc_option_needs_a_value()
    {
        Error("--atc").ShouldContain("precisa de um valor");
    }

    [Fact]
    public void The_microphone_test_takes_the_file_and_has_defaults_for_the_rest()
    {
        var options = Ok("--mic-test", "fala.wav");

        options.MicTestPath.ShouldBe("fala.wav");
        options.PttKey.ShouldBe("RControl");
        options.MicDevice.ShouldBe(-1);
        options.ListMics.ShouldBeFalse();
    }

    [Fact]
    public void The_microphone_test_accepts_the_device_and_the_key()
    {
        var options = Ok("--mic-test", "fala.wav", "--mic-device", "2", "--ptt-key", "mouse4", "--log-level", "error");

        options.MicDevice.ShouldBe(2);
        options.PttKey.ShouldBe("mouse4");
    }

    [Fact]
    public void Listing_the_microphones_takes_no_value()
    {
        Ok("--mic-list").ListMics.ShouldBeTrue();
    }

    [Theory]
    [InlineData("--mic-device", "1")]
    [InlineData("--ptt-key", "F8")]
    public void The_device_and_the_key_only_make_sense_with_the_microphone_test(string option, string value)
    {
        Error(option, value).ShouldContain("--mic-test");
    }

    [Theory]
    [InlineData("--atc", "SBCT")]
    [InlineData("--sim", "mock")]
    [InlineData("--replay", "x.jsonl")]
    [InlineData("--record", "x.jsonl")]
    [InlineData("--speed", "2")]
    [InlineData("--duration", "5")]
    public void The_microphone_test_does_not_mix_with_the_other_modes(string option, string value)
    {
        Error("--mic-test", "fala.wav", option, value).ShouldContain("--mic-test não combina");
    }

    [Fact]
    public void Listing_the_microphones_does_not_mix_with_anything_else()
    {
        Error("--mic-list", "--atc", "SBCT").ShouldContain("--mic-list");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-2")]
    [InlineData("1.5")]
    public void A_bad_device_number_is_refused(string value)
    {
        Error("--mic-test", "f.wav", "--mic-device", value).ShouldContain("--mic-device");
    }

    [Fact]
    public void An_unknown_key_name_is_refused_with_examples()
    {
        var message = Error("--mic-test", "f.wav", "--ptt-key", "banana");

        message.ShouldContain("--ptt-key");
        message.ShouldContain("RControl");
    }

    [Fact]
    public void The_microphone_test_needs_a_file()
    {
        Error("--mic-test").ShouldContain("precisa de um valor");
    }

    [Fact]
    public void Transcribing_takes_the_file_and_has_defaults_for_the_rest()
    {
        var options = Ok("--transcribe", "fala.wav");

        options.TranscribePath.ShouldBe("fala.wav");
        options.SttDirectory.ShouldBeNull();
        options.SttModel.ShouldBeNull();
        options.SttGpu.ShouldBeFalse();
        options.Callsign.ShouldBeNull();
    }

    [Fact]
    public void Transcribing_accepts_the_callsign_the_model_the_folder_and_the_gpu()
    {
        var options = Ok("--transcribe", "fala.wav", "--callsign", "PR-ABC", "--stt-dir", "C:/modelos", "--stt-model", "ggml-base.bin", "--stt-gpu", "--log-level", "error");

        options.Callsign.ShouldBe("PR-ABC");
        options.SttDirectory.ShouldBe("C:/modelos");
        options.SttModel.ShouldBe("ggml-base.bin");
        options.SttGpu.ShouldBeTrue();
    }

    [Theory]
    [InlineData("--stt-dir", "x")]
    [InlineData("--stt-model", "x.bin")]
    public void The_speech_model_options_only_make_sense_when_transcribing(string option, string value)
    {
        Error(option, value).ShouldContain("--transcribe");
    }

    [Fact]
    public void The_gpu_flag_only_makes_sense_when_transcribing()
    {
        Error("--stt-gpu").ShouldContain("--transcribe");
    }

    [Theory]
    [InlineData("--atc", "SBCT")]
    [InlineData("--sim", "mock")]
    [InlineData("--replay", "x.jsonl")]
    [InlineData("--record", "x.jsonl")]
    [InlineData("--speed", "2")]
    [InlineData("--duration", "5")]
    [InlineData("--runway", "15")]
    [InlineData("--mic-test", "x.wav")]
    public void Transcribing_does_not_mix_with_the_other_modes(string option, string value)
    {
        Error("--transcribe", "fala.wav", option, value).ShouldContain("--transcribe não combina");
    }

    [Fact]
    public void Transcribing_needs_a_file()
    {
        Error("--transcribe").ShouldContain("precisa de um valor");
    }

    [Fact]
    public void Speaking_takes_the_text_and_has_defaults_for_the_rest()
    {
        var options = Ok("--speak", "Repita.");

        options.SpeakText.ShouldBe("Repita.");
        options.SpeakOut.ShouldBe("fonia-speak.wav");
        options.TtsVoice.ShouldBeNull();
        options.TtsDirectory.ShouldBeNull();
        options.RadioEffect.ShouldBe(RadioIntensity.Medium);
    }

    [Fact]
    public void Speaking_accepts_the_file_the_voice_the_effect_and_the_folder()
    {
        var options = Ok("--speak", "Repita.", "--speak-out", "x.wav", "--tts-voice", "pm_alex", "--radio-effect", "forte", "--tts-dir", "C:/modelos", "--log-level", "error");

        options.SpeakOut.ShouldBe("x.wav");
        options.TtsVoice.ShouldBe("pm_alex");
        options.RadioEffect.ShouldBe(RadioIntensity.Strong);
        options.TtsDirectory.ShouldBe("C:/modelos");
    }

    [Theory]
    [InlineData("aleatoria")]
    [InlineData("random")]
    [InlineData("ALEATÓRIA")]
    public void The_voice_may_be_left_to_chance_by_name(string name)
    {
        Ok("--speak", "x", "--tts-voice", name).TtsVoice.ShouldBeNull();
    }

    [Theory]
    [InlineData("--speak-out", "x.wav")]
    [InlineData("--tts-voice", "pf_dora")]
    [InlineData("--tts-dir", "x")]
    [InlineData("--radio-effect", "leve")]
    public void The_speech_options_only_make_sense_when_speaking(string option, string value)
    {
        Error(option, value).ShouldContain("--speak");
    }

    [Theory]
    [InlineData("--atc", "SBCT")]
    [InlineData("--sim", "mock")]
    [InlineData("--replay", "x.jsonl")]
    [InlineData("--speed", "2")]
    [InlineData("--mic-test", "x.wav")]
    [InlineData("--transcribe", "x.wav")]
    public void Speaking_does_not_mix_with_the_other_modes(string option, string value)
    {
        Error("--speak", "x", option, value).ShouldContain("--speak não combina");
    }

    [Fact]
    public void An_unknown_voice_is_refused_with_the_ones_that_exist()
    {
        var message = Error("--speak", "x", "--tts-voice", "pm_santa");

        message.ShouldContain("--tts-voice");
        message.ShouldContain("pf_dora");
        message.ShouldContain("pm_alex");
    }

    [Fact]
    public void An_unknown_effect_is_refused_with_the_ones_that_exist()
    {
        var message = Error("--speak", "x", "--radio-effect", "altissimo");

        message.ShouldContain("--radio-effect");
        message.ShouldContain("média");
    }

    [Fact]
    public void Speaking_needs_a_text()
    {
        Error("--speak").ShouldContain("precisa de um valor");
    }
}
