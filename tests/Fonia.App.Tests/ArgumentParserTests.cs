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
        foreach (var option in new[] { "--sim", "--replay", "--speed", "--record", "--duration", "--log-level", "--help" })
        {
            ArgumentParser.HelpText.ShouldContain(option);
        }
    }

    [Fact]
    public void Later_options_override_earlier_ones_of_the_same_kind()
    {
        Ok("--sim", "mock", "--sim", "msfs").Source.ShouldBe(SimSourceKind.Msfs);
    }
}
