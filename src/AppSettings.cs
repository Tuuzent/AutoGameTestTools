using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GameInputLab.Core
{
    [Serializable]
    public sealed class AppSettings
    {
        public int FormatVersion { get; set; }
        public bool StartMaximized { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int RecordingMaxWidth { get; set; }
        public int InterfaceScalePercent { get; set; }
        public int FontScalePercent { get; set; }
        public string BackgroundColor { get; set; }
        public string AccentColor { get; set; }

        public AppSettings()
        {
            SetDefaults();
        }

        public void SetDefaults()
        {
            FormatVersion = 1;
            StartMaximized = true;
            WindowWidth = 1400;
            WindowHeight = 880;
            RecordingMaxWidth = 1920;
            InterfaceScalePercent = 100;
            FontScalePercent = 100;
            BackgroundColor = "#F7F7F8";
            AccentColor = "#0F8B7E";
        }

        public AppSettings Clone()
        {
            return new AppSettings
            {
                FormatVersion = FormatVersion,
                StartMaximized = StartMaximized,
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                RecordingMaxWidth = RecordingMaxWidth,
                InterfaceScalePercent = InterfaceScalePercent,
                FontScalePercent = FontScalePercent,
                BackgroundColor = BackgroundColor,
                AccentColor = AccentColor
            };
        }

        public void Normalize()
        {
            FormatVersion = 1;
            WindowWidth = Math.Max(900, Math.Min(5000, WindowWidth));
            WindowHeight = Math.Max(600, Math.Min(3500, WindowHeight));
            if (RecordingMaxWidth != 0)
                RecordingMaxWidth = Math.Max(640, Math.Min(3840, RecordingMaxWidth));
            InterfaceScalePercent = Math.Max(75, Math.Min(150, InterfaceScalePercent));
            FontScalePercent = Math.Max(80, Math.Min(160, FontScalePercent));
            Color background;
            if (!TryParseColor(BackgroundColor, out background) ||
                background.GetBrightness() < 0.72f)
                BackgroundColor = "#F7F7F8";
            if (!IsValidColor(AccentColor))
                AccentColor = "#0F8B7E";
        }

        public static bool IsValidColor(string value)
        {
            Color color;
            return TryParseColor(value, out color);
        }

        public static bool TryParseColor(string value, out Color color)
        {
            color = Color.Empty;
            if (String.IsNullOrWhiteSpace(value))
                return false;
            string text = value.Trim();
            if (!text.StartsWith("#"))
                text = "#" + text;
            if (text.Length != 7)
                return false;
            try
            {
                color = ColorTranslator.FromHtml(text);
                return color != Color.Empty;
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class AppSettingsStore
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public string GetDefaultPath()
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "GameInputLab", "settings.json");
        }

        public AppSettings Load()
        {
            return Load(GetDefaultPath());
        }

        public AppSettings Load(string path)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return new AppSettings();
                string json = File.ReadAllText(path, Encoding.UTF8);
                AppSettings settings = _serializer.Deserialize<AppSettings>(json);
                if (settings == null)
                    return new AppSettings();
                settings.Normalize();
                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            Save(GetDefaultPath(), settings);
        }

        public void Save(string path, AppSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentException("设置文件路径不能为空。", "path");
            settings.Normalize();

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!String.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string json = _serializer.Serialize(settings);
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(fullPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, fullPath, null);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporaryPath, fullPath, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }
}
