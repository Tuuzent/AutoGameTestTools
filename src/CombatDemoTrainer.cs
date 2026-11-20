using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GameInputLab.Core
{
    public sealed class CombatDemoTrainer : IDisposable
    {
        private readonly object _stateLock = new object();
        private readonly object _frameLock = new object();
        private readonly List<CapturedFrame> _recentFrames = new List<CapturedFrame>();
        private readonly Dictionary<string, long> _lastCaptureByAction =
            new Dictionary<string, long>();
        private CombatProfile _profile;
        private string _profileDirectory;
        private Thread _worker;
        private volatile bool _recording;
        private volatile bool _stopRequested;
        private bool _disposed;
        private int _examplesCaptured;

        public event Action<CombatExample> ExampleCaptured;
        public event Action<string> Error;

        public bool IsRecording
        {
            get { return _recording; }
        }

        public int ExamplesCaptured
        {
            get { return _examplesCaptured; }
        }

        public void Start(CombatProfile profile, string profileDirectory)
        {
            if (_disposed)
                throw new ObjectDisposedException("CombatDemoTrainer");
            if (profile == null)
                throw new ArgumentNullException("profile");
            if (String.IsNullOrWhiteSpace(profileDirectory))
                throw new ArgumentException("示范保存目录不能为空。", "profileDirectory");

            profile.Normalize();
            if (profile.ObservationWidth < 32 || profile.ObservationHeight < 32)
                throw new InvalidOperationException("请先选择有效的战斗观察区域。");

            lock (_stateLock)
            {
                if (_recording)
                    throw new InvalidOperationException("战斗示范正在录制。");
                _profile = profile;
                _profileDirectory = Path.GetFullPath(profileDirectory);
                Directory.CreateDirectory(_profileDirectory);
                Directory.CreateDirectory(Path.Combine(_profileDirectory, "frames"));
                _examplesCaptured = 0;
                _stopRequested = false;
                _recording = true;
                lock (_frameLock)
                {
                    _recentFrames.Clear();
                    _lastCaptureByAction.Clear();
                }
                _worker = new Thread(CaptureLoop);
                _worker.IsBackground = true;
                _worker.Name = "GameInputLab Combat Demo";
                try
                {
                    _worker.Start();
                }
                catch
                {
                    _recording = false;
                    _worker = null;
                    throw;
                }
            }
        }

        public void FeedInput(RecordedEvent item)
        {
            if (!_recording || item == null)
                return;
            CombatActionKind action;
            CombatInputBinding binding;
            if (!TryFindAction(item, out action, out binding))
                return;

            long now = Stopwatch.GetTimestamp();
            string actionKey = action.ToString();
            lock (_stateLock)
            {
                long last;
                if (_lastCaptureByAction.TryGetValue(actionKey, out last) &&
                    (now - last) * 1000.0 / Stopwatch.Frequency < 250.0)
                {
                    return;
                }
                _lastCaptureByAction[actionKey] = now;
                if (_profile.Examples.Count >= 600)
                    return;
            }

            List<CapturedFrame> frames;
            lock (_frameLock)
            {
                frames = SelectTrainingFrames(now, _recentFrames);
            }
            if (frames.Count == 0)
                return;

            CombatExample example = new CombatExample();
            example.Action = action;
            example.Note = action.ToString();
            string actionPrefix = action.ToString().ToLowerInvariant();
            for (int i = 0; i < frames.Count; i++)
            {
                string fileName = actionPrefix + "_" + Guid.NewGuid().ToString("N") +
                    "_" + i + ".jpg";
                string fullPath = Path.Combine(_profileDirectory, "frames", fileName);
                try
                {
                    File.WriteAllBytes(fullPath, frames[i].JpegData);
                    example.FrameFiles.Add(Path.Combine("frames", fileName));
                }
                catch (Exception exception)
                {
                    RaiseError("保存战斗示范帧失败：" + exception.Message);
                }
            }
            if (example.FrameFiles.Count == 0)
                return;

            lock (_stateLock)
            {
                if (!_recording)
                    return;
                _profile.Examples.Add(example);
                _examplesCaptured++;
            }

            Action<CombatExample> handler = ExampleCaptured;
            if (handler != null)
            {
                try
                {
                    handler(example);
                }
                catch
                {
                }
            }
        }

        public void Stop()
        {
            Thread worker;
            lock (_stateLock)
            {
                _stopRequested = true;
                _recording = false;
                worker = _worker;
            }
            if (worker != null && Thread.CurrentThread != worker)
            {
                try
                {
                    worker.Join(2500);
                }
                catch (ThreadStateException)
                {
                }
            }
            lock (_stateLock)
            {
                _worker = null;
                if (_profile != null && !String.IsNullOrWhiteSpace(_profileDirectory))
                {
                    try
                    {
                        new CombatProfileStore().Save(_profileDirectory, _profile);
                    }
                    catch (Exception exception)
                    {
                        RaiseError("保存战斗示范失败：" + exception.Message);
                    }
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

        private void CaptureLoop()
        {
            try
            {
                Rectangle region = new Rectangle(
                    _profile.ObservationLeft,
                    _profile.ObservationTop,
                    _profile.ObservationWidth,
                    _profile.ObservationHeight);
                Rectangle virtualScreen = SystemInformation.VirtualScreen;
                region.Intersect(virtualScreen);
                if (region.Width < 2 || region.Height < 2)
                    throw new InvalidOperationException("观察区域不在当前桌面范围内。");

                float scale = Math.Min(1f, 320f / region.Width);
                int outputWidth = Math.Max(32, (int)Math.Round(region.Width * scale));
                int outputHeight = Math.Max(18, (int)Math.Round(region.Height * scale));
                ImageCodecInfo jpegCodec = FindJpegCodec();
                EncoderParameters encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, 72L);

                using (Bitmap source = new Bitmap(
                    region.Width, region.Height, PixelFormat.Format24bppRgb))
                using (Bitmap output = new Bitmap(
                    outputWidth, outputHeight, PixelFormat.Format24bppRgb))
                using (Graphics sourceGraphics = Graphics.FromImage(source))
                using (Graphics outputGraphics = Graphics.FromImage(output))
                {
                    outputGraphics.InterpolationMode =
                        System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                    const int fps = 15;
                    long frameInterval = Stopwatch.Frequency / fps;
                    long nextFrame = Stopwatch.GetTimestamp();
                    while (!_stopRequested)
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (now < nextFrame)
                        {
                            Thread.Sleep((int)Math.Min(10L,
                                Math.Max(1L, (nextFrame - now) * 1000L /
                                    Stopwatch.Frequency)));
                            continue;
                        }

                        sourceGraphics.CopyFromScreen(region.Left, region.Top, 0, 0,
                            region.Size, CopyPixelOperation.SourceCopy);
                        outputGraphics.DrawImage(
                            source, new Rectangle(0, 0, outputWidth, outputHeight));
                        using (MemoryStream stream = new MemoryStream())
                        {
                            output.Save(stream, jpegCodec, encoderParameters);
                            CapturedFrame frame = new CapturedFrame();
                            frame.Timestamp = Stopwatch.GetTimestamp();
                            frame.JpegData = stream.ToArray();
                            lock (_frameLock)
                            {
                                _recentFrames.Add(frame);
                                long cutoff = frame.Timestamp - Stopwatch.Frequency;
                                while (_recentFrames.Count > 0 &&
                                    _recentFrames[0].Timestamp < cutoff)
                                {
                                    _recentFrames.RemoveAt(0);
                                }
                            }
                        }
                        nextFrame += frameInterval;
                        if (nextFrame < Stopwatch.GetTimestamp() - frameInterval * 2)
                            nextFrame = Stopwatch.GetTimestamp();
                    }
                }
            }
            catch (Exception exception)
            {
                _recording = false;
                RaiseError("战斗示范画面采集失败：" + exception.Message);
            }
        }

        private bool TryFindAction(RecordedEvent item,
            out CombatActionKind action, out CombatInputBinding binding)
        {
            action = CombatActionKind.Dodge;
            binding = null;
            CombatProfile profile = _profile;
            if (profile == null)
                return false;
            if (profile.Dodge != null && profile.Dodge.Matches(item))
            {
                binding = profile.Dodge;
                return true;
            }
            action = CombatActionKind.LightAttack;
            if (profile.LightAttack != null && profile.LightAttack.Matches(item))
            {
                binding = profile.LightAttack;
                return true;
            }
            action = CombatActionKind.HeavyAttack;
            if (profile.HeavyAttack != null && profile.HeavyAttack.Matches(item))
            {
                binding = profile.HeavyAttack;
                return true;
            }
            action = CombatActionKind.Heal;
            if (profile.Heal != null && profile.Heal.Matches(item))
            {
                binding = profile.Heal;
                return true;
            }
            action = CombatActionKind.LockTarget;
            if (profile.LockTarget != null && profile.LockTarget.Matches(item))
            {
                binding = profile.LockTarget;
                return true;
            }
            return false;
        }

        private static List<CapturedFrame> SelectTrainingFrames(
            long actionTimestamp, List<CapturedFrame> source)
        {
            List<CapturedFrame> result = new List<CapturedFrame>();
            if (source.Count == 0)
                return result;
            int[] offsetsMs = new int[] { 350, 200, 50 };
            for (int i = 0; i < offsetsMs.Length; i++)
            {
                long target = actionTimestamp - Stopwatch.Frequency * offsetsMs[i] / 1000L;
                CapturedFrame best = null;
                long bestDistance = Int64.MaxValue;
                for (int j = 0; j < source.Count; j++)
                {
                    long distance = Math.Abs(source[j].Timestamp - target);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = source[j];
                    }
                }
                if (best != null && !ContainsFrame(result, best))
                    result.Add(best);
            }
            return result;
        }

        private static bool ContainsFrame(List<CapturedFrame> frames, CapturedFrame frame)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                if (Object.ReferenceEquals(frames[i], frame))
                    return true;
            }
            return false;
        }

        private static ImageCodecInfo FindJpegCodec()
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < codecs.Length; i++)
            {
                if (String.Equals(codecs[i].MimeType, "image/jpeg",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return codecs[i];
                }
            }
            throw new InvalidOperationException("系统缺少 JPEG 编码器。");
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

        private sealed class CapturedFrame
        {
            public long Timestamp;
            public byte[] JpegData;
        }
    }
}
