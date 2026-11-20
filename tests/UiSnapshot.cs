using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using GameInputLab;
using GameInputLab.Core;

internal sealed class SnapshotMainForm : MainForm
{
    protected override void OnLoad(EventArgs e)
    {
        // Snapshot mode intentionally skips the global input hooks.
    }
}

internal static class UiSnapshot
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "settings")
            return CaptureSettings(args[1]);
        if (args.Length == 2 && args[0] == "strategy")
            return CaptureStrategy(args[1]);
        if (args.Length == 2 && args[0] == "wukong")
            return CaptureWukong(args[1]);
        if (args.Length != 1 && args.Length != 3)
        {
            Console.Error.WriteLine("Usage: UiSnapshot.exe <output.png> [width height]");
            return 2;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (SnapshotMainForm form = new SnapshotMainForm())
        {
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;
            form.Location = new Point(-20000, -20000);
            int width = 1360;
            int height = 840;
            if (args.Length == 3)
            {
                if (!Int32.TryParse(args[1], out width) ||
                    !Int32.TryParse(args[2], out height))
                {
                    Console.Error.WriteLine("Width and height must be integers.");
                    return 2;
                }
            }
            form.ClientSize = new Size(width, height);

            TestProject project = new TestProject();
            project.Name = "测试 10-28 22.23";
            project.ScreenWidth = 1920;
            project.ScreenHeight = 1080;
            Type type = typeof(MainForm);
            type.GetField("project", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(form, project);
            type.GetMethod("RefreshProjectView", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, null);
            type.GetMethod("SetRunMode", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { GameInputLab.RunMode.Idle });

            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            Application.DoEvents();

            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(args[0], System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Close();
        }
        Console.WriteLine("PASS: UI snapshot written to " + args[0]);
        return 0;
    }

    private static int CaptureSettings(string output)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (SettingsForm form = new SettingsForm(new AppSettings()))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;
            form.Location = new Point(-20000, -20000);
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            Application.DoEvents();

            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Close();
        }
        Console.WriteLine("PASS: settings snapshot written to " + output);
        return 0;
    }

    private static int CaptureStrategy(string output)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        List<StrategyRule> rules = new List<StrategyRule>();
        StrategyRule dodge = new StrategyRule();
        dodge.Name = "危险预警闪避";
        dodge.Priority = 90;
        dodge.RegionLeft = 1180;
        dodge.RegionTop = 120;
        dodge.RegionWidth = 220;
        dodge.RegionHeight = 140;
        dodge.TargetColor = "#F03C35";
        dodge.VirtualKey = 32;
        dodge.KeyName = "Space";
        rules.Add(dodge);

        StrategyRule attack = new StrategyRule();
        attack.Name = "周期性普通攻击";
        attack.Priority = 20;
        attack.TriggerType = StrategyTriggerType.Periodic;
        attack.CooldownMs = 450;
        attack.ActionType = StrategyActionType.MouseLeftClick;
        rules.Add(attack);

        using (StrategyRulesForm form = new StrategyRulesForm(rules))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;
            form.Location = new Point(-20000, -20000);
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            Application.DoEvents();

            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Close();
        }
        Console.WriteLine("PASS: strategy snapshot written to " + output);
        return 0;
    }

    private static int CaptureWukong(string output)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        CombatProfile profile = new CombatProfile();
        profile.ObservationLeft = 800;
        profile.ObservationTop = 260;
        profile.ObservationWidth = 1100;
        profile.ObservationHeight = 620;
        for (int i = 0; i < 18; i++)
        {
            CombatExample example = new CombatExample();
            example.Action = i < 12
                ? CombatActionKind.Dodge
                : CombatActionKind.LightAttack;
            profile.Examples.Add(example);
        }

        using (WukongAgentForm form = new WukongAgentForm(
            profile, System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "GameInputLabWukongSnapshot"), 1f))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;
            form.Location = new Point(-20000, -20000);
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            Application.DoEvents();
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Close();
        }
        Console.WriteLine("PASS: Wukong agent snapshot written to " + output);
        return 0;
    }
}
