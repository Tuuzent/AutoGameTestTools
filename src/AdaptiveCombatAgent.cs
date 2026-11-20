using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GameInputLab.Core
{
    public sealed class AdaptiveCombatAgent : IDisposable
    {
        private readonly object _lifecycleLock = new object();
        private readonly List<VisualExample> _visualExamples = new List<VisualExample>();
        private readonly Dictionary<CombatActionKind, long> _lastActionTicks =
            new Dictionary<CombatActionKind, long>();
        private CombatProfile _profile;
        private string _profileDirectory;
        private Thread _worker;
        private volatile bool _running;
        private volatile bool _stopRequested;
        private bool _disposed;
        private int _actionCount;
        private string _lastAction;
        private long _lastAutoAttackTimestamp;

        public event Action<string> StatusChanged;
        public event Action<string> ActionTriggered;
        public event Action<string> Error;

        public bool IsRunning
        {
            get { return _running; }
        }

        public int ActionCount
        {
            get { return _actionCount; }
        }

        public string LastAction
        {
            get { return _lastAction; }
        }

        public void Start(CombatProfile profile, string profileDirectory)
        {
            if (_disposed)
                throw new ObjectDisposedException("AdaptiveCombatAgent");
            if (profile == null)
                throw new ArgumentNullException("profile");
            profile.Normalize();
            if (String.IsNullOrWhiteSpace(profileDirectory))
                throw new ArgumentException("战斗代理目录不能为空。", "profileDirectory");
            if (profile.ObservationWidth < 32 || profile.ObservationHeight < 32)
                throw new InvalidOperationException("请先设置战斗观察区域。");

            lock (_lifecycleLock)
            {
                if (_running)
                    throw new InvalidOperationException("战斗代理正在运行。");
                _profile = profile.Clone();
                _profile.Examples = new List<CombatExample>();
                for (int i = 0; i < profile.Examples.Count; i++)
                {
                    if (profile.Examples[i] != null)
                        _profile.Examples.Add(profile.Examples[i].Clone());
                }
                _profileDirectory = Path.GetFullPath(profileDirectory);
                _visualExamples.Clear();
                _lastActionTicks.Clear();
                _actionCount = 0;
                _lastAction = String.Empty;
                _lastAutoAttackTimestamp = 0;
                _stopRequested = false;
                _running = true;
                _worker = new Thread(RunLoop);
                _worker.IsBackground = true;
                _worker.Name = "GameInputLab Wukong Agent";
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
            RaiseStatus("黑神话战斗代理运行中");
        }

        public void Stop()
        {
            _stopRequested = true;
            Thread worker = _worker;
            if (worker != null && Thread.CurrentThread != worker)
            {
                try
                {
                    worker.Join(1500);
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

        private void RunLoop()
        {
            string failure = null;
            try
            {
                LoadExamples();
                if (_visualExamples.Count == 0)
                    throw new InvalidOperationException(
                        "没有可用的战斗示范，请先录制至少一轮包含闪避或攻击的示范。");

                Rectangle region = new Rectangle(
                    _profile.ObservationLeft,
                    _profile.ObservationTop,
                    _profile.ObservationWidth,
                    _profile.ObservationHeight);
                region.Intersect(SystemInformation.VirtualScreen);
                if (region.Width < 32 || region.Height < 32)
                    throw new InvalidOperationException("战斗观察区域不在当前桌面范围内。");

                if (_profile.LockTarget != null && _profile.LockTarget.IsConfigured())
                {
                    SendBinding(_profile.LockTarget);
                    Thread.Sleep(60);
                }

                using (Bitmap frame = new Bitmap(
                    region.Width, region.Height, PixelFormat.Format24bppRgb))
                using (Graphics graphics = Graphics.FromImage(frame))
                {
                    graphics.InterpolationMode = InterpolationMode.Bilinear;
                    while (!_stopRequested)
                    {
                        long started = Stopwatch.GetTimestamp();
                        graphics.CopyFromScreen(region.Left, region.Top, 0, 0,
                            region.Size, CopyPixelOperation.SourceCopy);
                        float[] descriptor = BuildDescriptor(frame);
                        VisualExample nearest = FindNearest(descriptor);
                        bool executed = false;
                        if (nearest != null && nearest.Similarity >= _profile.MatchThreshold)
                        {
                            CombatInputBinding binding = _profile.GetBinding(nearest.Action);
                            if (binding != null && binding.IsConfigured() &&
                                IsCooldownReady(nearest.Action))
                            {
                                SendBinding(binding);
                                MarkAction(nearest.Action);
                                _actionCount++;
                                _lastAction = nearest.Action.ToString();
                                RaiseAction("示范动作：" + GetActionName(nearest.Action) +
                                    " · 相似度 " +
                                    (nearest.Similarity * 100.0).ToString("0.0") + "%");
                                executed = true;
                            }
                        }

                        if (!executed && _profile.AutoAttackEnabled &&
                            _profile.LightAttack != null &&
                            _profile.LightAttack.IsConfigured())
                        {
                            long now = Stopwatch.GetTimestamp();
                            double elapsed = _lastAutoAttackTimestamp == 0
                                ? Double.MaxValue
                                : (now - _lastAutoAttackTimestamp) * 1000.0 /
                                    Stopwatch.Frequency;
                            if (elapsed >= _profile.AutoAttackIntervalMs)
                            {
                                SendBinding(_profile.LightAttack);
                                _lastAutoAttackTimestamp = now;
                                _actionCount++;
                                _lastAction = "LightAttack";
                                RaiseAction("安全节奏：普通攻击");
                            }
                        }

                        WaitForNextFrame(started, 50);
                    }
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
                if (failure != null && !_stopRequested)
                    RaiseError("战斗代理已停止：" + failure);
                else
                    RaiseStatus("战斗代理已停止");
            }
        }

        private void LoadExamples()
        {
            if (_profile.Examples == null)
                return;
            for (int i = 0; i < _profile.Examples.Count; i++)
            {
                CombatExample example = _profile.Examples[i];
                if (example == null || example.FrameFiles == null)
                    continue;
                for (int j = 0; j < example.FrameFiles.Count; j++)
                {
                    string relative = example.FrameFiles[j];
                    if (String.IsNullOrWhiteSpace(relative))
                        continue;
                    string path = Path.IsPathRooted(relative)
                        ? relative
                        : Path.Combine(_profileDirectory, relative);
                    if (!File.Exists(path))
                        continue;
                    try
                    {
                        using (Bitmap bitmap = new Bitmap(path))
                        {
                            VisualExample visual = new VisualExample();
                            visual.Action = example.Action;
                            visual.Descriptor = BuildDescriptor(bitmap);
                            if (visual.Descriptor != null)
                                _visualExamples.Add(visual);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static float[] BuildDescriptor(Bitmap source)
        {
            if (source == null || source.Width <= 0 || source.Height <= 0)
                return null;
            const int width = 64;
            const int height = 36;
            using (Bitmap small = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(small))
            {
                graphics.InterpolationMode = InterpolationMode.Bilinear;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));
                BitmapData data = small.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] bytes = new byte[stride * height];
                    Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    float[] values = new float[width * height];
                    double sum = 0;
                    for (int y = 0; y < height; y++)
                    {
                        int row = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int offset = row + x * 3;
                            float gray = (bytes[offset] + bytes[offset + 1] +
                                bytes[offset + 2]) / (3f * 255f);
                            values[y * width + x] = gray;
                            sum += gray;
                        }
                    }
                    double mean = sum / values.Length;
                    double variance = 0;
                    for (int i = 0; i < values.Length; i++)
                    {
                        double delta = values[i] - mean;
                        variance += delta * delta;
                    }
                    double standardDeviation = Math.Sqrt(
                        variance / values.Length) + 0.0001;
                    for (int i = 0; i < values.Length; i++)
                        values[i] = (float)((values[i] - mean) / standardDeviation);
                    return values;
                }
                finally
                {
                    small.UnlockBits(data);
                }
            }
        }

        private VisualExample FindNearest(float[] descriptor)
        {
            if (descriptor == null)
                return null;
            VisualExample best = null;
            float bestDistance = Single.MaxValue;
            for (int i = 0; i < _visualExamples.Count; i++)
            {
                VisualExample candidate = _visualExamples[i];
                float[] other = candidate.Descriptor;
                if (other == null || other.Length != descriptor.Length)
                    continue;
                float sum = 0;
                for (int j = 0; j < descriptor.Length; j++)
                    sum += Math.Abs(descriptor[j] - other[j]);
                float distance = sum / descriptor.Length;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            if (best == null)
                return null;
            VisualExample result = new VisualExample();
            result.Action = best.Action;
            result.Descriptor = best.Descriptor;
            result.Similarity = (float)Math.Max(0, Math.Min(1, 1.0 - bestDistance /
                Math.Max(1.0, _profile.MatchThreshold * 4.0)));
            return result;
        }

        private bool IsCooldownReady(CombatActionKind action)
        {
            long last;
            if (!_lastActionTicks.TryGetValue(action, out last))
                return true;
            int cooldown = GetCooldownMs(action);
            return (Stopwatch.GetTimestamp() - last) * 1000.0 /
                Stopwatch.Frequency >= cooldown;
        }

        private void MarkAction(CombatActionKind action)
        {
            _lastActionTicks[action] = Stopwatch.GetTimestamp();
        }

        private static int GetCooldownMs(CombatActionKind action)
        {
            switch (action)
            {
                case CombatActionKind.Dodge:
                    return 350;
                case CombatActionKind.LightAttack:
                    return 600;
                case CombatActionKind.HeavyAttack:
                    return 1000;
                case CombatActionKind.Heal:
                    return 1200;
                case CombatActionKind.LockTarget:
                    return 3000;
                default:
                    return 800;
            }
        }

        private static string GetActionName(CombatActionKind action)
        {
            switch (action)
            {
                case CombatActionKind.Dodge:
                    return "闪避";
                case CombatActionKind.LightAttack:
                    return "轻击";
                case CombatActionKind.HeavyAttack:
                    return "重击";
                case CombatActionKind.Heal:
                    return "喝药";
                case CombatActionKind.LockTarget:
                    return "锁定";
                default:
                    return action.ToString();
            }
        }

        private static void SendBinding(CombatInputBinding binding)
        {
            if (binding == null || !binding.IsConfigured())
                return;
            if (binding.Kind == CombatBindingKind.Keyboard)
                SendKeyboard(binding);
            else
                SendMouseClick(binding.Kind);
            Thread.Sleep(35);
        }

        private static void SendKeyboard(CombatInputBinding binding)
        {
            Win32.INPUT down = new Win32.INPUT();
            down.type = Win32.INPUT_KEYBOARD;
            if (binding.ScanCode > 0)
            {
                down.U.ki.wVk = 0;
                down.U.ki.wScan = (ushort)binding.ScanCode;
                down.U.ki.dwFlags = Win32.KEYEVENTF_SCANCODE;
                if (binding.IsExtended)
                    down.U.ki.dwFlags |= Win32.KEYEVENTF_EXTENDEDKEY;
            }
            else
            {
                down.U.ki.wVk = (ushort)binding.VirtualKey;
                down.U.ki.wScan = 0;
                down.U.ki.dwFlags = 0;
            }
            Win32.INPUT up = down;
            up.U.ki.dwFlags |= Win32.KEYEVENTF_KEYUP;
            SendInput(down);
            Thread.Sleep(35);
            SendInput(up);
        }

        private static void SendMouseClick(CombatBindingKind kind)
        {
            bool left = kind == CombatBindingKind.MouseLeft;
            Win32.INPUT down = new Win32.INPUT();
            down.type = Win32.INPUT_MOUSE;
            down.U.mi.dwFlags = left
                ? Win32.MOUSEEVENTF_LEFTDOWN
                : Win32.MOUSEEVENTF_RIGHTDOWN;
            Win32.INPUT up = new Win32.INPUT();
            up.type = Win32.INPUT_MOUSE;
            up.U.mi.dwFlags = left
                ? Win32.MOUSEEVENTF_LEFTUP
                : Win32.MOUSEEVENTF_RIGHTUP;
            SendInput(down);
            Thread.Sleep(45);
            SendInput(up);
        }

        private static void SendInput(Win32.INPUT input)
        {
            Win32.INPUT[] inputs = new Win32.INPUT[] { input };
            uint sent = Win32.SendInput(
                1, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
            if (sent != 1)
                throw new InvalidOperationException(
                    "Windows 拒绝发送战斗代理输入，错误码 " +
                    Marshal.GetLastWin32Error() + "。");
        }

        private void WaitForNextFrame(long started, int intervalMs)
        {
            while (!_stopRequested)
            {
                double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 /
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

        private void RaiseAction(string message)
        {
            Action<string> handler = ActionTriggered;
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

        private sealed class VisualExample
        {
            public CombatActionKind Action;
            public float[] Descriptor;
            public float Similarity;
        }
    }
}
