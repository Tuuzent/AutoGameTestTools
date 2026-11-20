using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GameInputLab.Core
{
    public sealed class ScreenRecorder : IDisposable
    {
        private readonly object _stateLock = new object();
        private Thread _worker;
        private volatile bool _recording;
        private volatile bool _stopRequested;
        private bool _disposed;
        private string _currentPath;
        private long _elapsedMilliseconds;
        private int _framesCaptured;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public event Action<string> RecordingCompleted;
        public event Action<string> Error;

        public bool IsRecording
        {
            get { return _recording; }
        }

        public string CurrentPath
        {
            get { return _currentPath; }
        }

        public TimeSpan Elapsed
        {
            get
            {
                return TimeSpan.FromMilliseconds(
                    _recording ? _clock.ElapsedMilliseconds : _elapsedMilliseconds);
            }
        }

        public int FramesCaptured
        {
            get { return _framesCaptured; }
        }

        public string GetDefaultDirectory()
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (String.IsNullOrWhiteSpace(videos))
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                videos = Path.Combine(documents, "GameInputLab");
            }
            return Path.Combine(videos, "GameInputLab");
        }

        public string Start(string path, int framesPerSecond, int jpegQuality, int maxWidth)
        {
            if (_disposed)
                throw new ObjectDisposedException("ScreenRecorder");
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentException("录屏文件路径不能为空。", "path");

            lock (_stateLock)
            {
                if (_recording)
                    throw new InvalidOperationException("屏幕录制正在进行中。");

                string fullPath = Path.GetFullPath(path);
                string outputDirectory = Path.GetDirectoryName(fullPath);
                if (!String.IsNullOrWhiteSpace(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);
                Rectangle captureBounds = Screen.PrimaryScreen.Bounds;
                if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
                    throw new InvalidOperationException("无法读取主显示器尺寸。");

                int fps = Math.Max(5, Math.Min(30, framesPerSecond));
                int quality = Math.Max(30, Math.Min(95, jpegQuality));
                int outputWidth = captureBounds.Width;
                int outputHeight = captureBounds.Height;
                if (maxWidth > 0 && outputWidth > maxWidth)
                {
                    double scale = maxWidth / (double)outputWidth;
                    outputWidth = maxWidth;
                    outputHeight = (int)Math.Round(outputHeight * scale);
                }
                outputWidth = Math.Max(2, outputWidth - outputWidth % 2);
                outputHeight = Math.Max(2, outputHeight - outputHeight % 2);

                _stopRequested = false;
                _currentPath = fullPath;
                _elapsedMilliseconds = 0;
                _framesCaptured = 0;
                _recording = true;
                _clock.Reset();
                _clock.Start();

                _worker = new Thread(delegate()
                {
                    RecordWorker(captureBounds, outputWidth, outputHeight, fps, quality);
                });
                _worker.IsBackground = true;
                _worker.Name = "GameInputLab Screen Recorder";
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
                return _currentPath;
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
                    worker.Join(3000);
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
            _clock.Stop();
            GC.SuppressFinalize(this);
        }

        private void RecordWorker(Rectangle captureBounds, int outputWidth, int outputHeight,
            int fps, int quality)
        {
            string failure = null;
            string outputPath = _currentPath;
            AviMjpegWriter writer = null;
            try
            {
                ImageCodecInfo jpegCodec = FindJpegCodec();
                EncoderParameters encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, (long)quality);

                using (Bitmap source = new Bitmap(
                    captureBounds.Width, captureBounds.Height, PixelFormat.Format24bppRgb))
                using (Graphics sourceGraphics = Graphics.FromImage(source))
                using (Bitmap frame = new Bitmap(
                    outputWidth, outputHeight, PixelFormat.Format24bppRgb))
                using (Graphics frameGraphics = Graphics.FromImage(frame))
                using (MemoryStream jpegStream = new MemoryStream())
                {
                    frameGraphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    frameGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    writer = new AviMjpegWriter(outputPath, outputWidth, outputHeight, fps);

                    long frameIntervalTicks = Stopwatch.Frequency / fps;
                    long recordingStart = Stopwatch.GetTimestamp();
                    long frameIndex = 0;
                    while (!_stopRequested)
                    {
                        long targetTicks = recordingStart + frameIndex * frameIntervalTicks;
                        while (!_stopRequested)
                        {
                            long remaining = targetTicks - Stopwatch.GetTimestamp();
                            if (remaining <= 0)
                                break;
                            int sleepMs = (int)Math.Min(10L,
                                Math.Max(1L, remaining * 1000L / Stopwatch.Frequency));
                            Thread.Sleep(sleepMs);
                        }
                        if (_stopRequested)
                            break;

                        sourceGraphics.CopyFromScreen(
                            captureBounds.Left, captureBounds.Top, 0, 0,
                            captureBounds.Size, CopyPixelOperation.SourceCopy);
                        frameGraphics.DrawImage(source, new Rectangle(
                            0, 0, outputWidth, outputHeight));
                        DrawCursor(frameGraphics, captureBounds, outputWidth, outputHeight);

                        jpegStream.SetLength(0);
                        frame.Save(jpegStream, jpegCodec, encoderParameters);
                        writer.WriteFrame(jpegStream.GetBuffer(), (int)jpegStream.Length);
                        _framesCaptured++;
                        _elapsedMilliseconds = _clock.ElapsedMilliseconds;
                        frameIndex++;
                    }
                    writer.Close();
                    writer = null;
                }
            }
            catch (Exception exception)
            {
                failure = exception.Message;
            }
            finally
            {
                if (writer != null)
                {
                    try
                    {
                        writer.Close();
                    }
                    catch
                    {
                    }
                }
                _elapsedMilliseconds = _clock.ElapsedMilliseconds;
                _clock.Stop();
                _recording = false;
                _worker = null;
                if (failure == null && _framesCaptured == 0)
                    failure = "未捕获到任何视频帧。";
                if (failure != null)
                    RaiseError("录屏已停止：" + failure);
                else
                    RaiseCompleted(outputPath);
            }
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
            throw new InvalidOperationException("系统缺少 JPEG 编码器，无法创建录屏文件。");
        }

        private static void DrawCursor(Graphics graphics, Rectangle captureBounds,
            int outputWidth, int outputHeight)
        {
            try
            {
                Point cursor = Cursor.Position;
                if (!captureBounds.Contains(cursor))
                    return;
                float scaleX = outputWidth / (float)captureBounds.Width;
                float scaleY = outputHeight / (float)captureBounds.Height;
                int cursorSize = Math.Max(18, (int)Math.Round(28 * scaleX));
                Cursor.Current.Draw(graphics, new Rectangle(
                    (int)Math.Round((cursor.X - captureBounds.Left) * scaleX),
                    (int)Math.Round((cursor.Y - captureBounds.Top) * scaleY),
                    cursorSize, cursorSize));
            }
            catch
            {
            }
        }

        private void RaiseCompleted(string path)
        {
            Action<string> handler = RecordingCompleted;
            if (handler == null)
                return;
            try
            {
                handler(path);
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

    internal sealed class AviMjpegWriter : IDisposable
    {
        private const uint AVIF_HASINDEX = 0x00000010;
        private const uint AVIIF_KEYFRAME = 0x00000010;
        private const long MaximumFileSize = 1900L * 1024L * 1024L;

        private readonly FileStream _stream;
        private readonly BinaryWriter _writer;
        private readonly int _width;
        private readonly int _height;
        private readonly int _fps;
        private readonly List<IndexEntry> _index = new List<IndexEntry>();
        private readonly long _riffSizePosition;
        private readonly long _totalFramesPosition;
        private readonly long _streamLengthPosition;
        private readonly long _suggestedBufferPosition;
        private readonly long _moviSizePosition;
        private readonly long _moviDataStart;
        private bool _closed;
        private int _maximumFrameBytes;

        public AviMjpegWriter(string path, int width, int height, int fps)
        {
            _width = width;
            _height = height;
            _fps = fps;
            _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            _writer = new BinaryWriter(_stream);

            WriteFourCc("RIFF");
            _riffSizePosition = _stream.Position;
            _writer.Write((uint)0);
            WriteFourCc("AVI ");

            long headerListPosition = _stream.Position;
            WriteFourCc("LIST");
            long headerListSizePosition = _stream.Position;
            _writer.Write((uint)0);
            WriteFourCc("hdrl");

            WriteFourCc("avih");
            _writer.Write((uint)56);
            _writer.Write((uint)Math.Max(1, 1000000 / fps));
            _writer.Write((uint)Math.Max(1, width * height * 3 * fps / 8));
            _writer.Write((uint)0);
            _writer.Write(AVIF_HASINDEX);
            _totalFramesPosition = _stream.Position;
            _writer.Write((uint)0);
            _writer.Write((uint)0);
            _writer.Write((uint)1);
            _writer.Write((uint)Math.Max(1, width * height));
            _writer.Write((uint)width);
            _writer.Write((uint)height);
            _writer.Write((uint)0);
            _writer.Write((uint)0);
            _writer.Write((uint)0);
            _writer.Write((uint)0);

            WriteFourCc("LIST");
            long streamListSizePosition = _stream.Position;
            _writer.Write((uint)0);
            WriteFourCc("strl");

            WriteFourCc("strh");
            _writer.Write((uint)56);
            WriteFourCc("vids");
            WriteFourCc("MJPG");
            _writer.Write((uint)0);
            _writer.Write((ushort)0);
            _writer.Write((ushort)0);
            _writer.Write((uint)0);
            _writer.Write((uint)1);
            _writer.Write((uint)fps);
            _writer.Write((uint)0);
            _streamLengthPosition = _stream.Position;
            _writer.Write((uint)0);
            _suggestedBufferPosition = _stream.Position;
            _writer.Write((uint)Math.Max(1, width * height));
            _writer.Write((uint)0xFFFFFFFF);
            _writer.Write((uint)0);
            _writer.Write((short)0);
            _writer.Write((short)0);
            _writer.Write((short)Math.Min(width, 65535));
            _writer.Write((short)Math.Min(height, 65535));

            WriteFourCc("strf");
            _writer.Write((uint)40);
            _writer.Write((uint)40);
            _writer.Write(width);
            _writer.Write(height);
            _writer.Write((ushort)1);
            _writer.Write((ushort)24);
            WriteFourCc("MJPG");
            _writer.Write((uint)(width * height * 3));
            _writer.Write(0);
            _writer.Write(0);
            _writer.Write((uint)0);
            _writer.Write((uint)0);

            PatchSize(streamListSizePosition, _stream.Position);
            PatchSize(headerListSizePosition, _stream.Position);

            WriteFourCc("LIST");
            _moviSizePosition = _stream.Position;
            _writer.Write((uint)0);
            WriteFourCc("movi");
            _moviDataStart = _stream.Position;
        }

        public int FrameCount
        {
            get { return _index.Count; }
        }

        public void WriteFrame(byte[] jpegData, int length)
        {
            if (_closed)
                throw new ObjectDisposedException("AviMjpegWriter");
            if (jpegData == null || length <= 0)
                return;
            if (_stream.Position + length + 65536L > MaximumFileSize)
                throw new IOException("录屏文件已达到 1.9 GB 上限，请停止后保存。");

            long chunkPosition = _stream.Position;
            WriteFourCc("00dc");
            _writer.Write((uint)length);
            _writer.Write(jpegData, 0, length);
            if ((length & 1) != 0)
                _writer.Write((byte)0);

            long offset = chunkPosition - _moviDataStart + 4;
            _index.Add(new IndexEntry(offset, length));
            _maximumFrameBytes = Math.Max(_maximumFrameBytes, length);
        }

        public void Close()
        {
            if (_closed)
                return;
            _closed = true;

            long moviEnd = _stream.Position;
            PatchSize(_moviSizePosition, moviEnd);

            WriteFourCc("idx1");
            _writer.Write((uint)(_index.Count * 16));
            for (int i = 0; i < _index.Count; i++)
            {
                WriteFourCc("00dc");
                _writer.Write(AVIIF_KEYFRAME);
                _writer.Write((uint)_index[i].Offset);
                _writer.Write((uint)_index[i].Length);
            }

            PatchUInt32(_totalFramesPosition, (uint)_index.Count);
            PatchUInt32(_streamLengthPosition, (uint)_index.Count);
            PatchUInt32(_suggestedBufferPosition, (uint)Math.Max(1, _maximumFrameBytes));
            PatchUInt32(_riffSizePosition, (uint)Math.Max(0, _stream.Length - 8));
            _writer.Flush();
            _writer.Dispose();
            _stream.Dispose();
        }

        public void Dispose()
        {
            Close();
        }

        private void PatchSize(long position, long chunkEnd)
        {
            PatchUInt32(position, (uint)Math.Max(0, chunkEnd - position - 4));
        }

        private void PatchUInt32(long position, uint value)
        {
            _writer.Flush();
            long current = _stream.Position;
            _stream.Position = position;
            _writer.Write(value);
            _writer.Flush();
            _stream.Position = current;
        }

        private void WriteFourCc(string value)
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(value);
            if (bytes.Length != 4)
                throw new ArgumentException("FourCC must contain four characters.", "value");
            _writer.Write(bytes);
        }

        private struct IndexEntry
        {
            public readonly long Offset;
            public readonly int Length;

            public IndexEntry(long offset, int length)
            {
                Offset = offset;
                Length = length;
            }
        }
    }
}
