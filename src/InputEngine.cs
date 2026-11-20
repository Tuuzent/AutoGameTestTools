using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GameInputLab.Core
{
    public sealed class InputRecorder : IDisposable
    {
        private static InputRecorder _activeRecorder;

        private readonly Win32.LowLevelMouseProc _mouseProc;
        private readonly Win32.LowLevelKeyboardProc _keyboardProc;
        private readonly object _stateLock = new object();
        private readonly object _rawInputLock = new object();
        private readonly bool _captureInjectedForTesting;
        private readonly RawInputWindow _rawInputWindow;
        private IntPtr _mouseHook;
        private IntPtr _keyboardHook;
        private volatile bool _rawInputAvailable;
        private volatile bool _recording;
        private volatile bool _disposed;
        private long _startTimestamp;
        private long _lastMoveTimestamp;
        private long _lastRawFlushTimestamp;
        private int _lastMoveX;
        private int _lastMoveY;
        private int _rawAccumulatedX;
        private int _rawAccumulatedY;
        private string _initializationWarning;

        public event Action<RecordedEvent> EventRecorded;
        public event Action<Keys> HotkeyPressed;
        public event Action<string> Error;

        public bool IsRecording
        {
            get { return _recording; }
        }

        public bool RawInputAvailable
        {
            get { return _rawInputAvailable; }
        }

        public string InitializationWarning
        {
            get { return _initializationWarning; }
        }

        public InputRecorder() : this(false)
        {
        }

        internal InputRecorder(bool captureInjectedForTesting)
        {
            lock (typeof(InputRecorder))
            {
                if (_activeRecorder != null)
                    throw new InvalidOperationException("同一时间只能有一个输入录制器。");
            }

            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;
            _captureInjectedForTesting = captureInjectedForTesting;
            IntPtr module = Win32.GetModuleHandle(null);
            _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, module, 0);
            if (_mouseHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装全局鼠标钩子。");

            _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, module, 0);
            if (_keyboardHook == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Win32.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
                throw new Win32Exception(error, "无法安装全局键盘钩子。");
            }

            _rawInputWindow = new RawInputWindow(this);
            try
            {
                _rawInputWindow.Create();
                if (!Win32.RegisterRawInputDevices(
                    new Win32.RAWINPUTDEVICE[]
                    {
                        new Win32.RAWINPUTDEVICE
                        {
                            usUsagePage = Win32.HID_USAGE_PAGE_GENERIC,
                            usUsage = Win32.HID_USAGE_GENERIC_MOUSE,
                            dwFlags = Win32.RIDEV_INPUTSINK,
                            hwndTarget = _rawInputWindow.Handle
                        }
                    },
                    1,
                    (uint)Marshal.SizeOf(typeof(Win32.RAWINPUTDEVICE))))
                {
                    _initializationWarning = "无法启用 Raw Input 相对镜头捕获，将仅记录绝对鼠标位置。";
                }
                else
                {
                    _rawInputAvailable = true;
                }
            }
            catch (Exception exception)
            {
                _initializationWarning = "无法启用 Raw Input 相对镜头捕获：" + exception.Message;
            }

            lock (typeof(InputRecorder))
                _activeRecorder = this;
        }

        public void StartRecording()
        {
            if (_disposed)
                throw new ObjectDisposedException("InputRecorder");
            lock (_stateLock)
            {
                _recording = false;
                _startTimestamp = Stopwatch.GetTimestamp();
                _lastMoveTimestamp = 0;
                _lastMoveX = Int32.MinValue;
                _lastMoveY = Int32.MinValue;
            }
            lock (_rawInputLock)
            {
                _lastRawFlushTimestamp = 0;
                _rawAccumulatedX = 0;
                _rawAccumulatedY = 0;
            }
            lock (_stateLock)
                _recording = true;
        }

        public void StopRecording()
        {
            lock (_stateLock)
                _recording = false;
            FlushRawMovement();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _recording = false;

            if (_rawInputAvailable)
            {
                try
                {
                    Win32.RegisterRawInputDevices(
                        new Win32.RAWINPUTDEVICE[]
                        {
                            new Win32.RAWINPUTDEVICE
                            {
                                usUsagePage = Win32.HID_USAGE_PAGE_GENERIC,
                                usUsage = Win32.HID_USAGE_GENERIC_MOUSE,
                                dwFlags = Win32.RIDEV_REMOVE,
                                hwndTarget = IntPtr.Zero
                            }
                        },
                        1,
                        (uint)Marshal.SizeOf(typeof(Win32.RAWINPUTDEVICE)));
                }
                catch
                {
                }
                _rawInputAvailable = false;
            }
            if (_rawInputWindow != null)
                _rawInputWindow.Destroy();

            if (_keyboardHook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }

            lock (typeof(InputRecorder))
            {
                if (Object.ReferenceEquals(_activeRecorder, this))
                    _activeRecorder = null;
            }
            GC.SuppressFinalize(this);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_disposed)
            {
                try
                {
                    Win32.MSLLHOOKSTRUCT data = (Win32.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(Win32.MSLLHOOKSTRUCT));
                    bool injected = (data.flags & Win32.LLMHF_INJECTED) != 0;
                    if (!injected || _captureInjectedForTesting)
                        ProcessMouseMessage(wParam, data);
                }
                catch (Exception exception)
                {
                    RaiseError("鼠标事件处理失败：" + exception.Message);
                }
            }
            return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void ProcessMouseMessage(IntPtr message, Win32.MSLLHOOKSTRUCT data)
        {
            int messageId = message.ToInt32();
            if (messageId == Win32.WM_MOUSEMOVE)
            {
                if (!_recording)
                    return;
                long now = Stopwatch.GetTimestamp();
                int dx = data.pt.X - _lastMoveX;
                int dy = data.pt.Y - _lastMoveY;
                long elapsedMs = _lastMoveTimestamp == 0
                    ? Int64.MaxValue
                    : (long)((now - _lastMoveTimestamp) * 1000.0 / Stopwatch.Frequency);
                if (_lastMoveTimestamp != 0 && elapsedMs < 10 && dx * dx + dy * dy < 25)
                    return;
                _lastMoveTimestamp = now;
                _lastMoveX = data.pt.X;
                _lastMoveY = data.pt.Y;
                Publish(CreateMouseEvent(RecordedEventKind.MouseMove, data.pt.X, data.pt.Y,
                    RecordedMouseButton.None, 0));
                return;
            }

            if (!_recording)
                return;

            RecordedMouseButton button = RecordedMouseButton.None;
            RecordedEventKind kind = RecordedEventKind.MouseMove;
            int wheel = 0;
            switch (messageId)
            {
                case Win32.WM_LBUTTONDOWN:
                    button = RecordedMouseButton.Left;
                    kind = RecordedEventKind.MouseButtonDown;
                    break;
                case Win32.WM_LBUTTONUP:
                    button = RecordedMouseButton.Left;
                    kind = RecordedEventKind.MouseButtonUp;
                    break;
                case Win32.WM_RBUTTONDOWN:
                    button = RecordedMouseButton.Right;
                    kind = RecordedEventKind.MouseButtonDown;
                    break;
                case Win32.WM_RBUTTONUP:
                    button = RecordedMouseButton.Right;
                    kind = RecordedEventKind.MouseButtonUp;
                    break;
                case Win32.WM_MBUTTONDOWN:
                    button = RecordedMouseButton.Middle;
                    kind = RecordedEventKind.MouseButtonDown;
                    break;
                case Win32.WM_MBUTTONUP:
                    button = RecordedMouseButton.Middle;
                    kind = RecordedEventKind.MouseButtonUp;
                    break;
                case Win32.WM_XBUTTONDOWN:
                case Win32.WM_XBUTTONUP:
                    int xButton = (int)((data.mouseData >> 16) & 0xffff);
                    button = xButton == Win32.XBUTTON2
                        ? RecordedMouseButton.X2
                        : RecordedMouseButton.X1;
                    kind = messageId == Win32.WM_XBUTTONDOWN
                        ? RecordedEventKind.MouseButtonDown
                        : RecordedEventKind.MouseButtonUp;
                    break;
                case Win32.WM_MOUSEWHEEL:
                    wheel = (short)((data.mouseData >> 16) & 0xffff);
                    kind = RecordedEventKind.MouseWheel;
                    break;
                default:
                    return;
            }

            _lastMoveX = data.pt.X;
            _lastMoveY = data.pt.Y;
            _lastMoveTimestamp = Stopwatch.GetTimestamp();
            Publish(CreateMouseEvent(kind, data.pt.X, data.pt.Y, button, wheel));
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_disposed)
            {
                try
                {
                    Win32.KBDLLHOOKSTRUCT data = (Win32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(Win32.KBDLLHOOKSTRUCT));
                    bool injected = (data.flags & Win32.LLKHF_INJECTED) != 0;
                    if (!injected || _captureInjectedForTesting)
                    {
                        int messageId = wParam.ToInt32();
                        bool down = messageId == Win32.WM_KEYDOWN || messageId == Win32.WM_SYSKEYDOWN;
                        bool up = messageId == Win32.WM_KEYUP || messageId == Win32.WM_SYSKEYUP;
                        Keys key = (Keys)data.vkCode;
                        if (down && !injected && IsControlHotkey(key))
                        {
                            Action<Keys> handler = HotkeyPressed;
                            if (handler != null)
                            {
                                try
                                {
                                    handler(key);
                                }
                                catch (Exception exception)
                                {
                                    RaiseError("快捷键处理失败：" + exception.Message);
                                }
                            }
                        }

                        if (_recording && !IsControlHotkey(key) && (down || up))
                        {
                            RecordedEvent item = new RecordedEvent();
                            item.TimeMs = ElapsedMilliseconds();
                            item.Kind = down ? RecordedEventKind.KeyDown : RecordedEventKind.KeyUp;
                            item.VirtualKey = (int)data.vkCode;
                            item.ScanCode = (int)data.scanCode;
                            item.IsExtended = (data.flags & Win32.LLKHF_EXTENDED) != 0;
                            item.KeyName = key.ToString();
                            Publish(item);
                        }
                    }
                }
                catch (Exception exception)
                {
                    RaiseError("键盘事件处理失败：" + exception.Message);
                }
            }
            return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private void ProcessRawInputMessage(IntPtr rawInputHandle)
        {
            if (_disposed || !_recording || !_rawInputAvailable)
                return;

            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(Win32.RAWINPUTHEADER));
            uint result = Win32.GetRawInputData(rawInputHandle, Win32.RID_INPUT,
                IntPtr.Zero, ref size, headerSize);
            if (result == UInt32.MaxValue || size == 0)
                return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                result = Win32.GetRawInputData(rawInputHandle, Win32.RID_INPUT,
                    buffer, ref size, headerSize);
                if (result == UInt32.MaxValue || result == 0)
                    return;

                Win32.RAWINPUTHEADER header = (Win32.RAWINPUTHEADER)Marshal.PtrToStructure(
                    buffer, typeof(Win32.RAWINPUTHEADER));
                if (header.dwType != Win32.RIM_TYPEMOUSE)
                    return;

                Win32.RAWINPUT raw = (Win32.RAWINPUT)Marshal.PtrToStructure(
                    buffer, typeof(Win32.RAWINPUT));
                if ((raw.mouse.usFlags & Win32.MOUSE_MOVE_ABSOLUTE) != 0)
                    return;
                if (raw.mouse.lLastX == 0 && raw.mouse.lLastY == 0)
                    return;

                AccumulateRawMovement(raw.mouse.lLastX, raw.mouse.lLastY);
            }
            catch (Exception exception)
            {
                RaiseError("镜头位移解析失败：" + exception.Message);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void AccumulateRawMovement(int deltaX, int deltaY)
        {
            RecordedEvent ready = null;
            lock (_rawInputLock)
            {
                if (!_recording)
                    return;
                _rawAccumulatedX += deltaX;
                _rawAccumulatedY += deltaY;
                long now = Stopwatch.GetTimestamp();
                long elapsed = _lastRawFlushTimestamp == 0
                    ? Int64.MaxValue
                    : (long)((now - _lastRawFlushTimestamp) * 1000.0 / Stopwatch.Frequency);
                if (_lastRawFlushTimestamp == 0 || elapsed >= 8)
                {
                    ready = CreateRelativeMouseEvent(_rawAccumulatedX, _rawAccumulatedY);
                    _rawAccumulatedX = 0;
                    _rawAccumulatedY = 0;
                    _lastRawFlushTimestamp = now;
                }
            }
            if (ready != null)
                Publish(ready);
        }

        private void FlushRawMovement()
        {
            RecordedEvent ready = null;
            lock (_rawInputLock)
            {
                if (_rawAccumulatedX != 0 || _rawAccumulatedY != 0)
                {
                    ready = CreateRelativeMouseEvent(_rawAccumulatedX, _rawAccumulatedY);
                    _rawAccumulatedX = 0;
                    _rawAccumulatedY = 0;
                }
                _lastRawFlushTimestamp = 0;
            }
            if (ready != null)
                Publish(ready);
        }

        private RecordedEvent CreateRelativeMouseEvent(int deltaX, int deltaY)
        {
            RecordedEvent item = new RecordedEvent();
            item.TimeMs = ElapsedMilliseconds();
            item.Kind = RecordedEventKind.MouseRelativeMove;
            item.X = deltaX;
            item.Y = deltaY;
            item.MouseButton = RecordedMouseButton.None;
            return item;
        }

        private RecordedEvent CreateMouseEvent(RecordedEventKind kind, int x, int y,
            RecordedMouseButton button, int wheel)
        {
            RecordedEvent item = new RecordedEvent();
            item.TimeMs = ElapsedMilliseconds();
            item.Kind = kind;
            item.X = x;
            item.Y = y;
            item.MouseButton = button;
            item.WheelDelta = wheel;
            return item;
        }

        private long ElapsedMilliseconds()
        {
            if (_startTimestamp == 0)
                return 0;
            long elapsed = Stopwatch.GetTimestamp() - _startTimestamp;
            return Math.Max(0, (long)(elapsed * 1000.0 / Stopwatch.Frequency));
        }

        private void Publish(RecordedEvent item)
        {
            Action<RecordedEvent> handler = EventRecorded;
            if (handler == null)
                return;
            try
            {
                handler(item);
            }
            catch (Exception exception)
            {
                RaiseError("事件回调失败：" + exception.Message);
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

        private static bool IsControlHotkey(Keys key)
        {
            return key == Keys.F8 || key == Keys.F9 ||
                key == Keys.F10 || key == Keys.F11 || key == Keys.F12;
        }

        private sealed class RawInputWindow : NativeWindow
        {
            private readonly InputRecorder _owner;

            public RawInputWindow(InputRecorder owner)
            {
                _owner = owner;
            }

            public void Create()
            {
                CreateParams parameters = new CreateParams();
                parameters.Caption = "GameInputLab.RawInput";
                parameters.Parent = new IntPtr(-3);
                CreateHandle(parameters);
            }

            public void Destroy()
            {
                if (Handle != IntPtr.Zero)
                    DestroyHandle();
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == Win32.WM_INPUT)
                {
                    _owner.ProcessRawInputMessage(message.LParam);
                    message.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref message);
            }
        }
    }

    public sealed class PlaybackEngine : IDisposable
    {
        private readonly object _lifecycleLock = new object();
        private readonly Dictionary<int, RecordedEvent> _pressedKeys =
            new Dictionary<int, RecordedEvent>();
        private readonly HashSet<RecordedMouseButton> _pressedButtons =
            new HashSet<RecordedMouseButton>();
        private Thread _worker;
        private volatile bool _playing;
        private volatile bool _stopRequested;
        private bool _relativeCameraMode;
        private bool _disposed;
        private int _virtualLeft;
        private int _virtualTop;
        private int _virtualWidth;
        private int _virtualHeight;

        public event Action<int> ProgressChanged;
        public event EventHandler PlaybackCompleted;
        public event Action<string> PlaybackFailed;

        public bool IsPlaying
        {
            get { return _playing; }
        }

        public PlaybackEngine()
        {
            RefreshDesktopMetrics();
        }

        public void Play(IList<RecordedEvent> events, double speed, int repeatCount, int initialDelayMs)
        {
            Play(events, speed, repeatCount, initialDelayMs, false);
        }

        public void Play(IList<RecordedEvent> events, double speed, int repeatCount,
            int initialDelayMs, bool relativeCameraMode)
        {
            if (_disposed)
                throw new ObjectDisposedException("PlaybackEngine");
            if (events == null || events.Count == 0)
                throw new ArgumentException("没有可回放的事件。", "events");

            List<RecordedEvent> snapshot = new List<RecordedEvent>();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] != null)
                    snapshot.Add(events[i].Clone());
            }
            if (snapshot.Count == 0)
                throw new ArgumentException("没有可回放的有效事件。", "events");
            snapshot.Sort(delegate(RecordedEvent first, RecordedEvent second)
            {
                return first.TimeMs.CompareTo(second.TimeMs);
            });

            if (speed < 0.05 || speed > 20.0)
                throw new ArgumentOutOfRangeException("speed", "回放速度必须在 0.05× 到 20× 之间。");
            repeatCount = Math.Max(1, Math.Min(999, repeatCount));
            initialDelayMs = Math.Max(0, Math.Min(30000, initialDelayMs));

            lock (_lifecycleLock)
            {
                if (_playing)
                    throw new InvalidOperationException("回放正在进行中。");
                RefreshDesktopMetrics();
                _stopRequested = false;
                _playing = true;
                _relativeCameraMode = relativeCameraMode;
                _pressedKeys.Clear();
                _pressedButtons.Clear();
                _worker = new Thread(delegate()
                {
                    PlaybackWorker(snapshot, speed, repeatCount, initialDelayMs, relativeCameraMode);
                });
                _worker.IsBackground = true;
                _worker.Name = "GameInputLab Playback";
                try
                {
                    _worker.Start();
                }
                catch
                {
                    _playing = false;
                    _worker = null;
                    throw;
                }
            }
        }

        public void Stop()
        {
            _stopRequested = true;
            Thread worker = _worker;
            if (worker != null && Thread.CurrentThread != worker)
            {
                try
                {
                    worker.Join(800);
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

        private void PlaybackWorker(List<RecordedEvent> events, double speed,
            int repeatCount, int initialDelayMs, bool relativeCameraMode)
        {
            bool completed = false;
            string failure = null;
            try
            {
                RaiseProgress(0);
                if (!WaitMilliseconds(initialDelayMs))
                    return;

                long firstTime = events[0].TimeMs;
                int total = events.Count * repeatCount;
                int completedCount = 0;
                int lastPercent = -1;
                for (int repeat = 0; repeat < repeatCount && !_stopRequested; repeat++)
                {
                    Stopwatch repeatClock = Stopwatch.StartNew();
                    for (int i = 0; i < events.Count; i++)
                    {
                        if (_stopRequested)
                            return;
                        RecordedEvent item = events[i];
                        long relative = Math.Max(0, item.TimeMs - firstTime);
                        long target = (long)(relative / speed);
                        if (!WaitUntil(repeatClock, target))
                            return;
                        SendRecordedEvent(item, relativeCameraMode);
                        completedCount++;
                        int percent = total == 0
                            ? 100
                            : (int)Math.Min(100L, completedCount * 100L / total);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            RaiseProgress(percent);
                        }
                    }
                    ReleasePressedInputs();
                }

                if (!_stopRequested)
                {
                    RaiseProgress(100);
                    completed = true;
                }
            }
            catch (Exception exception)
            {
                failure = exception.Message;
            }
            finally
            {
                try
                {
                    ReleasePressedInputs();
                }
                catch
                {
                }
                _playing = false;
                _worker = null;
                if (failure != null && !_stopRequested)
                    RaiseFailure(failure);
                else if (completed && !_stopRequested)
                    RaiseCompleted();
            }
        }

        private bool WaitMilliseconds(int milliseconds)
        {
            return WaitUntil(Stopwatch.StartNew(), milliseconds);
        }

        private bool WaitUntil(Stopwatch clock, long targetMilliseconds)
        {
            while (!_stopRequested)
            {
                long remaining = targetMilliseconds - clock.ElapsedMilliseconds;
                if (remaining <= 0)
                    return true;
                if (remaining > 16)
                    Thread.Sleep((int)Math.Min(10L, remaining - 7L));
                else if (remaining > 3)
                    Thread.Sleep(1);
                else
                    Thread.SpinWait(80);
            }
            return false;
        }

        private void SendRecordedEvent(RecordedEvent item, bool relativeCameraMode)
        {
            switch (item.Kind)
            {
                case RecordedEventKind.MouseMove:
                    if (!relativeCameraMode)
                        SendMouseAbsolute(item.X, item.Y, Win32.MOUSEEVENTF_MOVE);
                    break;
                case RecordedEventKind.MouseRelativeMove:
                    if (relativeCameraMode)
                        SendMouseRelative(item.X, item.Y);
                    break;
                case RecordedEventKind.MouseButtonDown:
                    SendMouseButton(item, true);
                    break;
                case RecordedEventKind.MouseButtonUp:
                    SendMouseButton(item, false);
                    break;
                case RecordedEventKind.MouseWheel:
                    SendMouseWheel(item, relativeCameraMode);
                    break;
                case RecordedEventKind.KeyDown:
                    SendKeyboard(item, false);
                    _pressedKeys[item.VirtualKey] = item.Clone();
                    break;
                case RecordedEventKind.KeyUp:
                    SendKeyboard(item, true);
                    _pressedKeys.Remove(item.VirtualKey);
                    break;
            }
        }

        private void SendMouseAbsolute(int x, int y, uint actionFlags)
        {
            int left = _virtualLeft;
            int top = _virtualTop;
            int width = Math.Max(2, _virtualWidth);
            int height = Math.Max(2, _virtualHeight);
            long normalizedX = ((long)x - left) * 65535L / (width - 1);
            long normalizedY = ((long)y - top) * 65535L / (height - 1);
            normalizedX = Math.Max(0, Math.Min(65535, normalizedX));
            normalizedY = Math.Max(0, Math.Min(65535, normalizedY));

            Win32.INPUT input = new Win32.INPUT();
            input.type = Win32.INPUT_MOUSE;
            input.U.mi.dx = (int)normalizedX;
            input.U.mi.dy = (int)normalizedY;
            input.U.mi.mouseData = 0;
            input.U.mi.dwFlags = actionFlags | Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK;
            Send(input);
        }

        private void SendMouseRelative(int deltaX, int deltaY)
        {
            Win32.INPUT input = new Win32.INPUT();
            input.type = Win32.INPUT_MOUSE;
            input.U.mi.dx = deltaX;
            input.U.mi.dy = deltaY;
            input.U.mi.mouseData = 0;
            input.U.mi.dwFlags = Win32.MOUSEEVENTF_MOVE |
                Win32.MOUSEEVENTF_MOVE_NOCOALESCE;
            Send(input);
        }

        private void SendMouseButton(RecordedEvent item, bool down)
        {
            uint flag;
            uint data = 0;
            switch (item.MouseButton)
            {
                case RecordedMouseButton.Left:
                    flag = down ? Win32.MOUSEEVENTF_LEFTDOWN : Win32.MOUSEEVENTF_LEFTUP;
                    break;
                case RecordedMouseButton.Right:
                    flag = down ? Win32.MOUSEEVENTF_RIGHTDOWN : Win32.MOUSEEVENTF_RIGHTUP;
                    break;
                case RecordedMouseButton.Middle:
                    flag = down ? Win32.MOUSEEVENTF_MIDDLEDOWN : Win32.MOUSEEVENTF_MIDDLEUP;
                    break;
                case RecordedMouseButton.X1:
                    flag = down ? Win32.MOUSEEVENTF_XDOWN : Win32.MOUSEEVENTF_XUP;
                    data = Win32.XBUTTON1;
                    break;
                case RecordedMouseButton.X2:
                    flag = down ? Win32.MOUSEEVENTF_XDOWN : Win32.MOUSEEVENTF_XUP;
                    data = Win32.XBUTTON2;
                    break;
                default:
                    return;
            }

            Win32.INPUT input = new Win32.INPUT();
            input.type = Win32.INPUT_MOUSE;
            // Relative camera movement is independent of cursor placement, but every
            // button event is anchored to its recorded absolute point so the initial
            // cursor offset cannot shift subsequent game clicks.
            input.U.mi.dx = NormalizeX(item.X);
            input.U.mi.dy = NormalizeY(item.Y);
            input.U.mi.mouseData = data;
            input.U.mi.dwFlags = flag | Win32.MOUSEEVENTF_MOVE |
                Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK;
            Send(input);
            if (down)
                _pressedButtons.Add(item.MouseButton);
            else
                _pressedButtons.Remove(item.MouseButton);
        }

        private void SendMouseWheel(RecordedEvent item, bool relativeCameraMode)
        {
            Win32.INPUT input = new Win32.INPUT();
            input.type = Win32.INPUT_MOUSE;
            input.U.mi.dx = relativeCameraMode ? 0 : NormalizeX(item.X);
            input.U.mi.dy = relativeCameraMode ? 0 : NormalizeY(item.Y);
            input.U.mi.mouseData = unchecked((uint)item.WheelDelta);
            input.U.mi.dwFlags = relativeCameraMode
                ? Win32.MOUSEEVENTF_WHEEL
                : Win32.MOUSEEVENTF_WHEEL | Win32.MOUSEEVENTF_MOVE |
                    Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK;
            Send(input);
        }

        private void SendKeyboard(RecordedEvent item, bool keyUp)
        {
            Win32.INPUT input = new Win32.INPUT();
            input.type = Win32.INPUT_KEYBOARD;
            input.U.ki.time = 0;
            input.U.ki.dwExtraInfo = UIntPtr.Zero;
            if (item.ScanCode > 0)
            {
                input.U.ki.wVk = 0;
                input.U.ki.wScan = (ushort)item.ScanCode;
                input.U.ki.dwFlags = Win32.KEYEVENTF_SCANCODE;
                if (item.IsExtended)
                    input.U.ki.dwFlags |= Win32.KEYEVENTF_EXTENDEDKEY;
            }
            else
            {
                input.U.ki.wVk = (ushort)item.VirtualKey;
                input.U.ki.wScan = 0;
                input.U.ki.dwFlags = 0;
            }
            if (keyUp)
                input.U.ki.dwFlags |= Win32.KEYEVENTF_KEYUP;
            Send(input);
        }

        private int NormalizeX(int x)
        {
            long normalized = ((long)x - _virtualLeft) * 65535L / Math.Max(1, _virtualWidth - 1);
            return (int)Math.Max(0, Math.Min(65535, normalized));
        }

        private int NormalizeY(int y)
        {
            long normalized = ((long)y - _virtualTop) * 65535L / Math.Max(1, _virtualHeight - 1);
            return (int)Math.Max(0, Math.Min(65535, normalized));
        }

        private static void Send(Win32.INPUT input)
        {
            Win32.INPUT[] inputs = new Win32.INPUT[] { input };
            uint sent = Win32.SendInput(1, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
            if (sent != 1)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    "Windows 拒绝发送模拟输入" +
                    (error == Win32.ERROR_ACCESS_DENIED
                        ? "（权限不足；若游戏以管理员运行，请以管理员身份启动本软件）"
                        : "（Win32 错误 " + error + "：" + new Win32Exception(error).Message + "）") +
                    "。");
            }
        }

        private void ReleasePressedInputs()
        {
            List<RecordedEvent> keys = new List<RecordedEvent>(_pressedKeys.Values);
            for (int i = 0; i < keys.Count; i++)
            {
                try
                {
                    SendKeyboard(keys[i], true);
                }
                catch
                {
                }
            }
            _pressedKeys.Clear();

            List<RecordedMouseButton> buttons = new List<RecordedMouseButton>(_pressedButtons);
            for (int i = 0; i < buttons.Count; i++)
            {
                RecordedEvent item = new RecordedEvent();
                item.MouseButton = buttons[i];
                item.X = Cursor.Position.X;
                item.Y = Cursor.Position.Y;
                try
                {
                    SendMouseButton(item, false);
                }
                catch
                {
                }
            }
            _pressedButtons.Clear();
        }

        private void RefreshDesktopMetrics()
        {
            _virtualLeft = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
            _virtualTop = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
            _virtualWidth = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
            _virtualHeight = Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);
            if (_virtualWidth <= 0)
                _virtualWidth = Math.Max(1, SystemInformation.VirtualScreen.Width);
            if (_virtualHeight <= 0)
                _virtualHeight = Math.Max(1, SystemInformation.VirtualScreen.Height);
        }

        private void RaiseProgress(int percent)
        {
            Action<int> handler = ProgressChanged;
            if (handler == null)
                return;
            try
            {
                handler(percent);
            }
            catch
            {
            }
        }

        private void RaiseCompleted()
        {
            EventHandler handler = PlaybackCompleted;
            if (handler == null)
                return;
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
            }
        }

        private void RaiseFailure(string message)
        {
            Action<string> handler = PlaybackFailed;
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

    internal static class Win32
    {
        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_XBUTTONDOWN = 0x020B;
        public const int WM_XBUTTONUP = 0x020C;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_INPUT = 0x00FF;
        public const uint LLMHF_INJECTED = 0x00000001;
        public const uint LLKHF_EXTENDED = 0x00000001;
        public const uint LLKHF_INJECTED = 0x00000010;

        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_SCANCODE = 0x0008;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_MOVE_NOCOALESCE = 0x2000;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_XDOWN = 0x0080;
        public const uint MOUSEEVENTF_XUP = 0x0100;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        public const uint XBUTTON1 = 0x0001;
        public const uint XBUTTON2 = 0x0002;
        public const uint RID_INPUT = 0x10000003;
        public const uint RIM_TYPEMOUSE = 0;
        public const uint RIDEV_REMOVE = 0x00000001;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint MOUSE_MOVE_ABSOLUTE = 0x00000001;
        public const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        public const ushort HID_USAGE_GENERIC_MOUSE = 0x02;

        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;
        public const int ERROR_ACCESS_DENIED = 5;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWMOUSE
        {
            public ushort usFlags;
            public ushort usButtonFlags;
            public ushort usButtonData;
            public uint ulRawButtons;
            public int lLastX;
            public int lLastY;
            public uint ulExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUT
        {
            public RAWINPUTHEADER header;
            public RAWMOUSE mouse;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(
            IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(
            uint inputCount, [In] INPUT[] inputs, int structSize);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterRawInputDevices(
            [In] RAWINPUTDEVICE[] devices, uint deviceCount, uint structSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(
            IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
    }
}
