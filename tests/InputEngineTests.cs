using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using GameInputLab.Core;

internal static class InputEngineTests
{
    private static int _recordedMouseEvents;
    private static int _recordedKeyEvents;
    private static int _recordedCameraEvents;
    private static string _recordError;

    [STAThread]
    private static int Main()
    {
        Point originalCursor = Cursor.Position;
        InputRecorder recorder = null;
        PlaybackEngine player = null;
        try
        {
            recorder = new InputRecorder(true);
            recorder.EventRecorded += delegate(RecordedEvent item)
            {
                if (item != null && item.Kind == RecordedEventKind.MouseMove)
                    Interlocked.Increment(ref _recordedMouseEvents);
                if (item != null &&
                    (item.Kind == RecordedEventKind.KeyDown ||
                     item.Kind == RecordedEventKind.KeyUp))
                {
                    Interlocked.Increment(ref _recordedKeyEvents);
                }
                if (item != null && item.Kind == RecordedEventKind.MouseRelativeMove)
                    Interlocked.Increment(ref _recordedCameraEvents);
            };
            recorder.Error += delegate(string message)
            {
                _recordError = message;
            };
            if (!recorder.RawInputAvailable)
                throw new InvalidOperationException("raw input registration is unavailable");

            recordHookProbe(recorder, originalCursor);
            if (_recordedMouseEvents <= 0)
                throw new InvalidOperationException(
                    "mouse hook did not capture SetCursorPos movement" +
                    (String.IsNullOrEmpty(_recordError) ? "" : ": " + _recordError));
            if (_recordedKeyEvents < 2)
                throw new InvalidOperationException(
                    "keyboard hook did not capture the test key press" +
                    (String.IsNullOrEmpty(_recordError) ? "" : ": " + _recordError));
            if (_recordedCameraEvents <= 0)
                throw new InvalidOperationException(
                    "raw input did not capture relative camera movement" +
                    (String.IsNullOrEmpty(_recordError) ? "" : ": " + _recordError));

            ManualResetEvent finished = new ManualResetEvent(false);
            bool completed = false;
            string playbackError = null;
            player = new PlaybackEngine();
            player.PlaybackCompleted += delegate(object sender, EventArgs e)
            {
                completed = true;
                finished.Set();
            };
            player.PlaybackFailed += delegate(string message)
            {
                playbackError = message;
                finished.Set();
            };

            RecordedEvent move = new RecordedEvent();
            move.TimeMs = 0;
            move.Kind = RecordedEventKind.MouseMove;
            move.X = Math.Max(0, originalCursor.X + 8);
            move.Y = Math.Max(0, originalCursor.Y + 8);
            List<RecordedEvent> events = new List<RecordedEvent>();
            events.Add(move);
            RecordedEvent camera = new RecordedEvent();
            camera.TimeMs = 15;
            camera.Kind = RecordedEventKind.MouseRelativeMove;
            camera.X = 3;
            camera.Y = 2;
            events.Add(camera);
            int virtualLeft = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
            int virtualTop = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
            int virtualWidth = Math.Max(1, Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN));
            int virtualHeight = Math.Max(1, Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));
            int clickX = Math.Max(virtualLeft + 3,
                Math.Min(virtualLeft + virtualWidth - 4, originalCursor.X + 35));
            int clickY = Math.Max(virtualTop + 3,
                Math.Min(virtualTop + virtualHeight - 4, originalCursor.Y + 25));
            RecordedEvent clickDown = new RecordedEvent();
            clickDown.TimeMs = 30;
            clickDown.Kind = RecordedEventKind.MouseButtonDown;
            clickDown.MouseButton = RecordedMouseButton.Left;
            clickDown.X = clickX;
            clickDown.Y = clickY;
            events.Add(clickDown);
            RecordedEvent clickUp = clickDown.Clone();
            clickUp.TimeMs = 45;
            clickUp.Kind = RecordedEventKind.MouseButtonUp;
            events.Add(clickUp);
            player.Play(events, 1.0, 1, 0, true);

