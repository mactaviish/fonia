using System.Text;
using Fonia.Core.Speech;
using Microsoft.Extensions.Logging;

namespace Fonia.App.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fonia-settings-" + Guid.NewGuid().ToString("N"));

    private string Base => Path.Combine(_root, "app");

    private string User => Path.Combine(_root, "user");

    public SettingsTests()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(User);
    }

    public void Dispose() => Directory.Delete(_root, true);

    private static readonly KeyValuePair<string, string?>[] NoEnvironment = [];

    private LoadedSettings Load(params KeyValuePair<string, string?>[] environment) => SettingsLoader.Load(Base, User, environment);

    private void WriteBase(string json) => File.WriteAllText(Path.Combine(Base, "appsettings.json"), json);

    private void WriteUser(string json) => File.WriteAllText(Path.Combine(User, "appsettings.json"), json);

    [Fact]
    public void Without_any_file_the_settings_are_empty_and_that_is_no_problem()
    {
        var loaded = Load();

        loaded.Error.ShouldBeNull();
        loaded.Settings.Voice.PttKey.ShouldBeNull();
        loaded.Files.Count.ShouldBe(2);
        loaded.Files.ShouldAllBe(f => !f.Exists);
    }

    [Fact]
    public void The_file_of_the_user_wins_over_the_one_next_to_the_program_and_the_environment_over_both()
    {
        WriteBase("""{ "voice": { "pttKey": "F8", "radioEffect": "leve" }, "airport": "SBGR" }""");
        WriteUser("""{ "voice": { "pttKey": "RShift" } }""");

        var loaded = Load(new("FONIA_AIRPORT", "SBCT"), new("FONIA_VOICE__MICDEVICE", "2"), new("OTHER", "x"));

        loaded.Error.ShouldBeNull();
        loaded.Settings.Voice.PttKey.ShouldBe("RShift");
        loaded.Settings.Voice.RadioEffect.ShouldBe("leve");
        loaded.Settings.Airport.ShouldBe("SBCT");
        loaded.Settings.Voice.MicDevice.ShouldBe(2);
        loaded.Files.ShouldAllBe(f => f.Exists);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed_because_people_edit_this_by_hand()
    {
        WriteUser("""
            {
              // my key
              "voice": { "pttKey": "F8", },
            }
            """);

        Load().Settings.Voice.PttKey.ShouldBe("F8");
    }

    [Theory]
    [InlineData("""{ "voice": { "pttKye": "F8" } }""", "pttKye")]
    [InlineData("""{ "voice": { "micDevice": "abc" } }""", "micDevice")]
    [InlineData("{ not json", "appsettings.json")]
    public void A_file_with_a_typo_or_a_wrong_value_is_refused_with_its_name_instead_of_being_ignored(string json, string mention)
    {
        WriteUser(json);

        var loaded = Load();

        loaded.Error.ShouldNotBeNull();
        loaded.Error.ShouldContain(mention);
    }

    private static AppOptions Apply(AppSettings settings, AppOptions options, params string[] given)
    {
        var result = SettingsApplier.Apply(options, settings, new HashSet<string>(given, StringComparer.OrdinalIgnoreCase));

        result.Error.ShouldBeNull();
        return result.Options;
    }

    [Fact]
    public void What_the_command_line_did_not_say_comes_from_the_settings()
    {
        var settings = new AppSettings
        {
            Callsign = "PR-ABC",
            LogLevel = "error",
            Weather = { Metar = false },
            Voice = { PttKey = "F8", MicDevice = 1, AudioOut = 2, TtsVoice = "pm_alex", RadioEffect = "forte", SttGpu = true, SttModel = "ggml-base.bin", ModelsDir = "D:/modelos" },
        };

        var options = Apply(settings, new AppOptions(AtcAirport: "SBCT", Voice: true));

        options.Callsign.ShouldBe("PR-ABC");
        options.LogLevel.ShouldBe(LogLevel.Error);
        options.NoMetar.ShouldBeTrue();
        options.PttKey.ShouldBe("F8");
        options.MicDevice.ShouldBe(1);
        options.AudioOut.ShouldBe(2);
        options.TtsVoice.ShouldBe("pm_alex");
        options.RadioEffect.ShouldBe(RadioIntensity.Strong);
        options.SttGpu.ShouldBeTrue();
        options.SttModel.ShouldBe("ggml-base.bin");
        options.SttDirectory.ShouldBe("D:/modelos");
        options.TtsDirectory.ShouldBe("D:/modelos");
        options.ModelsDir.ShouldBe("D:/modelos");
    }

    [Fact]
    public void What_the_command_line_said_wins_even_when_it_is_the_default_value()
    {
        var settings = new AppSettings { Callsign = "PR-ABC", Voice = { PttKey = "F8", MicDevice = 1, RadioEffect = "forte" } };
        var typed = new AppOptions(AtcAirport: "SBCT", Callsign: "PR-XYZ", PttKey: "RControl", MicDevice: -1, RadioEffect: RadioIntensity.Medium);

        var options = Apply(settings, typed, "callsign", "ptt-key", "mic-device", "radio-effect");

        options.Callsign.ShouldBe("PR-XYZ");
        options.PttKey.ShouldBe("RControl");
        options.MicDevice.ShouldBe(-1);
        options.RadioEffect.ShouldBe(RadioIntensity.Medium);
    }

    [Fact]
    public void A_random_voice_is_the_default_voice_and_empty_settings_change_nothing()
    {
        var typed = new AppOptions(AtcAirport: "SBCT");

        Apply(new AppSettings { Voice = { TtsVoice = "aleatoria" } }, typed).TtsVoice.ShouldBeNull();
        Apply(new AppSettings(), typed).ShouldBe(typed);
    }

    [Theory]
    [InlineData("pttKey", "banana")]
    [InlineData("radioEffect", "altissimo")]
    [InlineData("ttsVoice", "robo")]
    [InlineData("logLevel", "muito")]
    public void A_value_the_program_does_not_know_is_refused_with_the_key_that_has_it(string key, string value)
    {
        var settings = new AppSettings();

        switch (key)
        {
            case "pttKey":
                settings.Voice.PttKey = value;
                break;
            case "radioEffect":
                settings.Voice.RadioEffect = value;
                break;
            case "ttsVoice":
                settings.Voice.TtsVoice = value;
                break;
            default:
                settings.LogLevel = value;
                break;
        }

        var result = SettingsApplier.Apply(new AppOptions(AtcAirport: "SBCT"), settings, new HashSet<string>());

        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain(key);
    }

    [Fact]
    public void The_options_given_on_the_command_line_are_listed_by_name()
    {
        ArgumentParser.OptionsGiven(["--atc", "SBCT", "--ptt-key=F8", "--voice", "-h"]).ShouldBe(["atc", "ptt-key", "voice"], ignoreOrder: true);
    }

    [Fact]
    public void The_config_command_shows_the_files_and_what_is_set_and_init_writes_a_file_once()
    {
        WriteUser("""{ "voice": { "pttKey": "F8" }, "airport": "SBCT" }""");
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };

        ConfigCommands.Show(Load(), output);

        var text = output.ToString();
        text.ShouldContain(Path.Combine(User, "appsettings.json"));
        text.ShouldContain("pttKey");
        text.ShouldContain("F8");
        text.ShouldContain("não existe");

        var fresh = Path.Combine(_root, "fresh");
        var created = new StringWriter(new StringBuilder()) { NewLine = "\n" };

        ConfigCommands.Init(Path.Combine(fresh, "appsettings.json"), created, new StringWriter()).ShouldBe(0);
        File.Exists(Path.Combine(fresh, "appsettings.json")).ShouldBeTrue();

        var again = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        ConfigCommands.Init(Path.Combine(fresh, "appsettings.json"), again, new StringWriter()).ShouldBe(0);
        again.ToString().ShouldContain("já existe");
    }

    [Fact]
    public void The_file_that_init_writes_is_valid_and_changes_nothing_until_the_user_edits_it()
    {
        var path = Path.Combine(User, "appsettings.json");
        ConfigCommands.Init(path, new StringWriter(), new StringWriter()).ShouldBe(0);

        var loaded = Load();

        loaded.Error.ShouldBeNull();
        SettingsApplier.Apply(new AppOptions(AtcAirport: "SBCT"), loaded.Settings, new HashSet<string>()).Options.ShouldBe(new AppOptions(AtcAirport: "SBCT"));
    }
}
