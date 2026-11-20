using System;
using System.Collections.Generic;

namespace GameInputLab.Core
{
    public enum RecordedEventKind
    {
        MouseMove = 0,
        MouseButtonDown = 1,
        MouseButtonUp = 2,
        MouseWheel = 3,
        KeyDown = 4,
        KeyUp = 5,
        MouseRelativeMove = 6
    }

    public enum RecordedMouseButton
    {
        None = 0,
        Left = 1,
        Right = 2,
        Middle = 3,
        X1 = 4,
        X2 = 5
    }

    public enum StrategyTriggerType
    {
        ColorMatch = 0,
        Periodic = 1
    }

    public enum StrategyActionType
    {
        KeyPress = 0,
        MouseLeftClick = 1,
        MouseRightClick = 2,
        RelativeMouseMove = 3
    }

    [Serializable]
    public sealed class StrategyRule
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public int Priority { get; set; }
        public StrategyTriggerType TriggerType { get; set; }
        public int RegionLeft { get; set; }
        public int RegionTop { get; set; }
        public int RegionWidth { get; set; }
        public int RegionHeight { get; set; }
        public string TargetColor { get; set; }
        public int ColorTolerance { get; set; }
        public int MatchPercent { get; set; }
        public int CooldownMs { get; set; }
        public StrategyActionType ActionType { get; set; }
        public int VirtualKey { get; set; }
        public int ScanCode { get; set; }
        public bool IsExtended { get; set; }
        public string KeyName { get; set; }
        public int HoldMs { get; set; }
        public int DeltaX { get; set; }
        public int DeltaY { get; set; }
        public string Notes { get; set; }

