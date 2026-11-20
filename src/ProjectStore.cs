using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GameInputLab.Core
{
    public sealed class ProjectStore
    {
        public const string FormatId = "game-input-lab";
        public const int CurrentFormatVersion = 1;
        public const int MaximumEventCount = 250000;
        private const long MaximumFileBytes = 128L * 1024L * 1024L;

        private readonly JavaScriptSerializer _serializer;

        public ProjectStore()
        {
            _serializer = new JavaScriptSerializer();
            _serializer.MaxJsonLength = (int)MaximumFileBytes;
            _serializer.RecursionLimit = 100;
        }

        public string GetDefaultRecordingsDirectory()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (String.IsNullOrWhiteSpace(documents))
            {
                documents = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            return Path.Combine(documents, "GameInputLab", "Recordings");
        }

        public void Save(string path, TestProject project)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentException("保存路径不能为空。", "path");
            if (project == null)
                throw new ArgumentNullException("project");
            if (project.Events == null)
                project.Events = new List<RecordedEvent>();
            if (project.StrategyRules == null)
                project.StrategyRules = new List<StrategyRule>();
            if (project.Events.Count > MaximumEventCount)
                throw new InvalidDataException("事件数量超过 250,000 的安全上限，无法保存。");

            ValidateProject(project, true);
            project.FormatId = FormatId;
            project.FormatVersion = CurrentFormatVersion;
            project.Touch();

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (String.IsNullOrWhiteSpace(directory))
                throw new InvalidDataException("无法确定项目保存目录。");
            Directory.CreateDirectory(directory);

            string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string json = _serializer.Serialize(project);
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
                    catch (PlatformNotSupportedException)
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

        public TestProject Load(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentException("项目路径不能为空。", "path");
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("找不到测试项目文件。", fullPath);

            FileInfo file = new FileInfo(fullPath);
            if (file.Length <= 0)
                throw new InvalidDataException("测试项目文件为空。");
            if (file.Length > MaximumFileBytes)
                throw new InvalidDataException("测试项目文件超过 128 MB 安全上限。");

            string json = File.ReadAllText(fullPath, Encoding.UTF8);
            TestProject project;
            try
            {
                project = _serializer.Deserialize<TestProject>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("测试项目 JSON 无法解析：" + exception.Message, exception);
            }

            if (project == null)
                throw new InvalidDataException("测试项目内容为空。");
            if (project.Events == null)
                project.Events = new List<RecordedEvent>();
            if (project.StrategyRules == null)
                project.StrategyRules = new List<StrategyRule>();
            ValidateProject(project, false);

            project.Events.Sort(delegate(RecordedEvent first, RecordedEvent second)
            {
                return first.TimeMs.CompareTo(second.TimeMs);
            });
            return project;
        }

        private static void ValidateProject(TestProject project, bool saving)
        {
            if (project.FormatVersion > CurrentFormatVersion)
                throw new InvalidDataException(
                    "项目版本 " + project.FormatVersion + " 高于当前支持版本 " + CurrentFormatVersion + "。");
            if (!String.IsNullOrWhiteSpace(project.FormatId) &&
                !String.Equals(project.FormatId, FormatId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("文件不是游戏输入实验室项目。");
            }
            if (project.Events.Count > MaximumEventCount)
                throw new InvalidDataException("项目事件数超过 250,000 的安全上限。");
            if (project.StrategyRules.Count > 200)
                throw new InvalidDataException("智能策略规则不能超过 200 条。");
            for (int i = 0; i < project.StrategyRules.Count; i++)
            {
                if (project.StrategyRules[i] == null)
                    throw new InvalidDataException("第 " + (i + 1) + " 条智能策略为空。");
                project.StrategyRules[i].Normalize();
            }

            long previousTime = -1;
            for (int i = 0; i < project.Events.Count; i++)
            {
                RecordedEvent item = project.Events[i];
                if (item == null)
                    throw new InvalidDataException("第 " + (i + 1) + " 个事件为空。");
                if (item.TimeMs < 0 || item.TimeMs > 86400000L)
                    throw new InvalidDataException("第 " + (i + 1) + " 个事件的时间超出 24 小时范围。");
                int kind = (int)item.Kind;
                if (kind < 0 || kind > (int)RecordedEventKind.MouseRelativeMove)
                    throw new InvalidDataException("第 " + (i + 1) + " 个事件类型无效。");
                if (item.Kind == RecordedEventKind.KeyDown || item.Kind == RecordedEventKind.KeyUp)
                {
                    if (item.VirtualKey < 0 || item.VirtualKey > 255)
                        throw new InvalidDataException("第 " + (i + 1) + " 个键盘事件按键码无效。");
                }
                if (previousTime >= 0 && item.TimeMs < previousTime && saving)
                {
                    // The UI may produce a transiently unordered edit; save still succeeds,
                    // while Load sorts events before playback.
                }
                previousTime = item.TimeMs;
                if (String.IsNullOrWhiteSpace(item.KeyName))
                    item.KeyName = String.Empty;
            }

            if (String.IsNullOrWhiteSpace(project.Name))
                project.Name = "未命名测试";
            if (String.IsNullOrWhiteSpace(project.CreatedAt))
                project.CreatedAt = DateTime.UtcNow.ToString("o");
            if (String.IsNullOrWhiteSpace(project.UpdatedAt))
                project.UpdatedAt = project.CreatedAt;
            if (project.ScreenWidth <= 0)
                project.ScreenWidth = 1920;
            if (project.ScreenHeight <= 0)
                project.ScreenHeight = 1080;
            if (String.IsNullOrWhiteSpace(project.FormatId))
                project.FormatId = FormatId;
            project.FormatVersion = CurrentFormatVersion;
        }
    }
}
