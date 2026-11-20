using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GameInputLab.Core
{
    public enum CombatBindingKind
    {
        Keyboard = 0,
        MouseLeft = 1,
        MouseRight = 2
    }

    public enum CombatActionKind
    {
        Dodge = 0,
        LightAttack = 1,
        HeavyAttack = 2,
        Heal = 3,
        LockTarget = 4
    }

    [Serializable]
    public sealed class CombatInputBinding
    {
        public CombatBindingKind Kind { get; set; }
        public int VirtualKey { get; set; }
        public int ScanCode { get; set; }
        public bool IsExtended { get; set; }
        public string KeyName { get; set; }

        public CombatInputBinding()
        {
            Kind = CombatBindingKind.Keyboard;
            KeyName = String.Empty;
        }

        public CombatInputBinding Clone()
        {
            return new CombatInputBinding
            {
                Kind = Kind,
                VirtualKey = VirtualKey,
                ScanCode = ScanCode,
                IsExtended = IsExtended,
                KeyName = KeyName
            };
        }

        public void Normalize()
        {
            VirtualKey = Math.Max(0, Math.Min(255, VirtualKey));
            ScanCode = Math.Max(0, Math.Min(255, ScanCode));
            KeyName = (KeyName ?? String.Empty).Trim();
        }

        public bool IsConfigured()
        {
            return Kind != CombatBindingKind.Keyboard || VirtualKey > 0;
        }

        public string GetDisplayName()
        {
            if (!IsConfigured())
                return "未设置";
            if (Kind == CombatBindingKind.MouseLeft)
                return "鼠标左键";
            if (Kind == CombatBindingKind.MouseRight)
                return "鼠标右键";
            return String.IsNullOrWhiteSpace(KeyName)
                ? "VK " + VirtualKey
                : KeyName;
        }

        public bool Matches(RecordedEvent item)
        {
            if (item == null || item.Kind != RecordedEventKind.KeyDown &&
                item.Kind != RecordedEventKind.MouseButtonDown)
                return false;
            if (Kind == CombatBindingKind.MouseLeft)
                return item.Kind == RecordedEventKind.MouseButtonDown &&
                    item.MouseButton == RecordedMouseButton.Left;
            if (Kind == CombatBindingKind.MouseRight)
                return item.Kind == RecordedEventKind.MouseButtonDown &&
                    item.MouseButton == RecordedMouseButton.Right;
            return item.Kind == RecordedEventKind.KeyDown &&
                item.VirtualKey == VirtualKey;
        }
    }

    [Serializable]
    public sealed class CombatExample
    {
        public string Id { get; set; }
        public CombatActionKind Action { get; set; }
        public string CapturedAt { get; set; }
        public List<string> FrameFiles { get; set; }
        public string Note { get; set; }

        public CombatExample()
        {
            Id = Guid.NewGuid().ToString("N");
            CapturedAt = DateTime.UtcNow.ToString("o");
            FrameFiles = new List<string>();
            Note = String.Empty;
        }

        public CombatExample Clone()
        {
            return new CombatExample
            {
                Id = Id,
                Action = Action,
                CapturedAt = CapturedAt,
                FrameFiles = new List<string>(FrameFiles ?? new List<string>()),
                Note = Note
            };
        }
    }

    [Serializable]
    public sealed class CombatProfile
    {
        public int FormatVersion { get; set; }
        public string Name { get; set; }
        public int ObservationLeft { get; set; }
        public int ObservationTop { get; set; }
        public int ObservationWidth { get; set; }
        public int ObservationHeight { get; set; }
        public double MatchThreshold { get; set; }
        public bool AutoAttackEnabled { get; set; }
        public int AutoAttackIntervalMs { get; set; }
        public CombatInputBinding Dodge { get; set; }
        public CombatInputBinding LightAttack { get; set; }
        public CombatInputBinding HeavyAttack { get; set; }
        public CombatInputBinding Heal { get; set; }
        public CombatInputBinding LockTarget { get; set; }
        public List<CombatExample> Examples { get; set; }

        public CombatProfile()
        {
            FormatVersion = 1;
            Name = "黑神话：悟空 · 战斗代理";
            ObservationWidth = 900;
            ObservationHeight = 520;
            MatchThreshold = 0.86;
            AutoAttackEnabled = true;
            AutoAttackIntervalMs = 900;
            Dodge = KeyboardBinding(32, 57, "Space");
            LightAttack = new CombatInputBinding
            {
                Kind = CombatBindingKind.MouseLeft,
                KeyName = "鼠标左键"
            };
            HeavyAttack = new CombatInputBinding
            {
                Kind = CombatBindingKind.MouseRight,
                KeyName = "鼠标右键"
            };
            Heal = new CombatInputBinding { KeyName = String.Empty };
            LockTarget = new CombatInputBinding { KeyName = String.Empty };
            Examples = new List<CombatExample>();
        }

        public CombatProfile Clone()
        {
            CombatProfile copy = new CombatProfile
            {
                FormatVersion = FormatVersion,
                Name = Name,
                ObservationLeft = ObservationLeft,
                ObservationTop = ObservationTop,
                ObservationWidth = ObservationWidth,
                ObservationHeight = ObservationHeight,
                MatchThreshold = MatchThreshold,
                AutoAttackEnabled = AutoAttackEnabled,
                AutoAttackIntervalMs = AutoAttackIntervalMs,
                Dodge = Dodge == null ? new CombatInputBinding() : Dodge.Clone(),
                LightAttack = LightAttack == null
                    ? new CombatInputBinding()
                    : LightAttack.Clone(),
                HeavyAttack = HeavyAttack == null
                    ? new CombatInputBinding()
                    : HeavyAttack.Clone(),
                Heal = Heal == null ? new CombatInputBinding() : Heal.Clone(),
                LockTarget = LockTarget == null
                    ? new CombatInputBinding()
                    : LockTarget.Clone(),
                Examples = new List<CombatExample>()
            };
            if (Examples != null)
            {
                for (int i = 0; i < Examples.Count; i++)
                {
                    if (Examples[i] != null)
                        copy.Examples.Add(Examples[i].Clone());
                }
            }
            return copy;
        }

        public CombatInputBinding GetBinding(CombatActionKind action)
        {
            switch (action)
            {
                case CombatActionKind.Dodge:
                    return Dodge;
                case CombatActionKind.LightAttack:
                    return LightAttack;
                case CombatActionKind.HeavyAttack:
                    return HeavyAttack;
                case CombatActionKind.Heal:
                    return Heal;
                case CombatActionKind.LockTarget:
                    return LockTarget;
                default:
                    return null;
            }
        }

        public void Normalize()
        {
            FormatVersion = 1;
            if (String.IsNullOrWhiteSpace(Name))
                Name = "黑神话：悟空 · 战斗代理";
            ObservationLeft = Math.Max(-10000, Math.Min(10000, ObservationLeft));
            ObservationTop = Math.Max(-10000, Math.Min(10000, ObservationTop));
            ObservationWidth = Math.Max(32, Math.Min(8000, ObservationWidth));
            ObservationHeight = Math.Max(32, Math.Min(8000, ObservationHeight));
            MatchThreshold = Math.Max(0.5, Math.Min(0.995, MatchThreshold));
            AutoAttackIntervalMs = Math.Max(200, Math.Min(10000, AutoAttackIntervalMs));
            if (Dodge == null) Dodge = KeyboardBinding(32, 57, "Space");
            if (LightAttack == null) LightAttack = new CombatInputBinding();
            if (HeavyAttack == null) HeavyAttack = new CombatInputBinding();
            if (Heal == null) Heal = new CombatInputBinding();
            if (LockTarget == null) LockTarget = new CombatInputBinding();
            Dodge.Normalize();
            LightAttack.Normalize();
            HeavyAttack.Normalize();
            Heal.Normalize();
            LockTarget.Normalize();
            if (Examples == null)
                Examples = new List<CombatExample>();
            if (Examples.Count > 600)
                Examples.RemoveRange(600, Examples.Count - 600);
            for (int i = 0; i < Examples.Count; i++)
            {
                if (Examples[i] == null)
                    continue;
                if (String.IsNullOrWhiteSpace(Examples[i].Id))
                    Examples[i].Id = Guid.NewGuid().ToString("N");
                if (Examples[i].FrameFiles == null)
                    Examples[i].FrameFiles = new List<string>();
                Examples[i].Note = Examples[i].Note ?? String.Empty;
            }
        }

        private static CombatInputBinding KeyboardBinding(
            int virtualKey, int scanCode, string name)
        {
            return new CombatInputBinding
            {
                Kind = CombatBindingKind.Keyboard,
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                KeyName = name
            };
        }
    }

    public sealed class CombatProfileStore
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public string GetDefaultDirectory()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "GameInputLab", "CombatProfiles", "Wukong");
        }

        public string GetProfilePath(string directory)
        {
            return Path.Combine(directory, "combat-profile.json");
        }

        public CombatProfile LoadDefault()
        {
            string directory = GetDefaultDirectory();
            CombatProfile profile = Load(directory);
            if (profile == null)
            {
                profile = new CombatProfile();
                Rectangle defaultRegion = SystemInformation.VirtualScreen;
                profile.ObservationLeft = defaultRegion.Left +
                    Math.Max(0, (defaultRegion.Width - profile.ObservationWidth) / 2);
                profile.ObservationTop = defaultRegion.Top +
                    Math.Max(0, (defaultRegion.Height - profile.ObservationHeight) / 2 - 60);
            }
            return profile;
        }

        public CombatProfile Load(string directory)
        {
            try
            {
                string path = GetProfilePath(directory);
                if (!File.Exists(path))
                    return null;
                CombatProfile profile = _serializer.Deserialize<CombatProfile>(
                    File.ReadAllText(path, Encoding.UTF8));
                if (profile == null)
                    return null;
                profile.Normalize();
                return profile;
            }
            catch
            {
                return null;
            }
        }

        public void Save(string directory, CombatProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException("profile");
            if (String.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("战斗代理目录不能为空。", "directory");
            profile.Normalize();
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "frames"));
            string path = GetProfilePath(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, _serializer.Serialize(profile),
                    new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch
                {
                }
            }
        }

        public void ClearExamples(string directory, CombatProfile profile)
        {
            if (profile == null)
                return;
            string frames = Path.Combine(directory, "frames");
            if (Directory.Exists(frames))
            {
                string[] files = Directory.GetFiles(frames);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        File.Delete(files[i]);
                    }
                    catch
                    {
                    }
                }
            }
            profile.Examples = new List<CombatExample>();
            Save(directory, profile);
        }
    }
}