        public StrategyRule()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "新规则";
            Enabled = true;
            TriggerType = StrategyTriggerType.ColorMatch;
            RegionWidth = 120;
            RegionHeight = 80;
            TargetColor = "#FF3B30";
            ColorTolerance = 48;
            MatchPercent = 20;
            CooldownMs = 600;
            ActionType = StrategyActionType.KeyPress;
            VirtualKey = 32;
            ScanCode = 57;
            KeyName = "Space";
            HoldMs = 60;
            Notes = String.Empty;
        }

        public StrategyRule Clone()
        {
            return new StrategyRule
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                Priority = Priority,
                TriggerType = TriggerType,
                RegionLeft = RegionLeft,
                RegionTop = RegionTop,
                RegionWidth = RegionWidth,
                RegionHeight = RegionHeight,
                TargetColor = TargetColor,
                ColorTolerance = ColorTolerance,
                MatchPercent = MatchPercent,
                CooldownMs = CooldownMs,
                ActionType = ActionType,
                VirtualKey = VirtualKey,
                ScanCode = ScanCode,
                IsExtended = IsExtended,
                KeyName = KeyName,
                HoldMs = HoldMs,
                DeltaX = DeltaX,
                DeltaY = DeltaY,
                Notes = Notes
            };
        }

        public void Normalize()
        {
            if (String.IsNullOrWhiteSpace(Id))
                Id = Guid.NewGuid().ToString("N");
            if (String.IsNullOrWhiteSpace(Name))
                Name = "未命名规则";
            Priority = Math.Max(-100, Math.Min(100, Priority));
            RegionLeft = Math.Max(-10000, Math.Min(10000, RegionLeft));
            RegionTop = Math.Max(-10000, Math.Min(10000, RegionTop));
            RegionWidth = Math.Max(0, Math.Min(8000, RegionWidth));
            RegionHeight = Math.Max(0, Math.Min(8000, RegionHeight));
            TargetColor = (TargetColor ?? String.Empty).Trim();
            if (!IsHexColor(TargetColor))
                TargetColor = "#FF3B30";
            ColorTolerance = Math.Max(0, Math.Min(128, ColorTolerance));
            MatchPercent = Math.Max(1, Math.Min(100, MatchPercent));
            CooldownMs = Math.Max(80, Math.Min(60000, CooldownMs));
            VirtualKey = Math.Max(0, Math.Min(255, VirtualKey));
            ScanCode = Math.Max(0, Math.Min(255, ScanCode));
            HoldMs = Math.Max(0, Math.Min(5000, HoldMs));
            DeltaX = Math.Max(-2000, Math.Min(2000, DeltaX));
            DeltaY = Math.Max(-2000, Math.Min(2000, DeltaY));
            if (String.IsNullOrWhiteSpace(KeyName))
                KeyName = VirtualKey == 0 ? "Space" : "VK " + VirtualKey;
            KeyName = KeyName.Trim();
            Notes = Notes ?? String.Empty;
        }

        public string GetTriggerText()
        {
            if (TriggerType == StrategyTriggerType.Periodic)
                return "每 " + CooldownMs + " ms";
            return TargetColor + " · " + MatchPercent + "%";
        }

        public string GetActionText()
        {
            switch (ActionType)
            {
                case StrategyActionType.MouseLeftClick:
                    return "点击左键";
                case StrategyActionType.MouseRightClick:
                    return "点击右键";
                case StrategyActionType.RelativeMouseMove:
                    return "移动镜头 (" + DeltaX + ", " + DeltaY + ")";
                default:
                    return "按键 " +
                        (String.IsNullOrEmpty(KeyName) ? VirtualKey.ToString() : KeyName);
            }
        }

        private static bool IsHexColor(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return false;
            string text = value.Trim().TrimStart('#');
            if (text.Length != 6)
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (!Uri.IsHexDigit(text[i]))
                    return false;
            }
            return true;
        }
    }

    [Serializable]
    public class RecordedEvent
    {
        public long TimeMs { get; set; }
        public RecordedEventKind Kind { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public RecordedMouseButton MouseButton { get; set; }
        public int WheelDelta { get; set; }
        public int VirtualKey { get; set; }
        public int ScanCode { get; set; }
        public bool IsExtended { get; set; }
        public string KeyName { get; set; }

        public RecordedEvent()
        {
            KeyName = string.Empty;
        }

        public RecordedEvent Clone()
        {
            return new RecordedEvent
            {
                TimeMs = TimeMs,
                Kind = Kind,
                X = X,
                Y = Y,
                MouseButton = MouseButton,
                WheelDelta = WheelDelta,
                VirtualKey = VirtualKey,
                ScanCode = ScanCode,
                IsExtended = IsExtended,
                KeyName = KeyName ?? string.Empty
            };
        }

        public string GetKindLabel()
        {
            switch (Kind)
            {
                case RecordedEventKind.MouseMove:
                    return "鼠标移动";
                case RecordedEventKind.MouseButtonDown:
                    return "鼠标按下";
                case RecordedEventKind.MouseButtonUp:
                    return "鼠标松开";
                case RecordedEventKind.MouseWheel:
                    return "鼠标滚轮";
                case RecordedEventKind.MouseRelativeMove:
                    return "镜头移动";
                case RecordedEventKind.KeyDown:
                    return "键盘按下";
                case RecordedEventKind.KeyUp:
                    return "键盘松开";
                default:
                    return "未知事件";
            }
        }

        public string GetSummary()
        {
            switch (Kind)
            {
                case RecordedEventKind.MouseMove:
                case RecordedEventKind.MouseButtonDown:
                case RecordedEventKind.MouseButtonUp:
                    string button = GetMouseButtonLabel();
                    string action = Kind == RecordedEventKind.MouseMove
                        ? string.Empty
                        : " · " + button;
                    return "(" + X + ", " + Y + ")" + action;
                case RecordedEventKind.MouseWheel:
                    return WheelDelta > 0 ? "向上滚动 " + WheelDelta : "向下滚动 " + Math.Abs(WheelDelta);
                case RecordedEventKind.MouseRelativeMove:
                    return "相对位移 (" + X + ", " + Y + ")";
                case RecordedEventKind.KeyDown:
                case RecordedEventKind.KeyUp:
                    return string.IsNullOrEmpty(KeyName) ? "按键 #" + VirtualKey : KeyName;
                default:
                    return string.Empty;
            }
        }

        public string GetMouseButtonLabel()
        {
            switch (MouseButton)
            {
                case RecordedMouseButton.Left:
                    return "左键";
                case RecordedMouseButton.Right:
                    return "右键";
                case RecordedMouseButton.Middle:
                    return "中键";
                case RecordedMouseButton.X1:
                    return "侧键 1";
                case RecordedMouseButton.X2:
                    return "侧键 2";
                default:
                    return "鼠标";
            }
        }
    }

    [Serializable]
    public class TestProject
    {
        public string FormatId { get; set; }
        public int FormatVersion { get; set; }
        public string Name { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public int ScreenLeft { get; set; }
        public int ScreenTop { get; set; }
        public List<RecordedEvent> Events { get; set; }
        public List<StrategyRule> StrategyRules { get; set; }

        public TestProject()
        {
            FormatId = "game-input-lab";
            FormatVersion = 1;
            Name = "未命名测试";
            CreatedAt = DateTime.UtcNow.ToString("o");
            UpdatedAt = CreatedAt;
            Events = new List<RecordedEvent>();
            StrategyRules = new List<StrategyRule>();
        }

        public long GetDurationMs()
        {
            if (Events == null || Events.Count == 0)
            {
                return 0;
            }

            long duration = 0;
            for (int i = 0; i < Events.Count; i++)
            {
                if (Events[i] != null && Events[i].TimeMs > duration)
                {
                    duration = Events[i].TimeMs;
                }
            }
            return duration;
        }

        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow.ToString("o");
        }
    }
}
