using System;
using System.IO;
using GameInputLab.Core;

internal static class SettingsTests
{
    private static int Main()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "GameInputLabSettings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "settings.json");
            AppSettings settings = new AppSettings();
            settings.StartMaximized = false;
            settings.WindowWidth = 1536;
            settings.WindowHeight = 900;
            settings.RecordingMaxWidth = 2560;
            settings.InterfaceScalePercent = 125;
            settings.FontScalePercent = 115;
            settings.BackgroundColor = "#F2F7FC";
            settings.AccentColor = "#2563EB";

            AppSettingsStore store = new AppSettingsStore();
            store.Save(path, settings);
            AppSettings loaded = store.Load(path);
            Assert(!loaded.StartMaximized, "window mode mismatch");
            Assert(loaded.WindowWidth == 1536 && loaded.WindowHeight == 900,
                "window size mismatch");
            Assert(loaded.RecordingMaxWidth == 2560, "recording resolution mismatch");
            Assert(loaded.InterfaceScalePercent == 125, "interface scale mismatch");
            Assert(loaded.FontScalePercent == 115, "font scale mismatch");
            Assert(loaded.BackgroundColor == "#F2F7FC", "background color mismatch");
            Assert(loaded.AccentColor == "#2563EB", "accent color mismatch");

            File.WriteAllText(path, "{ invalid settings");
            loaded = store.Load(path);
            Assert(loaded.StartMaximized, "invalid settings should fall back to defaults");
            Console.WriteLine("PASS: settings persistence and fallback completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception.Message);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