            if (!finished.WaitOne(3000))
                throw new TimeoutException("playback did not complete within three seconds");
            if (!String.IsNullOrEmpty(playbackError))
                throw new InvalidOperationException("playback failed: " + playbackError);
            if (!completed)
                throw new InvalidOperationException("playback completion event was not raised");
            Point finalCursor = Cursor.Position;
            if (Math.Abs(finalCursor.X - clickX) > 2 ||
                Math.Abs(finalCursor.Y - clickY) > 2)
            {
                throw new InvalidOperationException(
                    "relative camera playback did not anchor the click to its recorded point");
            }

            Console.WriteLine("PASS: global input, raw camera, relative playback and absolute click anchoring completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception.Message);
            return 1;
        }
        finally
        {
            Cursor.Position = originalCursor;
            if (player != null)
                player.Dispose();
            if (recorder != null)
                recorder.Dispose();
        }
    }

    private static void recordHookProbe(InputRecorder recorder, Point start)
    {
        ApplicationContext context = new ApplicationContext();
        System.Windows.Forms.Timer moveTimer = new System.Windows.Forms.Timer();
        moveTimer.Interval = 220;
        moveTimer.Tick += delegate
        {
            moveTimer.Stop();
            try
            {
                SendInjectedMove(
                    Math.Max(0, start.X + 5),
                    Math.Max(0, start.Y + 5));
                SendInjectedKey(0x87, false);
                SendInjectedKey(0x87, true);
                SendInjectedRelativeMove(3, 2);
            }
            catch (Exception exception)
            {
                _recordError = exception.Message;
                recorder.StopRecording();
                Application.ExitThread();
            }
        };

        System.Windows.Forms.Timer stopTimer = new System.Windows.Forms.Timer();
        stopTimer.Interval = 650;
        stopTimer.Tick += delegate
        {
            stopTimer.Stop();
            recorder.StopRecording();
            Application.ExitThread();
        };

        recorder.StartRecording();
        moveTimer.Start();
        stopTimer.Start();
        Application.Run(context);
        moveTimer.Dispose();
        stopTimer.Dispose();
    }

    private static void SendInjectedMove(int x, int y)
    {
        int left = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
        int top = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
        int width = Math.Max(2, Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN));
        int height = Math.Max(2, Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));
        long normalizedX = ((long)x - left) * 65535L / (width - 1);
        long normalizedY = ((long)y - top) * 65535L / (height - 1);

        Win32.INPUT input = new Win32.INPUT();
        input.type = Win32.INPUT_MOUSE;
        input.U.mi.dx = (int)Math.Max(0, Math.Min(65535, normalizedX));
        input.U.mi.dy = (int)Math.Max(0, Math.Min(65535, normalizedY));
        input.U.mi.dwFlags = Win32.MOUSEEVENTF_MOVE |
            Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK;
        Win32.INPUT[] inputs = new Win32.INPUT[] { input };
        uint sent = Win32.SendInput(
            1, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
        if (sent != 1)
            throw new InvalidOperationException("SendInput did not deliver the test mouse move");
    }

    private static void SendInjectedKey(int virtualKey, bool keyUp)
    {
        Win32.INPUT input = new Win32.INPUT();
        input.type = Win32.INPUT_KEYBOARD;
        input.U.ki.wVk = (ushort)virtualKey;
        input.U.ki.dwFlags = keyUp ? Win32.KEYEVENTF_KEYUP : 0;
        Win32.INPUT[] inputs = new Win32.INPUT[] { input };
        uint sent = Win32.SendInput(
            1, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
        if (sent != 1)
            throw new InvalidOperationException("SendInput did not deliver the test key press");
    }

    private static void SendInjectedRelativeMove(int deltaX, int deltaY)
    {
        Win32.INPUT input = new Win32.INPUT();
        input.type = Win32.INPUT_MOUSE;
        input.U.mi.dx = deltaX;
        input.U.mi.dy = deltaY;
        input.U.mi.dwFlags = Win32.MOUSEEVENTF_MOVE;
        Win32.INPUT[] inputs = new Win32.INPUT[] { input };
        uint sent = Win32.SendInput(
            1, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
        if (sent != 1)
            throw new InvalidOperationException("SendInput did not deliver relative mouse movement");
    }
}
