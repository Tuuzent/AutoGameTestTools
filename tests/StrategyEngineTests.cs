using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using GameInputLab.Core;

internal static class StrategyEngineTests
{
    private static int Main()
    {
        try
        {
            using (Bitmap red = new Bitmap(100, 80, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(red))
            {
                graphics.Clear(Color.FromArgb(240, 40, 35));
                Assert(StrategyEngine.BitmapContainsColor(
                    red, Color.FromArgb(240, 40, 35), 4, 100),
                    "solid red region was not matched");
                Assert(!StrategyEngine.BitmapContainsColor(
                    red, Color.Blue, 10, 20),
                    "blue should not match a red region");
            }

            using (Bitmap mixed = new Bitmap(100, 80, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(mixed))
            {
                graphics.Clear(Color.Black);
                using (SolidBrush red = new SolidBrush(Color.Red))
                    graphics.FillRectangle(red, 0, 0, 100, 24);
                Assert(StrategyEngine.BitmapContainsColor(mixed, Color.Red, 8, 20),
                    "30 percent red region should satisfy 20 percent threshold");
                Assert(!StrategyEngine.BitmapContainsColor(mixed, Color.Red, 8, 40),
                    "30 percent red region should fail 40 percent threshold");
            }

            StrategyRule rule = new StrategyRule();
            rule.Name = String.Empty;
            rule.TargetColor = "bad";
            rule.MatchPercent = 0;
            rule.CooldownMs = 0;
            rule.Normalize();
            Assert(!String.IsNullOrEmpty(rule.Name), "rule name was not normalized");
            Assert(rule.TargetColor == "#FF3B30", "rule color was not normalized");
            Assert(rule.MatchPercent == 1, "match percentage was not clamped");
            Assert(rule.CooldownMs == 80, "cooldown was not clamped");

            string engineError = null;
            using (StrategyEngine engine = new StrategyEngine())
            {
                engine.Error += delegate(string message) { engineError = message; };
                StrategyRule periodic = new StrategyRule();
                periodic.Name = "周期测试";
                periodic.TriggerType = StrategyTriggerType.Periodic;
                periodic.ActionType = StrategyActionType.KeyPress;
                periodic.VirtualKey = 0x87;
                periodic.ScanCode = 0;
                periodic.KeyName = "F24";
                periodic.HoldMs = 5;
                periodic.CooldownMs = 100;
                engine.Start(new List<StrategyRule> { periodic });
                System.Threading.Thread.Sleep(380);
                engine.Stop();
                Assert(String.IsNullOrEmpty(engineError), "strategy runtime error: " + engineError);
                Assert(engine.TriggerCount >= 2,
                    "periodic strategy did not execute at the configured cadence");
            }

            Console.WriteLine("PASS: strategy color detection and rule validation completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception.Message);
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
