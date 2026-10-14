using Fonia.Audio;

namespace Fonia.App;

/// <summary>The two microphone commands, wired to the real devices. Only the composition root calls them; what they use is tested separately.</summary>
internal static class MicCommands
{
    public static int List(TextWriter output, TextWriter error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error.WriteLine("fonia: o microfone só funciona no Windows.");
            return 1;
        }

        var devices = NAudioCapture.ListDevices();

        if (devices.Count == 0)
        {
            output.WriteLine("Nenhum microfone encontrado.");
            return 0;
        }

        output.WriteLine("Microfones (o padrão do Windows é usado sem --mic-device):");

        for (var i = 0; i < devices.Count; i++)
        {
            output.WriteLine($"  {i}: {devices[i]}");
        }

        return 0;
    }

    public static async Task<int> TestAsync(AppOptions options, TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            error.WriteLine("fonia: o microfone só funciona no Windows.");
            return 1;
        }

        if (!PushToTalkKey.TryParse(options.PttKey, out var virtualKey))
        {
            error.WriteLine($"fonia: tecla '{options.PttKey}' desconhecida.");
            return 2;
        }

        try
        {
            using var capture = new NAudioCapture(options.MicDevice);
            using var pushToTalk = KeyboardPushToTalk.Create(virtualKey);

            return await new MicTest(capture, pushToTalk, options.PttKey, output, error).RunAsync(options.MicTestPath!, ct);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error.WriteLine($"fonia: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException)
        {
            error.WriteLine($"fonia: não foi possível abrir o microfone: {ex.Message}");
            return 1;
        }
    }
}
