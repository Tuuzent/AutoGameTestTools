using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using GameInputLab.Core;

internal static class CombatTests
{
    [STAThread]
    private static int Main()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "GameInputLabCombat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CombatProfile profile = new CombatProfile();
            profile.ObservationLeft = 100;
            profile.ObservationTop = 80;
            profile.ObservationWidth = 320;
            profile.ObservationHeight = 180;
            profile.MatchThreshold = 0.91;
            profile.Examples.Add(new CombatExample
            {
                Action = CombatActionKind.Dodge,
                FrameFiles = new List<string> { "frames/test.jpg" }
            });

            CombatProfileStore store = new CombatProfileStore();
            store.Save(root, profile);
            CombatProfile loaded = store.Load(root);
            Assert(loaded != null, "combat profile was not loaded");
            Assert(loaded.ObservationWidth == 320, "observation width mismatch");
            Assert(Math.Abs(loaded.MatchThreshold - 0.91) < 0.0001,
                "match threshold mismatch");
            Assert(loaded.Examples.Count == 1, "combat examples mismatch");
            CombatProfile clone = loaded.Clone();
            Assert(clone.Examples.Count == 1, "combat profile clone lost examples");
            Assert(loaded.Dodge.Matches(new RecordedEvent
            {
                Kind = RecordedEventKind.KeyDown,
                VirtualKey = 32
            }), "keyboard dodge binding did not match");
            Assert(loaded.LightAttack.Matches(new RecordedEvent
            {
                Kind = RecordedEventKind.MouseButtonDown,
                MouseButton = RecordedMouseButton.Left
            }), "mouse light attack binding did not match");

            string frameDir = Path.Combine(root, "frames");
            Directory.CreateDirectory(frameDir);
            string framePath = Path.Combine(frameDir, "test.jpg");
            File.WriteAllBytes(framePath, new byte[] { 1, 2, 3 });
            store.ClearExamples(root, loaded);
            Assert(loaded.Examples.Count == 0, "examples were not cleared");
            Assert(!File.Exists(framePath), "frame file was not removed");
            CombatProfile reloaded = store.Load(root);
            Assert(reloaded.Examples.Count == 0, "cleared examples were not saved");

            CombatProfile trainingProfile = new CombatProfile();
            Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            trainingProfile.ObservationLeft = screen.Left + screen.Width / 2 - 80;
            trainingProfile.ObservationTop = screen.Top + screen.Height / 2 - 50;
            trainingProfile.ObservationWidth = 160;
            trainingProfile.ObservationHeight = 100;
            CombatDemoTrainer trainer = new CombatDemoTrainer();
            trainer.Start(trainingProfile, Path.Combine(root, "training"));
            System.Threading.Thread.Sleep(420);
            trainer.FeedInput(new RecordedEvent
            {
                Kind = RecordedEventKind.KeyDown,
                VirtualKey = 32,
                ScanCode = 57,
                KeyName = "Space"
            });
            System.Threading.Thread.Sleep(280);
            trainer.Stop();
            trainer.Dispose();
            Assert(trainingProfile.Examples.Count >= 1,
                "visible demonstration did not capture a dodge example");
            Assert(trainingProfile.Examples[0].FrameFiles.Count >= 1,
                "captured example has no frames");

            Console.WriteLine("PASS: combat profile and visual demonstration capture completed.");
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
