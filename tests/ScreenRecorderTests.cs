using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using GameInputLab.Core;

internal static class ScreenRecorderTests
{
    [STAThread]
    private static int Main()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "GameInputLabScreenTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ScreenRecorder recorder = null;
        try
        {
            string path = Path.Combine(root, "capture.avi");
            ManualResetEvent finished = new ManualResetEvent(false);
            string error = null;
            recorder = new ScreenRecorder();
            recorder.RecordingCompleted += delegate(string savedPath)
            {
                path = savedPath;
                finished.Set();
            };
            recorder.Error += delegate(string message)
            {
                error = message;
                finished.Set();
            };

            recorder.Start(path, 10, 60, 640);
            Thread.Sleep(1300);
            recorder.Stop();
            if (!finished.WaitOne(5000))
                throw new TimeoutException("screen recorder did not finalize within five seconds");
            if (!String.IsNullOrEmpty(error))
                throw new InvalidOperationException(error);
            if (!File.Exists(path))
                throw new InvalidOperationException("screen recording file was not created");
            if (recorder.FramesCaptured <= 0)
                throw new InvalidOperationException("screen recorder captured no frames");

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 4096)
                throw new InvalidOperationException("screen recording file is too small");
            string header = Encoding.ASCII.GetString(bytes, 0, Math.Min(512, bytes.Length));
            if (!header.StartsWith("RIFF", StringComparison.Ordinal) ||
                header.IndexOf("AVI ", StringComparison.Ordinal) < 0 ||
                header.IndexOf("MJPG", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("screen recording is not a valid MJPEG AVI header");
            }
            string all = Encoding.ASCII.GetString(bytes);
            if (all.IndexOf("movi", StringComparison.Ordinal) < 0 ||
                all.IndexOf("idx1", StringComparison.Ordinal) < 0 ||
                all.IndexOf("00dc", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("screen recording AVI chunks are incomplete");
            }
            VerifyWindowsCanOpen(path);

            Console.WriteLine("PASS: screen recording captured " +
                recorder.FramesCaptured + " MJPEG frames (" + bytes.Length + " bytes).");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception.Message);
            return 1;
        }
        finally
        {
            if (recorder != null)
                recorder.Dispose();
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

    private static void VerifyWindowsCanOpen(string path)
    {
        MediaPlayer player = new MediaPlayer();
        bool opened = false;
        string failure = null;
        DispatcherFrame frame = new DispatcherFrame();
        DispatcherTimer timeout = new DispatcherTimer();
        timeout.Interval = TimeSpan.FromSeconds(5);
        timeout.Tick += delegate
        {
            timeout.Stop();
            frame.Continue = false;
        };
        player.MediaOpened += delegate
        {
            opened = true;
            timeout.Stop();
            frame.Continue = false;
        };
        player.MediaFailed += delegate(object sender, ExceptionEventArgs args)
        {
            failure = args.ErrorException == null
                ? "unknown media error"
                : args.ErrorException.Message;
            timeout.Stop();
            frame.Continue = false;
        };

        player.Open(new Uri(path, UriKind.Absolute));
        timeout.Start();
        Dispatcher.PushFrame(frame);
        try
        {
            if (!String.IsNullOrEmpty(failure))
                throw new InvalidOperationException("Windows could not decode AVI: " + failure);
            if (!opened)
                throw new TimeoutException("Windows media decoder did not open the AVI");
            if (player.NaturalVideoWidth <= 0 || player.NaturalVideoHeight <= 0)
                throw new InvalidOperationException("decoded AVI has no video dimensions");
        }
        finally
        {
            timeout.Stop();
            player.Close();
        }
    }
}
