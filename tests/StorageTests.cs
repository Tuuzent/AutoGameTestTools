using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameInputLab.Core;

internal static class StorageTests
{
    private static int _passed;

    private static int Main()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "GameInputLabTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            RunRoundTrip(root);
            RunCorruptFileTest(root);
            RunWrongFormatTest(root);
            RunCameraEventSelectionTest();
            Console.WriteLine("PASS: " + _passed + " storage checks completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception);
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

    private static void RunRoundTrip(string root)
    {
        TestProject source = CreateFixture();
        string path = Path.Combine(root, "round-trip.gitproj.json");
        ProjectStore store = new ProjectStore();
        store.Save(path, source);

        Assert(File.Exists(path), "project file was not created");
        Assert(new FileInfo(path).Length > 0, "project file is empty");
        Assert(store.Load(path).Events.Count == source.Events.Count, "event count did not round-trip");

        TestProject loaded = store.Load(path);
        Assert(loaded.FormatId == ProjectStore.FormatId, "format id mismatch");
        Assert(loaded.FormatVersion == ProjectStore.CurrentFormatVersion, "format version mismatch");
        Assert(loaded.Name == source.Name, "project name mismatch");
        Assert(loaded.ScreenLeft == -1920, "virtual screen left mismatch");
        Assert(loaded.ScreenTop == -120, "virtual screen top mismatch");
        Assert(loaded.ScreenWidth == 3840, "screen width mismatch");
        Assert(loaded.ScreenHeight == 1200, "screen height mismatch");
        Assert(loaded.Events[0].Kind == RecordedEventKind.MouseMove, "mouse kind mismatch");
        Assert(loaded.Events[0].TimeMs == 12, "mouse time mismatch");
        Assert(loaded.Events[0].X == -50 && loaded.Events[0].Y == 77, "mouse coordinates mismatch");
        Assert(loaded.Events[1].MouseButton == RecordedMouseButton.Left, "mouse button mismatch");
        Assert(loaded.Events[2].WheelDelta == -120, "wheel delta mismatch");
        Assert(loaded.Events[3].VirtualKey == 65, "virtual key mismatch");
        Assert(loaded.Events[3].ScanCode == 30, "scan code mismatch");
        Assert(loaded.Events[3].KeyName == "A", "key name mismatch");
        Assert(loaded.Events[4].Kind == RecordedEventKind.MouseRelativeMove,
            "relative camera event kind mismatch");
        Assert(loaded.Events[4].X == 7 && loaded.Events[4].Y == -4,
            "relative camera delta mismatch");
        Assert(loaded.StrategyRules != null && loaded.StrategyRules.Count == 1,
            "strategy rules did not round-trip");
        Assert(loaded.StrategyRules[0].Name == "危险闪避",
            "strategy rule name mismatch");
        Assert(loaded.StrategyRules[0].VirtualKey == 32,
            "strategy rule key mismatch");

        string temporary = Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly).Length == 0
            ? "none"
            : "present";
        Assert(temporary == "none", "temporary save file was not cleaned up");
        _passed++;
    }

