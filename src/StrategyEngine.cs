using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GameInputLab.Core
{
    public sealed class StrategyEngine : IDisposable
    {
        private readonly object _lifecycleLock = new object();
        private Thread _worker;
        private volatile bool _running;
        private volatile bool _stopRequested;
        private bool _disposed;
        private int _triggerCount;
        private long _startedAt;
        private string _lastTriggeredRule;

        public event Action<string> StatusChanged;
        public event Action<string> RuleTriggered;
        public event Action<string> Error;

        public bool IsRunning
        {
            get { return _running; }
        }

        public int TriggerCount
        {
            get { return _triggerCount; }
        }

        public string LastTriggeredRule
        {
            get { return _lastTriggeredRule; }
        }

        public TimeSpan Elapsed
        {
            get
            {
                if (_startedAt == 0)
                    return TimeSpan.Zero;
                return TimeSpan.FromMilliseconds(
                    (Stopwatch.GetTimestamp() - _startedAt) * 1000.0 /
                    Stopwatch.Frequency);
            }
        }

        public void Start(IList<StrategyRule> rules)
        {
            if (_disposed)
                throw new ObjectDisposedException("StrategyEngine");
            if (rules == null || rules.Count == 0)
                throw new ArgumentException("至少需要一条智能策略规则。", "rules");

            List<StrategyRule> snapshot = new List<StrategyRule>();
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] != null && rules[i].Enabled)
                {
                    StrategyRule copy = rules[i].Clone();
                    copy.Normalize();
                    snapshot.Add(copy);
                }
            }
            if (snapshot.Count == 0)
                throw new ArgumentException("没有已启用的智能策略规则。", "rules");
            snapshot.Sort(delegate(StrategyRule first, StrategyRule second)
            {
                int byPriority = second.Priority.CompareTo(first.Priority);
                if (byPriority != 0)
                    return byPriority;
                return String.Compare(first.Name, second.Name, StringComparison.CurrentCulture);
            });

            lock (_lifecycleLock)
            {
                if (_running)
                    throw new InvalidOperationException("智能策略正在运行。");
                _running = true;
                _stopRequested = false;
                _triggerCount = 0;
                _lastTriggeredRule = String.Empty;
                _startedAt = Stopwatch.GetTimestamp();
                _worker = new Thread(delegate()
                {
                    RunLoop(snapshot);
                });
                _worker.IsBackground = true;
                _worker.Name = "GameInputLab Strategy";
                try
                {
                    _worker.Start();
                }
                catch
                {
                    _running = false;
                    _worker = null;
                    throw;
                }
            }
            RaiseStatus("智能策略运行中");
        }

        public void Stop()
        {
            _stopRequested = true;
            Thread worker = _worker;
            if (worker != null && Thread.CurrentThread != worker)
            {
                try
                {
                    worker.Join(1200);
                }
                catch (ThreadStateException)
                {
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
            GC.SuppressFinalize(this);
        }

        private void RunLoop(List<StrategyRule> rules)
        {
            Dictionary<string, long> lastTriggered = new Dictionary<string, long>();
            string failure = null;
            try
            {
                while (!_stopRequested)
                {
                    long cycleStart = Stopwatch.GetTimestamp();
                    StrategyRule selected = null;
                    for (int i = 0; i < rules.Count; i++)
                    {
                        StrategyRule rule = rules[i];
                        if (!rule.Enabled)
                            continue;
                        long last;
                        if (lastTriggered.TryGetValue(rule.Id, out last))
                        {
                            double elapsedMs = (Stopwatch.GetTimestamp() - last) * 1000.0 /
                                Stopwatch.Frequency;
                            if (elapsedMs < rule.CooldownMs)
                                continue;
                        }

                        bool matched = rule.TriggerType == StrategyTriggerType.Periodic ||
                            RegionContainsColor(rule);
                        if (matched)
                        {
                            selected = rule;
                            break;
                        }
                    }

                    if (selected != null)
                    {
                        ExecuteAction(selected);
                        lastTriggered[selected.Id] = Stopwatch.GetTimestamp();
                        _triggerCount++;
                        _lastTriggeredRule = selected.Name;
                        RaiseTriggered("命中策略“" + selected.Name + "” → " +
                            selected.GetActionText());
                    }
                    WaitForNextCycle(cycleStart, 50);
                }
            }
            catch (Exception exception)
            {
                failure = exception.Message;
            }
            finally
            {
                _running = false;
                _worker = null;
                _startedAt = 0;
                if (failure != null)
                    RaiseError("智能策略已停止：" + failure);
                else
                    RaiseStatus("智能策略已停止");
            }
        }

        private bool RegionContainsColor(StrategyRule rule)
        {
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            Rectangle region = new Rectangle(
                rule.RegionLeft, rule.RegionTop,
                rule.RegionWidth, rule.RegionHeight);
            if (region.Width < 2 || region.Height < 2 ||
                !virtualScreen.IntersectsWith(region))
            {
                return false;
            }
            region.Intersect(virtualScreen);
            if (region.Width < 2 || region.Height < 2)
                return false;

            Color target;
            try
            {
                target = ColorTranslator.FromHtml(rule.TargetColor);
            }
            catch
            {
                return false;
            }

            using (Bitmap bitmap = new Bitmap(
                region.Width, region.Height, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(region.Left, region.Top, 0, 0,
                    region.Size, CopyPixelOperation.SourceCopy);
                return BitmapContainsColor(
                    bitmap, target, rule.ColorTolerance, rule.MatchPercent);
            }
        }

        internal static bool BitmapContainsColor(Bitmap bitmap, Color target,
            int tolerance, int matchPercent)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                return false;
            tolerance = Math.Max(0, Math.Min(255, tolerance));
            matchPercent = Math.Max(1, Math.Min(100, matchPercent));

            Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(
                bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] bytes = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                int toleranceSquared = tolerance * tolerance;
                int total = bitmap.Width * bitmap.Height;
                int stridePixels = Math.Max(1, total / 12000);
                int matched = 0;
                int sampled = 0;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < bitmap.Width; x += stridePixels)
                    {
                        int offset = row + x * 3;
                        int blue = bytes[offset];
                        int green = bytes[offset + 1];
                        int red = bytes[offset + 2];
                        int dr = red - target.R;
                        int dg = green - target.G;
                        int db = blue - target.B;
                        if (dr * dr + dg * dg + db * db <= toleranceSquared)
                            matched++;
                        sampled++;
                    }
                }
                if (sampled == 0)
                    return false;
                return matched * 100L >= (long)sampled * matchPercent;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private void ExecuteAction(StrategyRule rule)
        {
            if (_stopRequested)
                return;
            switch (rule.ActionType)
            {
                case StrategyActionType.KeyPress:
                    SendKey(rule, false);
                    int remaining = Math.Max(0, rule.HoldMs);
                    while (remaining > 0 && !_stopRequested)
                    {
                        int wait = Math.Min(20, remaining);
                        Thread.Sleep(wait);
                        remaining -= wait;
                    }
                    try
                    {
                        SendKey(rule, true);
                    }
                    catch
                    {
                    }
                    break;
                case StrategyActionType.MouseLeftClick:
                    SendRegionClick(rule, RecordedMouseButton.Left);
                    break;
                case StrategyActionType.MouseRightClick:
                    SendRegionClick(rule, RecordedMouseButton.Right);
                    break;
                case StrategyActionType.RelativeMouseMove:
                    SendRelativeMove(rule.DeltaX, rule.DeltaY);
                    break;
            }
        }

        private void SendKey(StrategyRule rule, bool keyUp)
        {
            Win32.INPUT input = new Win32.INPUT();
            input.type = Win32.INPUT_KEYBOARD;
            if (rule.ScanCode > 0)
            {
                input.U.ki.wVk = 0;
                input.U.ki.wScan = (ushort)rule.ScanCode;
                input.U.ki.dwFlags = Win32.KEYEVENTF_SCANCODE;
                if (rule.IsExtended)
                    input.U.ki.dwFlags |= Win32.KEYEVENTF_EXTENDEDKEY;
            }
            else
            {
                input.U.ki.wVk = (ushort)rule.VirtualKey;
                input.U.ki.wScan = 0;
                input.U.ki.dwFlags = 0;
            }
            if (keyUp)
                input.U.ki.dwFlags |= Win32.KEYEVENTF_KEYUP;
            Send(input);
        }

        private void SendRegionClick(StrategyRule rule, RecordedMouseButton button)
        {
            int x = rule.RegionLeft + Math.Max(1, rule.RegionWidth) / 2;
            int y = rule.RegionTop + Math.Max(1, rule.RegionHeight) / 2;
            if (rule.RegionWidth <= 1 || rule.RegionHeight <= 1)
            {
                x = Cursor.Position.X;
                y = Cursor.Position.Y;
            }

            SendMouse(button, true, x, y);
            Thread.Sleep(35);
            SendMouse(button, false, x, y);
        }

        private void SendRelativeMove(int deltaX, int deltaY)
        {
            Win32.INPUT input = new Win32.INPUT();
            input.type = Win32.INPUT_MOUSE;
            input.U.mi.dx = deltaX;
            input.U.mi.dy = deltaY;
            input.U.mi.dwFlags = Win32.MOUSEEVENTF_MOVE |
                Win32.MOUSEEVENTF_MOVE_NOCOALESCE;
            Send(input);
        }

        private void SendMouse(RecordedMouseButton button, bool down, int screenX, int screenY)
        {
            Rectangle screen = SystemInformation.VirtualScreen;
            long normalizedX = ((long)screenX - screen.Left) * 65535L /
                Math.Max(1, screen.Width - 1);
            long normalizedY = ((long)screenY - screen.Top) * 65535L /
                Math.Max(1, screen.Height - 1);

            uint flags;
            uint data = 0;
            if (button == RecordedMouseButton.Right)
                flags = down ? Win32.MOUSEEVENTF_RIGHTDOWN : Win32.MOUSEEVENTF_RIGHTUP;
            else
                flags = down ? Win32.MOUSEEVENTF_LEFTDOWN : Win32.MOUSEEVENTF_LEFTUP;

            Win32.INPUT input = new Win32.INPUT();
            input.type = Win32.INPUT_MOUSE;
            input.U.mi.dx = (int)Math.Max(0, Math.Min(65535, normalizedX));
            input.U.mi.dy = (int)Math.Max(0, Math.Min(65535, normalizedY));
            input.U.mi.mouseData = data;
            input.U.mi.dwFlags = flags | Win32.MOUSEEVENTF_MOVE |
                Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK;
            Send(input);
        }

        private static void Send(Win32.INPUT input)
        {
            Win32.INPUT[] inputs = new Win32.INPUT[] { input };
            uint sent = Win32.SendInput(
                1, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
            if (sent != 1)
                throw new InvalidOperationException(
                    "Windows 拒绝发送策略动作，错误码 " +
                    Marshal.GetLastWin32Error() + "。");
        }

        private void WaitForNextCycle(long cycleStart, int intervalMs)
        {
            while (!_stopRequested)
            {
                double elapsed = (Stopwatch.GetTimestamp() - cycleStart) * 1000.0 /
                    Stopwatch.Frequency;
                if (elapsed >= intervalMs)
                    return;
                Thread.Sleep(5);
            }
        }

        private void RaiseStatus(string message)
        {
            Action<string> handler = StatusChanged;
            if (handler == null)
                return;
            try
            {
                handler(message);
            }
            catch
            {
            }
        }

        private void RaiseTriggered(string message)
        {
            Action<string> handler = RuleTriggered;
            if (handler == null)
                return;
            try
            {
                handler(message);
            }
            catch
            {
            }
        }

        private void RaiseError(string message)
        {
            Action<string> handler = Error;
            if (handler == null)
                return;
            try
            {
                handler(message);
            }
            catch
            {
            }
        }
    }
}