    private static void RunCorruptFileTest(string root)
    {
        string path = Path.Combine(root, "corrupt.gitproj.json");
        File.WriteAllText(path, "{ this is not valid json", new UTF8Encoding(false));
        bool rejected = false;
        try
        {
            new ProjectStore().Load(path);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Assert(rejected, "corrupt JSON was not rejected");
        _passed++;
    }

    private static void RunWrongFormatTest(string root)
    {
        string path = Path.Combine(root, "wrong-format.json");
        File.WriteAllText(
            path,
            "{\"FormatId\":\"not-this-app\",\"FormatVersion\":1,\"Name\":\"x\",\"Events\":[]}",
            new UTF8Encoding(false));
        bool rejected = false;
        try
        {
            new ProjectStore().Load(path);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Assert(rejected, "foreign JSON format was not rejected");
        _passed++;
    }

    private static void RunCameraEventSelectionTest()
    {
        List<RecordedEvent> events = new List<RecordedEvent>();
        RecordedEvent firstMove = new RecordedEvent();
        firstMove.Kind = RecordedEventKind.MouseMove;
        firstMove.X = 10;
        firstMove.Y = 20;
        events.Add(firstMove);
        RecordedEvent secondMove = new RecordedEvent();
        secondMove.Kind = RecordedEventKind.MouseMove;
        secondMove.X = 15;
        secondMove.Y = 24;
        events.Add(secondMove);
        RecordedEvent camera = new RecordedEvent();
        camera.Kind = RecordedEventKind.MouseRelativeMove;
        camera.X = 3;
        camera.Y = -2;
        events.Add(camera);
        RecordedEvent click = new RecordedEvent();
        click.Kind = RecordedEventKind.MouseButtonDown;
        click.MouseButton = RecordedMouseButton.Left;
        click.X = 15;
        click.Y = 24;
        events.Add(click);

        Assert(PlaybackEventSelector.HasRelativeCameraData(events),
            "raw camera data was not detected");
        List<RecordedEvent> relative = PlaybackEventSelector.SelectForPlayback(events, true);
        Assert(relative.Count == 2, "relative selection count mismatch");
        Assert(relative[0].Kind == RecordedEventKind.MouseRelativeMove,
            "relative selection kept absolute movement");
        Assert(relative[1].Kind == RecordedEventKind.MouseButtonDown,
            "relative selection removed mouse buttons");

        List<RecordedEvent> absolute = PlaybackEventSelector.SelectForPlayback(events, false);
        Assert(absolute.Count == 3, "absolute selection count mismatch");
        for (int i = 0; i < absolute.Count; i++)
            Assert(absolute[i].Kind != RecordedEventKind.MouseRelativeMove,
                "absolute selection kept raw camera movement");

        List<RecordedEvent> fallbackSource = new List<RecordedEvent>();
        fallbackSource.Add(firstMove.Clone());
        fallbackSource.Add(secondMove.Clone());
        fallbackSource.Add(click.Clone());
        List<RecordedEvent> fallback = PlaybackEventSelector.SelectForPlayback(
            fallbackSource, true);
        Assert(fallback.Count == 2, "relative fallback count mismatch");
        Assert(fallback[0].Kind == RecordedEventKind.MouseRelativeMove,
            "relative fallback kind mismatch");
        Assert(fallback[0].X == 5 && fallback[0].Y == 4,
            "relative fallback delta mismatch");
        _passed++;
    }

    private static TestProject CreateFixture()
    {
        TestProject project = new TestProject();
        project.Name = "存储往返测试";
        project.CreatedAt = "2026-10-28T12:00:00.0000000Z";
        project.UpdatedAt = "2026-10-28T12:05:00.0000000Z";
        project.ScreenLeft = -1920;
        project.ScreenTop = -120;
        project.ScreenWidth = 3840;
        project.ScreenHeight = 1200;
        project.Events = new List<RecordedEvent>();

        RecordedEvent move = new RecordedEvent();
        move.TimeMs = 12;
        move.Kind = RecordedEventKind.MouseMove;
        move.X = -50;
        move.Y = 77;
        project.Events.Add(move);

        RecordedEvent click = new RecordedEvent();
        click.TimeMs = 30;
        click.Kind = RecordedEventKind.MouseButtonDown;
        click.X = -50;
        click.Y = 77;
        click.MouseButton = RecordedMouseButton.Left;
        project.Events.Add(click);

        RecordedEvent wheel = new RecordedEvent();
        wheel.TimeMs = 42;
        wheel.Kind = RecordedEventKind.MouseWheel;
        wheel.X = -50;
        wheel.Y = 77;
        wheel.WheelDelta = -120;
        project.Events.Add(wheel);

        RecordedEvent key = new RecordedEvent();
        key.TimeMs = 100;
        key.Kind = RecordedEventKind.KeyDown;
        key.VirtualKey = 65;
        key.ScanCode = 30;
        key.KeyName = "A";
        project.Events.Add(key);

        RecordedEvent camera = new RecordedEvent();
        camera.TimeMs = 124;
        camera.Kind = RecordedEventKind.MouseRelativeMove;
        camera.X = 7;
        camera.Y = -4;
        project.Events.Add(camera);

        StrategyRule strategy = new StrategyRule();
        strategy.Name = "危险闪避";
        strategy.Priority = 90;
        strategy.RegionLeft = 120;
        strategy.RegionTop = 80;
        strategy.RegionWidth = 200;
        strategy.RegionHeight = 120;
        strategy.TargetColor = "#FF3B30";
        strategy.VirtualKey = 32;
        strategy.KeyName = "Space";
        project.StrategyRules.Add(strategy);
        return project;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
