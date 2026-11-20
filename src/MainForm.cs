using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using GameInputLab.Core;
using GameInputLab.UI;

namespace GameInputLab
{
    internal enum RunMode
    {
        Idle,
        PreparingRecording,
        Recording,
        PreparingPlayback,
        Playing,
        PreparingStrategy,
        Strategy,
        PreparingCombatTraining,
        CombatTraining,
        PreparingCombatAgent,
        CombatAgent,
        Error
    }

    internal enum CameraPlaybackMode
    {
        Auto = 0,
        Absolute = 1,
        Relative = 2
    }

    public class MainForm : Form
    {
        private const int SidebarWidth = 338;
        private const int SidebarCardWidth = 290;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;

        private readonly object captureLock = new object();
        private readonly List<RecordedEvent> captureBuffer = new List<RecordedEvent>();
        private readonly List<RecordedEvent> undoBuffer = new List<RecordedEvent>();
        private readonly System.Windows.Forms.Timer prepareTimer;
        private readonly System.Windows.Forms.Timer statusTimer;

        private InputRecorder recorder;
        private PlaybackEngine player;
        private ScreenRecorder screenRecorder;
        private StrategyEngine strategyEngine;
        private CombatProfileStore combatProfileStore;
        private CombatProfile combatProfile;
        private string combatProfileDirectory;
        private CombatDemoTrainer combatDemoTrainer;
        private AdaptiveCombatAgent combatAgent;
        private ProjectStore projectStore;
        private AppSettings appSettings;
        private TestProject project;
        private RunMode runMode = RunMode.Idle;
        private string currentPath;
        private bool dirty;
        private bool loadingProject;
        private int prepareSecondsLeft;
        private int selectedEventIndex = -1;
        private bool restoringWindow;
        private bool recordingLimitNotified;
        private DateTime lastScreenRecordHotkeyUtc = DateTime.MinValue;
        private DateTime lastStrategyHotkeyUtc = DateTime.MinValue;
        private float appliedUiScale = 1f;
        private int strategyTriggerCount;
        private string lastStrategyMessage;
        private int combatTrainingExamples;
        private int combatActionCount;
        private string combatLastAction;

        private Panel titleBar;
        private Panel rootBody;
        private Panel sidebar;
        private Panel contentHost;
        private RoundedPanel statusCard;
        private StateBadge stateBadge;
        private Label statusTitle;
        private Label statusDetail;
        private RoundedButton recordButton;
        private RoundedButton replayButton;
        private Label eventCountValue;
        private Label clickCountValue;
        private Label durationValue;
        private Label keyCountValue;
        private RoundedButton openButton;
        private RoundedButton saveButton;
        private RoundedButton newButton;
        private RoundedButton videoRecordButton;
        private RoundedButton strategyRulesButton;
        private RoundedButton strategyRunButton;
        private ToolTip mainToolTip;
        private Button settingsButton;
        private Button minimizeButton;
        private Button maximizeButton;
        private Button closeButton;
        private TextBox projectNameBox;
        private Label projectPathLabel;
        private Label timelineCountLabel;
        private BufferedGrid eventGrid;
        private TimelinePreview preview;
        private Label selectedKindLabel;
        private Label selectedSummaryLabel;
        private Label selectedTimeLabel;
        private UiNumeric speedInput;
        private UiNumeric repeatInput;
        private UiNumeric countdownInput;
        private ComboBox cameraModeInput;
        private Label cameraModeHint;
        private RoundedButton playButton;
        private RoundedButton stopButton;
        private RoundedButton deleteButton;
        private RoundedButton undoButton;
        private Label globalProgressLabel;
        private ContextMenuStrip eventMenu;

        public MainForm()
        {
            appSettings = new AppSettingsStore().Load();
            UiTheme.ApplySettings(appSettings);
            InitializeWindow();
            BuildInterface();
            ApplyDpiScale();

            prepareTimer = new System.Windows.Forms.Timer();
            prepareTimer.Interval = 1000;
            prepareTimer.Tick += PrepareTimerTick;

            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = 120;
            statusTimer.Tick += StatusTimerTick;
            statusTimer.Start();
        }

        private void InitializeWindow()
        {
            Text = "InputLab 游戏输入实验室";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(1080, 720);
            ClientSize = new Size(1400, 880);
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            Font = UiTheme.Font(9f, false);
            KeyPreview = true;
            DoubleBuffered = true;
            Padding = new Padding(1);
            AllowDrop = true;
        }

        private void ApplyDpiScale()
        {
            float systemScale;
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                systemScale = graphics.DpiX / 96f;
            float scale = systemScale *
                (Math.Max(75, appSettings.InterfaceScalePercent) / 100f);
            appliedUiScale = scale;
            if (scale <= 1.01f)
                return;

            SuspendLayout();
            Scale(new SizeF(scale, scale));
            ResumeLayout(true);
            PerformLayout();
        }

        private void ApplyConfiguredWindowBounds()
        {
            Screen activeScreen = Screen.PrimaryScreen;
            Rectangle workArea = activeScreen.WorkingArea;
            MaximizedBounds = workArea;
            if (appSettings.StartMaximized)
            {
                WindowState = FormWindowState.Maximized;
                if (maximizeButton != null)
                    maximizeButton.Text = "❐";
                return;
            }

            WindowState = FormWindowState.Normal;
            int width = (int)Math.Round(appSettings.WindowWidth * appliedUiScale);
            int height = (int)Math.Round(appSettings.WindowHeight * appliedUiScale);
            width = Math.Max(MinimumSize.Width, Math.Min(workArea.Width, width));
            height = Math.Max(MinimumSize.Height, Math.Min(workArea.Height, height));
            Bounds = new Rectangle(
                workArea.Left + Math.Max(0, (workArea.Width - width) / 2),
                workArea.Top + Math.Max(0, (workArea.Height - height) / 2),
                width, height);
            if (maximizeButton != null)
                maximizeButton.Text = "□";
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            BeginInvoke((Action)delegate
            {
                ApplyConfiguredWindowBounds();
            });
            mainToolTip = new ToolTip();
            if (settingsButton != null)
                mainToolTip.SetToolTip(settingsButton, "界面与录屏设置");
            if (strategyRulesButton != null)
                mainToolTip.SetToolTip(strategyRulesButton,
                    "黑神话悟空战斗代理：选择观察区域并录制示范");
            if (strategyRunButton != null)
                mainToolTip.SetToolTip(strategyRunButton,
                    "启动示范学习的战斗代理，按 F11 停止");
            screenRecorder = new ScreenRecorder();
            screenRecorder.RecordingCompleted += ScreenRecordingCompleted;
            screenRecorder.Error += ScreenRecordingError;
            strategyEngine = new StrategyEngine();
            strategyEngine.StatusChanged += StrategyStatusChanged;
            strategyEngine.RuleTriggered += StrategyRuleTriggered;
            strategyEngine.Error += StrategyError;
            combatProfileStore = new CombatProfileStore();
            combatProfileDirectory = combatProfileStore.GetDefaultDirectory();
            combatProfile = combatProfileStore.LoadDefault();
            try
            {
                projectStore = new ProjectStore();
                recorder = new InputRecorder();
                player = new PlaybackEngine();
                WireRuntimeEvents();
                StartNewProject(false);
                ShowToast(recorder.RawInputAvailable
                    ? "已就绪。F8 录制操作，F10 录制屏幕。"
                    : (recorder.InitializationWarning ??
                        "已就绪，但相对镜头捕获未启用。"),
                    recorder.RawInputAvailable ? ToastKind.Info : ToastKind.Warning);
            }
            catch (Exception exception)
            {
                SetRunMode(RunMode.Error);
                statusTitle.Text = "初始化失败";
                statusDetail.Text = exception.Message;
                UiToast.Show(this, "输入引擎初始化失败：" + exception.Message, ToastKind.Error);
            }
            finally
            {
                RefreshVideoButton();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (screenRecorder != null && screenRecorder.IsRecording)
            {
                DialogResult videoResult = MessageBox.Show(
                    "屏幕录制正在进行。停止并保存录屏后退出吗？",
                    "停止屏幕录制",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (videoResult != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                screenRecorder.Stop();
                RefreshVideoButton();
            }

            if (runMode == RunMode.Recording || runMode == RunMode.Playing ||
                runMode == RunMode.Strategy || runMode == RunMode.CombatTraining ||
                runMode == RunMode.CombatAgent)
            {
                DialogResult result = MessageBox.Show(
                    "当前正在录制或回放。停止并退出吗？",
                    "确认退出",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                AbortCurrentOperation(true);
            }

            if (dirty && project != null && project.Events.Count > 0)
            {
                DialogResult result = MessageBox.Show(
                    "当前测试尚未保存。退出前保存吗？",
                    "保存测试",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (result == DialogResult.Yes && !SaveProject(false))
                {
                    e.Cancel = true;
                    return;
                }
            }

            prepareTimer.Stop();
            statusTimer.Stop();
            DisposeRuntimeEvents();
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && m.Result == (IntPtr)HTCLIENT &&
                WindowState == FormWindowState.Normal)
            {
                Point cursor = PointToClient(Cursor.Position);
                int grip = 6;
                bool left = cursor.X <= grip;
                bool right = cursor.X >= ClientSize.Width - grip;
                bool top = cursor.Y <= grip;
                bool bottom = cursor.Y >= ClientSize.Height - grip;

                if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(UiTheme.Border))
            {
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            if (drgevent.Data.GetDataPresent(DataFormats.FileDrop) &&
                AcceptsIdleActions())
            {
                drgevent.Effect = DragDropEffects.Copy;
            }
            else
            {
                drgevent.Effect = DragDropEffects.None;
            }
            base.OnDragEnter(drgevent);
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            string[] files = drgevent.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                LoadProject(files[0]);
            }
            base.OnDragDrop(drgevent);
        }

        private void BuildInterface()
        {
            BuildTitleBar();
            BuildBody();
            BuildEventMenu();
            ResumeLayout(true);
        }

        private void BuildTitleBar()
        {
            titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = 60;
            titleBar.BackColor = UiTheme.Surface;
            titleBar.Padding = new Padding(18, 0, 10, 0);
            titleBar.MouseDown += TitleBarMouseDown;
            titleBar.DoubleClick += TitleBarDoubleClick;
            titleBar.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen divider = new Pen(UiTheme.BorderSoft))
                {
                    e.Graphics.DrawLine(divider, 0, titleBar.Height - 1,
                        titleBar.Width, titleBar.Height - 1);
                }
            };
            Controls.Add(titleBar);

            Label mark = new Label();
            mark.AutoSize = false;
            mark.Size = new Size(36, 36);
            mark.Location = new Point(18, 12);
            mark.BackColor = UiTheme.Text;
            mark.ForeColor = Color.White;
            mark.Font = UiTheme.Font(11.8f, true);
            mark.Text = "IL";
            mark.TextAlign = ContentAlignment.MiddleCenter;
            mark.MouseDown += TitleBarMouseDown;
            titleBar.Controls.Add(mark);

            Label title = new Label();
            title.AutoSize = true;
            title.Location = new Point(64, 16);
            title.ForeColor = UiTheme.Text;
            title.Font = UiTheme.Font(12.6f, true);
            title.Text = "INPUTLAB";
            title.MouseDown += TitleBarMouseDown;
            titleBar.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.AutoSize = true;
            subtitle.Location = new Point(224, 21);
            subtitle.ForeColor = UiTheme.TextFaint;
            subtitle.Font = UiTheme.Font(9.6f, false);
            subtitle.Text = "游戏操作录制与回放";
            subtitle.MouseDown += TitleBarMouseDown;
            titleBar.Controls.Add(subtitle);

            closeButton = CreateWindowButton("×", false);
            closeButton.Dock = DockStyle.Right;
            closeButton.Width = 50;
            closeButton.Click += delegate { Close(); };
            titleBar.Controls.Add(closeButton);

            maximizeButton = CreateWindowButton("□", false);
            maximizeButton.Dock = DockStyle.Right;
            maximizeButton.Width = 50;
            maximizeButton.Click += ToggleMaximize;
            titleBar.Controls.Add(maximizeButton);

            minimizeButton = CreateWindowButton("—", false);
            minimizeButton.Dock = DockStyle.Right;
            minimizeButton.Width = 50;
            minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };
            titleBar.Controls.Add(minimizeButton);

            settingsButton = CreateWindowButton("⚙", false);
            settingsButton.Dock = DockStyle.Right;
            settingsButton.Width = 50;
            settingsButton.Click += delegate { OpenSettings(); };
            titleBar.Controls.Add(settingsButton);
        }

        private Button CreateWindowButton(string text, bool close)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = UiTheme.Surface;
            button.ForeColor = UiTheme.TextMuted;
            button.Font = UiTheme.Font(12f, false);
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            button.MouseEnter += WindowButtonMouseEnter;
            button.MouseLeave += WindowButtonMouseLeave;
            return button;
        }

        private void WindowButtonMouseEnter(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;
            bool isClose = button.Text == "×";
            button.BackColor = isClose ? UiTheme.Record : UiTheme.SurfaceHover;
            button.ForeColor = isClose ? Color.White : UiTheme.Text;
        }

        private void WindowButtonMouseLeave(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;
            button.BackColor = UiTheme.Surface;
            button.ForeColor = UiTheme.TextMuted;
        }

        private void TitleBarMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        private void TitleBarDoubleClick(object sender, EventArgs e)
        {
            ToggleMaximize(sender, EventArgs.Empty);
        }

        private void ToggleMaximize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                maximizeButton.Text = "□";
            }
            else
            {
                MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
                WindowState = FormWindowState.Maximized;
                maximizeButton.Text = "❐";
            }
        }

        private void BuildBody()
        {
            rootBody = new Panel();
            rootBody.Dock = DockStyle.Fill;
            rootBody.BackColor = UiTheme.Background;
            Controls.Add(rootBody);
            rootBody.BringToFront();

            BuildSidebar();
            BuildContent();
        }

        private void BuildSidebar()
        {
            sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = SidebarWidth;
            sidebar.BackColor = UiTheme.Sidebar;
            sidebar.Padding = new Padding(24, 18, 24, 18);
            rootBody.Controls.Add(sidebar);
            sidebar.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen divider = new Pen(UiTheme.BorderSoft))
                {
                    e.Graphics.DrawLine(divider, sidebar.Width - 1, 0,
                        sidebar.Width - 1, sidebar.Height);
                }
            };

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.BackColor = Color.Transparent;
            sidebar.Controls.Add(flow);

            statusCard = new RoundedPanel();
            statusCard.Size = new Size(SidebarCardWidth, 124);
            statusCard.BackColor = UiTheme.Surface;
            statusCard.BorderColor = UiTheme.BorderSoft;
            statusCard.Margin = new Padding(0, 0, 0, 12);
            flow.Controls.Add(statusCard);

            stateBadge = new StateBadge();
            stateBadge.Location = new Point(15, 14);
            stateBadge.Size = new Size(128, 34);
            statusCard.Controls.Add(stateBadge);

            statusTitle = NewLabel("操作就绪", 10.7f, true, UiTheme.Text);
            statusTitle.Location = new Point(16, 57);
            statusTitle.Size = new Size(SidebarCardWidth - 32, 27);
            statusCard.Controls.Add(statusTitle);

            statusDetail = NewLabel("录制一次完整游玩并自动回放", 8.9f, false, UiTheme.TextMuted);
            statusDetail.Location = new Point(16, 88);
            statusDetail.Size = new Size(SidebarCardWidth - 32, 24);
            statusCard.Controls.Add(statusDetail);

            recordButton = NewActionButton("●  开始录制", ButtonRole.Danger);
            recordButton.Size = new Size(SidebarCardWidth, 58);
            recordButton.Margin = new Padding(0, 0, 0, 9);
            recordButton.Click += delegate { BeginRecording(); };
            flow.Controls.Add(recordButton);

            replayButton = NewActionButton("▶  开始回放", ButtonRole.Primary);
            replayButton.Size = new Size(SidebarCardWidth, 54);
            replayButton.Margin = new Padding(0, 0, 0, 17);
            replayButton.Click += delegate { BeginPlayback(); };
            flow.Controls.Add(replayButton);

            flow.Controls.Add(NewSectionLabel("本轮概览"));
            eventCountValue = BuildStatsCard(flow, out clickCountValue, out durationValue, out keyCountValue);

            flow.Controls.Add(NewSectionLabel("全局快捷键"));
            RoundedPanel hotkeys = new RoundedPanel();
            hotkeys.Size = new Size(SidebarCardWidth, 126);
            hotkeys.BackColor = UiTheme.Surface;
            hotkeys.BorderColor = UiTheme.BorderSoft;
            hotkeys.Margin = new Padding(0, 6, 0, 12);
            flow.Controls.Add(hotkeys);
            AddHotkeyRow(hotkeys, 14, "F8", "开始 / 停止录制");
            AddHotkeyRow(hotkeys, 47, "F9", "开始 / 停止回放");
            AddHotkeyRow(hotkeys, 80, "F12", "紧急停止");

            Label safety = NewLabel(
                "相对镜头使用鼠标原始位移，不依赖屏幕坐标；仍建议保持相同分辨率和窗口布局。",
                8.6f, false, UiTheme.TextFaint);
            safety.Size = new Size(SidebarCardWidth, 76);
            safety.Margin = new Padding(0, 3, 0, 0);
            flow.Controls.Add(safety);
        }

        private Label BuildStatsCard(FlowLayoutPanel flow, out Label clickValue,
            out Label durationText, out Label keyValue)
        {
            RoundedPanel card = new RoundedPanel();
            card.Size = new Size(SidebarCardWidth, 140);
            card.BackColor = UiTheme.Surface;
            card.BorderColor = UiTheme.BorderSoft;
            card.Margin = new Padding(0, 6, 0, 17);
            flow.Controls.Add(card);

            Label totalTitle = NewLabel("事件", 8.4f, false, UiTheme.TextFaint);
            totalTitle.Location = new Point(16, 15);
            card.Controls.Add(totalTitle);
            eventCountValue = NewLabel("0", 17.5f, true, UiTheme.Text);
            eventCountValue.Location = new Point(15, 35);
            eventCountValue.Size = new Size(118, 36);
            card.Controls.Add(eventCountValue);

            Label clickTitle = NewLabel("输入", 8.4f, false, UiTheme.TextFaint);
            clickTitle.Location = new Point(152, 15);
            card.Controls.Add(clickTitle);
            clickValue = NewLabel("0", 17.5f, true, UiTheme.Text);
            clickValue.Location = new Point(151, 35);
            clickValue.Size = new Size(120, 36);
            card.Controls.Add(clickValue);

            Label durationTitle = NewLabel("时长", 8.4f, false, UiTheme.TextFaint);
            durationTitle.Location = new Point(16, 80);
            card.Controls.Add(durationTitle);
            durationText = NewLabel("00:00.0", 11.3f, true, UiTheme.Text);
            durationText.Location = new Point(15, 100);
            durationText.Size = new Size(128, 30);
            card.Controls.Add(durationText);

            Label keyTitle = NewLabel("键盘", 8.4f, false, UiTheme.TextFaint);
            keyTitle.Location = new Point(152, 80);
            card.Controls.Add(keyTitle);
            keyValue = NewLabel("0", 11.3f, true, UiTheme.Text);
            keyValue.Location = new Point(151, 100);
            keyValue.Size = new Size(110, 30);
            card.Controls.Add(keyValue);
            return eventCountValue;
        }

        private void AddHotkeyRow(Control parent, int top, string key, string description)
        {
            KeyCap cap = new KeyCap();
            cap.Text = key;
            cap.Location = new Point(13, top);
            parent.Controls.Add(cap);
            Label label = NewLabel(description, 9f, false, UiTheme.TextMuted);
            label.Location = new Point(74, top + 1);
            label.Size = new Size(SidebarCardWidth - 87, 24);
            parent.Controls.Add(label);
        }

        private void BuildContent()
        {
            contentHost = new Panel();
            contentHost.Dock = DockStyle.Fill;
            contentHost.BackColor = UiTheme.Background;
            contentHost.Padding = new Padding(28, 18, 28, 20);
            rootBody.Controls.Add(contentHost);
            contentHost.BringToFront();

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            layout.BackColor = Color.Transparent;
            contentHost.Controls.Add(layout);

            layout.Controls.Add(BuildProjectToolbar(), 0, 0);
            layout.Controls.Add(BuildWorkspace(), 0, 1);
            layout.Controls.Add(BuildPlaybackBar(), 0, 2);
        }

        private Control BuildProjectToolbar()
        {
            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.BackColor = Color.Transparent;
            toolbar.Margin = new Padding(0);

            Label eyebrow = NewLabel("测试项目", 8.2f, true, UiTheme.Accent);
            eyebrow.Location = new Point(0, 2);
            eyebrow.Size = new Size(200, 19);
            toolbar.Controls.Add(eyebrow);

            projectNameBox = new TextBox();
            projectNameBox.BorderStyle = BorderStyle.None;
            projectNameBox.BackColor = UiTheme.Background;
            projectNameBox.ForeColor = UiTheme.Text;
            projectNameBox.Font = UiTheme.Font(17.8f, true);
            projectNameBox.Location = new Point(0, 25);
            projectNameBox.Size = new Size(420, 38);
            projectNameBox.TextChanged += ProjectNameChanged;
            toolbar.Controls.Add(projectNameBox);

            projectPathLabel = NewLabel("尚未保存", 9.2f, false, UiTheme.TextFaint);
            projectPathLabel.Location = new Point(442, 34);
            projectPathLabel.Size = new Size(330, 24);
            projectPathLabel.AutoEllipsis = true;
            toolbar.Controls.Add(projectPathLabel);

            newButton = NewToolbarButton("新建");
            newButton.Size = new Size(78, 40);
            newButton.Click += delegate { NewProject(); };
            toolbar.Controls.Add(newButton);

            openButton = NewToolbarButton("打开");
            openButton.Size = new Size(78, 40);
            openButton.Click += delegate { OpenProject(); };
            toolbar.Controls.Add(openButton);

            saveButton = NewToolbarButton("保存");
            saveButton.Size = new Size(78, 40);
            saveButton.Click += delegate { SaveProject(false); };
            toolbar.Controls.Add(saveButton);

            videoRecordButton = NewToolbarButton("● 录屏 F10");
            videoRecordButton.Size = new Size(120, 40);
            videoRecordButton.Font = UiTheme.Font(9.2f, true);
            videoRecordButton.Click += delegate { ToggleScreenRecording(); };
            toolbar.Controls.Add(videoRecordButton);

            strategyRulesButton = NewToolbarButton("悟空代理");
            strategyRulesButton.Size = new Size(100, 40);
            strategyRulesButton.Click += delegate { OpenWukongAgent(); };
            toolbar.Controls.Add(strategyRulesButton);

            strategyRunButton = NewToolbarButton("▶ 战斗 F11");
            strategyRunButton.Size = new Size(120, 40);
            strategyRunButton.Click += delegate { ToggleCombatAgent(); };
            toolbar.Controls.Add(strategyRunButton);

            toolbar.Resize += delegate
            {
                saveButton.Location = new Point(toolbar.Width - saveButton.Width, 12);
                openButton.Location = new Point(saveButton.Left - openButton.Width - 9, 12);
                newButton.Location = new Point(openButton.Left - newButton.Width - 9, 12);
                videoRecordButton.Location =
                    new Point(newButton.Left - videoRecordButton.Width - 9, 12);
                strategyRunButton.Location =
                    new Point(videoRecordButton.Left - strategyRunButton.Width - 9, 12);
                strategyRulesButton.Location =
                    new Point(strategyRunButton.Left - strategyRulesButton.Width - 9, 12);
                projectNameBox.Width = Math.Max(180,
                    Math.Min(420, strategyRulesButton.Left - 18));
                int pathWidth = strategyRulesButton.Left - projectPathLabel.Left - 18;
                projectPathLabel.Visible = pathWidth >= 90;
                projectPathLabel.Width = Math.Max(0, pathWidth);
            };
            return toolbar;
        }

        private Control BuildWorkspace()
        {
            TableLayoutPanel workspace = new TableLayoutPanel();
            workspace.Dock = DockStyle.Fill;
            workspace.ColumnCount = 2;
            workspace.RowCount = 1;
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64f));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));
            workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            workspace.BackColor = Color.Transparent;
            workspace.Margin = new Padding(0);

            workspace.Controls.Add(BuildTimelineCard(), 0, 0);
            workspace.Controls.Add(BuildInspector(), 1, 0);
            return workspace;
        }

        private Control BuildTimelineCard()
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.BackColor = UiTheme.Surface;
            card.BorderColor = UiTheme.BorderSoft;
            card.Margin = new Padding(0, 0, 10, 0);
            card.Padding = new Padding(1);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0);
            content.Padding = new Padding(0);
            content.ColumnCount = 1;
            content.RowCount = 2;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            card.Controls.Add(content);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0);
            header.BackColor = Color.Transparent;
            content.Controls.Add(header, 0, 0);

            Label title = NewLabel("操作时间线", 11.2f, true, UiTheme.Text);
            title.Location = new Point(18, 16);
            title.Size = new Size(160, 27);
            header.Controls.Add(title);

            timelineCountLabel = NewLabel("0 个事件", 8.8f, false, UiTheme.TextMuted);
            timelineCountLabel.Location = new Point(176, 19);
            timelineCountLabel.Size = new Size(230, 24);
            header.Controls.Add(timelineCountLabel);

            undoButton = new RoundedButton();
            undoButton.Text = "撤销";
            undoButton.Role = ButtonRole.Quiet;
            undoButton.Font = UiTheme.Font(9.1f, true);
            undoButton.Size = new Size(64, 32);
            undoButton.Enabled = false;
            undoButton.Click += UndoClicked;
            header.Controls.Add(undoButton);

            deleteButton = new RoundedButton();
            deleteButton.Text = "删除所选";
            deleteButton.Role = ButtonRole.Quiet;
            deleteButton.Font = UiTheme.Font(9.1f, true);
            deleteButton.Size = new Size(94, 32);
            deleteButton.Enabled = false;
            deleteButton.Click += delegate { DeleteSelectedEvents(); };
            header.Controls.Add(deleteButton);

            header.Resize += delegate
            {
                deleteButton.Location = new Point(header.Width - deleteButton.Width - 14, 13);
                undoButton.Location = new Point(deleteButton.Left - undoButton.Width - 6, 13);
            };

            eventGrid = new BufferedGrid();
            eventGrid.Dock = DockStyle.Fill;
            eventGrid.BackgroundColor = UiTheme.Surface;
            eventGrid.BorderStyle = BorderStyle.None;
            eventGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            eventGrid.GridColor = UiTheme.BorderSoft;
            eventGrid.EnableHeadersVisualStyles = false;
            eventGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            eventGrid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.SurfaceRaised;
            eventGrid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextMuted;
            eventGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.SurfaceRaised;
            eventGrid.ColumnHeadersDefaultCellStyle.Font = UiTheme.Font(9.3f, true);
            eventGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 0, 0);
            eventGrid.ColumnHeadersHeight = 46;
            eventGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            eventGrid.DefaultCellStyle.BackColor = UiTheme.Surface;
            eventGrid.DefaultCellStyle.ForeColor = UiTheme.Text;
            eventGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 242, 237);
            eventGrid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
            eventGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 252);
            eventGrid.AlternatingRowsDefaultCellStyle.ForeColor = UiTheme.Text;
            eventGrid.DefaultCellStyle.Font = UiTheme.Font(9.5f, false);
            eventGrid.DefaultCellStyle.Padding = new Padding(10, 0, 5, 0);
            eventGrid.RowTemplate.Height = 44;
            eventGrid.RowHeadersVisible = false;
            eventGrid.ReadOnly = true;
            eventGrid.AllowUserToAddRows = false;
            eventGrid.AllowUserToDeleteRows = false;
            eventGrid.AllowUserToResizeRows = false;
            eventGrid.MultiSelect = true;
            eventGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            eventGrid.AutoGenerateColumns = false;
            eventGrid.VirtualMode = true;
            eventGrid.ShowCellToolTips = true;
            eventGrid.CellValueNeeded += EventGridCellValueNeeded;
            eventGrid.SelectionChanged += EventGridSelectionChanged;
            eventGrid.CellDoubleClick += EventGridCellDoubleClick;
            eventGrid.KeyDown += EventGridKeyDown;
            eventGrid.CellMouseDown += EventGridCellMouseDown;
            eventGrid.Paint += EventGridPaint;
            eventGrid.ScrollBars = ScrollBars.Vertical;
            AddGridColumns();
            Panel gridHost = new Panel();
            gridHost.Dock = DockStyle.Fill;
            gridHost.Margin = new Padding(0);
            gridHost.BackColor = UiTheme.Surface;
            eventGrid.Margin = new Padding(0);
            gridHost.Controls.Add(eventGrid);

            content.Controls.Add(gridHost, 0, 1);

            return card;
        }

        private void AddGridColumns()
        {
            DataGridViewTextBoxColumn index = new DataGridViewTextBoxColumn();
            index.HeaderText = "#";
            index.Width = 56;
            index.Resizable = DataGridViewTriState.False;
            index.ReadOnly = true;
            eventGrid.Columns.Add(index);

            DataGridViewTextBoxColumn time = new DataGridViewTextBoxColumn();
            time.HeaderText = "时间";
            time.Width = 116;
            time.ReadOnly = true;
            eventGrid.Columns.Add(time);

            DataGridViewTextBoxColumn kind = new DataGridViewTextBoxColumn();
            kind.HeaderText = "操作";
            kind.Width = 136;
            kind.ReadOnly = true;
            eventGrid.Columns.Add(kind);

            DataGridViewTextBoxColumn detail = new DataGridViewTextBoxColumn();
            detail.HeaderText = "输入内容";
            detail.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            detail.MinimumWidth = 180;
            detail.ReadOnly = true;
            eventGrid.Columns.Add(detail);
        }

        private Control BuildInspector()
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.BackColor = UiTheme.Surface;
            card.BorderColor = UiTheme.BorderSoft;
            card.Margin = new Padding(0, 0, 0, 0);
            card.Padding = new Padding(1);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0);
            content.Padding = new Padding(0);
            content.ColumnCount = 1;
            content.RowCount = 2;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 146f));
            card.Controls.Add(content);

            Panel detail = new Panel();
            detail.Dock = DockStyle.Fill;
            detail.Margin = new Padding(0);
            detail.BackColor = Color.Transparent;
            detail.Padding = new Padding(17, 12, 17, 12);
            content.Controls.Add(detail, 0, 1);

            Label title = NewLabel("事件详情", 8.6f, true, UiTheme.TextFaint);
            title.Location = new Point(17, 11);
            title.Size = new Size(100, 18);
            detail.Controls.Add(title);

            selectedKindLabel = NewLabel("尚未选择事件", 11.2f, true, UiTheme.Text);
            selectedKindLabel.Location = new Point(17, 37);
            selectedKindLabel.Size = new Size(320, 28);
            detail.Controls.Add(selectedKindLabel);

            selectedSummaryLabel = NewLabel("在左侧时间线中选择一行", 9.2f, false, UiTheme.TextMuted);
            selectedSummaryLabel.Location = new Point(17, 69);
            selectedSummaryLabel.Size = new Size(330, 24);
            detail.Controls.Add(selectedSummaryLabel);

            selectedTimeLabel = NewLabel("—", 8.8f, false, UiTheme.TextFaint);
            selectedTimeLabel.Location = new Point(17, 101);
            selectedTimeLabel.Size = new Size(320, 24);
            detail.Controls.Add(selectedTimeLabel);

            preview = new TimelinePreview();
            preview.Dock = DockStyle.Fill;
            preview.Margin = new Padding(0);
            content.Controls.Add(preview, 0, 0);
            return card;
        }

        private Control BuildPlaybackBar()
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.BackColor = UiTheme.Surface;
            card.BorderColor = UiTheme.BorderSoft;
            card.Margin = new Padding(0, 8, 0, 0);
            card.Padding = new Padding(1);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(18, 10, 18, 12);
            content.ColumnCount = 12;
            content.RowCount = 2;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124f));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            card.Controls.Add(content);

            Panel headingRow = new Panel();
            headingRow.Dock = DockStyle.Fill;
            headingRow.Margin = new Padding(0);
            headingRow.BackColor = Color.Transparent;
            content.Controls.Add(headingRow, 0, 0);
            content.SetColumnSpan(headingRow, 12);

            Label title = NewLabel("回放设置", 8.8f, true, UiTheme.TextFaint);
            title.Location = new Point(0, 1);
            title.Size = new Size(88, 31);
            headingRow.Controls.Add(title);

            Label cameraLabel = NewLabel("镜头", 8.9f, false, UiTheme.TextMuted);
            cameraLabel.Location = new Point(92, 1);
            cameraLabel.Size = new Size(46, 31);
            headingRow.Controls.Add(cameraLabel);

            cameraModeInput = new ComboBox();
            cameraModeInput.DropDownStyle = ComboBoxStyle.DropDownList;
            cameraModeInput.FlatStyle = FlatStyle.Flat;
            cameraModeInput.BackColor = UiTheme.SurfaceRaised;
            cameraModeInput.ForeColor = UiTheme.Text;
            cameraModeInput.Font = UiTheme.Font(9f, false);
            cameraModeInput.Items.Add("自动识别");
            cameraModeInput.Items.Add("绝对坐标");
            cameraModeInput.Items.Add("相对镜头");
            cameraModeInput.SelectedIndex = 0;
            cameraModeInput.Location = new Point(140, 1);
            cameraModeInput.Size = new Size(130, 30);
            headingRow.Controls.Add(cameraModeInput);

            cameraModeHint = NewLabel(
                "相对镜头时，每次点击会先对齐录制时的绝对位置",
                8.8f, false, UiTheme.TextFaint);
            cameraModeHint.AutoEllipsis = true;
            cameraModeHint.Size = new Size(390, 31);
            cameraModeHint.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cameraModeHint.TextAlign = ContentAlignment.MiddleRight;
            headingRow.Controls.Add(cameraModeHint);
            headingRow.Resize += delegate
            {
                cameraModeHint.Left = Math.Max(280, headingRow.Width - cameraModeHint.Width);
            };

            Label speedLabel = NewLabel("速度", 8.8f, false, UiTheme.TextMuted);
            speedLabel.Dock = DockStyle.Fill;
            speedLabel.TextAlign = ContentAlignment.MiddleLeft;
            content.Controls.Add(speedLabel, 0, 1);

            speedInput = NewNumeric(0, 0, 88, 0.25m, 4m, 0.25m, 1m, 2);
            speedInput.Margin = new Padding(0, 10, 0, 10);
            content.Controls.Add(speedInput, 1, 1);

            Label speedSuffix = NewLabel("×", 9.6f, true, UiTheme.TextMuted);
            speedSuffix.Dock = DockStyle.Fill;
            speedSuffix.TextAlign = ContentAlignment.MiddleCenter;
            content.Controls.Add(speedSuffix, 2, 1);

            Label repeatLabel = NewLabel("重复", 8.8f, false, UiTheme.TextMuted);
            repeatLabel.Dock = DockStyle.Fill;
            repeatLabel.TextAlign = ContentAlignment.MiddleLeft;
            content.Controls.Add(repeatLabel, 3, 1);

            repeatInput = NewNumeric(0, 0, 72, 1m, 999m, 1m, 1m, 0);
            repeatInput.Margin = new Padding(0, 10, 0, 10);
            content.Controls.Add(repeatInput, 4, 1);

            Label repeatSuffix = NewLabel("次", 8.8f, false, UiTheme.TextMuted);
            repeatSuffix.Dock = DockStyle.Fill;
            repeatSuffix.TextAlign = ContentAlignment.MiddleCenter;
            content.Controls.Add(repeatSuffix, 5, 1);

            Label countdownLabel = NewLabel("倒计时", 8.8f, false, UiTheme.TextMuted);
            countdownLabel.Dock = DockStyle.Fill;
            countdownLabel.TextAlign = ContentAlignment.MiddleLeft;
            content.Controls.Add(countdownLabel, 6, 1);

            countdownInput = NewNumeric(0, 0, 68, 0m, 10m, 1m, 3m, 0);
            countdownInput.Margin = new Padding(0, 10, 0, 10);
            content.Controls.Add(countdownInput, 7, 1);

            Label countdownSuffix = NewLabel("秒", 8.8f, false, UiTheme.TextMuted);
            countdownSuffix.Dock = DockStyle.Fill;
            countdownSuffix.TextAlign = ContentAlignment.MiddleCenter;
            content.Controls.Add(countdownSuffix, 8, 1);

            globalProgressLabel = NewLabel("准备就绪", 9.1f, false, UiTheme.TextMuted);
            globalProgressLabel.Dock = DockStyle.Fill;
            globalProgressLabel.TextAlign = ContentAlignment.MiddleLeft;
            globalProgressLabel.Margin = new Padding(8, 0, 8, 0);
            globalProgressLabel.AutoEllipsis = true;
            content.Controls.Add(globalProgressLabel, 9, 1);

            stopButton = new RoundedButton();
            stopButton.Text = "停止";
            stopButton.Role = ButtonRole.Danger;
            stopButton.Dock = DockStyle.Fill;
            stopButton.Margin = new Padding(0, 8, 8, 8);
            stopButton.Font = UiTheme.Font(9.5f, true);
            stopButton.Click += delegate { AbortCurrentOperation(false); };
            content.Controls.Add(stopButton, 10, 1);

            playButton = new RoundedButton();
            playButton.Text = "▶  执行";
            playButton.Role = ButtonRole.Primary;
            playButton.Dock = DockStyle.Fill;
            playButton.Margin = new Padding(0, 8, 0, 8);
            playButton.Font = UiTheme.Font(9.8f, true);
            playButton.Click += delegate { BeginPlayback(); };
            content.Controls.Add(playButton, 11, 1);
            return card;
        }

        private void BuildEventMenu()
        {
            eventMenu = new ContextMenuStrip();
            eventMenu.BackColor = UiTheme.SurfaceRaised;
            eventMenu.ForeColor = UiTheme.Text;
            eventMenu.ShowImageMargin = false;
            ToolStripMenuItem delete = new ToolStripMenuItem("删除选中事件");
            delete.Click += delegate { DeleteSelectedEvents(); };
            eventMenu.Items.Add(delete);
            ToolStripMenuItem copy = new ToolStripMenuItem("复制事件摘要");
            copy.Click += CopySelectedEvents;
            eventMenu.Items.Add(copy);
            eventMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem clear = new ToolStripMenuItem("清空时间线");
            clear.Click += delegate { ClearTimeline(); };
            eventMenu.Items.Add(clear);
        }

        private RoundedButton NewActionButton(string text, ButtonRole role)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Role = role;
            button.CornerRadius = 12;
            button.Font = UiTheme.Font(10f, true);
            return button;
        }

        private RoundedButton NewToolbarButton(string text)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Role = ButtonRole.Secondary;
            button.Size = new Size(66, 34);
            button.Font = UiTheme.Font(9.1f, true);
            return button;
        }

        private Label NewSectionLabel(string text)
        {
            Label label = NewLabel(text.ToUpperInvariant(), 8.2f, true, UiTheme.TextFaint);
            label.Size = new Size(SidebarCardWidth, 23);
            label.Margin = new Padding(0, 0, 0, 0);
            return label;
        }

        private Label NewLabel(string text, float size, bool bold, Color color)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.Text = text;
            label.Font = UiTheme.Font(size, bold);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private UiNumeric NewNumeric(int x, int y, int width, decimal min,
            decimal max, decimal increment, decimal value, int decimals)
        {
            UiNumeric input = new UiNumeric(min, max, increment, value, decimals);
            input.Location = new Point(x, y);
            input.Size = new Size(width, 34);
            input.Font = UiTheme.Mono(9.4f, true);
            input.Dock = DockStyle.Fill;
            return input;
        }

        private void WireRuntimeEvents()
        {
            recorder.EventRecorded += RecorderEventRecorded;
            recorder.HotkeyPressed += RecorderHotkeyPressed;
            recorder.Error += RecorderError;

            player.ProgressChanged += PlayerProgressChanged;
            player.PlaybackCompleted += PlayerPlaybackCompleted;
            player.PlaybackFailed += PlayerPlaybackFailed;

        }

        private void DisposeRuntimeEvents()
        {
            if (recorder != null)
            {
                recorder.EventRecorded -= RecorderEventRecorded;
                recorder.HotkeyPressed -= RecorderHotkeyPressed;
                recorder.Error -= RecorderError;
                recorder.Dispose();
                recorder = null;
            }
            if (player != null)
            {
                player.ProgressChanged -= PlayerProgressChanged;
                player.PlaybackCompleted -= PlayerPlaybackCompleted;
                player.PlaybackFailed -= PlayerPlaybackFailed;
                player.Dispose();
                player = null;
            }
            if (screenRecorder != null)
            {
                screenRecorder.RecordingCompleted -= ScreenRecordingCompleted;
                screenRecorder.Error -= ScreenRecordingError;
                screenRecorder.Dispose();
                screenRecorder = null;
            }
            if (strategyEngine != null)
            {
                strategyEngine.StatusChanged -= StrategyStatusChanged;
                strategyEngine.RuleTriggered -= StrategyRuleTriggered;
                strategyEngine.Error -= StrategyError;
                strategyEngine.Dispose();
                strategyEngine = null;
            }
            if (combatDemoTrainer != null)
            {
                combatDemoTrainer.Dispose();
                combatDemoTrainer = null;
            }
            if (combatAgent != null)
            {
                combatAgent.Dispose();
                combatAgent = null;
            }
            if (mainToolTip != null)
            {
                mainToolTip.Dispose();
                mainToolTip = null;
            }
        }

        private void StartNewProject(bool confirm)
        {
            if (runMode != RunMode.Idle && runMode != RunMode.Error)
            {
                ShowToast("请先停止当前录制或回放。", ToastKind.Warning);
                return;
            }

            if (confirm && !ConfirmDiscardChanges())
            {
                return;
            }

            TestProject fresh = new TestProject();
            fresh.Name = "测试 " + DateTime.Now.ToString("MM-dd HH.mm");
            Rectangle bounds = SystemInformation.VirtualScreen;
            fresh.ScreenLeft = bounds.Left;
            fresh.ScreenTop = bounds.Top;
            fresh.ScreenWidth = bounds.Width;
            fresh.ScreenHeight = bounds.Height;
            project = fresh;
            currentPath = null;
            dirty = false;
            undoBuffer.Clear();
            selectedEventIndex = -1;
            SetRunMode(RunMode.Idle);
            RefreshProjectView();
        }

        private void NewProject()
        {
            if (!AcceptsIdleActions())
            {
                return;
            }

            if (project != null && project.Events.Count > 0 || dirty)
            {
                DialogResult answer = MessageBox.Show(
                    "创建新项目？当前测试会被关闭。",
                    "新建测试",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }
            StartNewProject(false);
            ShowToast("已创建新的测试项目。", ToastKind.Info);
        }

        private void OpenProject()
        {
            if (!AcceptsIdleActions())
            {
                ShowToast("录制或回放期间不能打开项目。", ToastKind.Warning);
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "打开 InputLab 测试项目";
                dialog.Filter = "InputLab 测试 (*.gitproj.json)|*.gitproj.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                dialog.RestoreDirectory = true;
                if (projectStore != null)
                {
                    string defaultDirectory = GetRecordingsDirectory();
                    if (Directory.Exists(defaultDirectory))
                    {
                        dialog.InitialDirectory = defaultDirectory;
                    }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    LoadProject(dialog.FileName);
                }
            }
        }

        private void LoadProject(string path)
        {
            if (!AcceptsIdleActions())
            {
                ShowToast("请先停止当前操作。", ToastKind.Warning);
                return;
            }
            if (!ConfirmDiscardChanges())
            {
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                TestProject loaded = projectStore.Load(path);
                loaded.Events = loaded.Events ?? new List<RecordedEvent>();
                project = loaded;
                currentPath = path;
                dirty = false;
                undoBuffer.Clear();
                selectedEventIndex = -1;
                SetRunMode(RunMode.Idle);
                RefreshProjectView();
                ShowToast("已打开 " + Path.GetFileName(path), ToastKind.Success);
            }
            catch (Exception exception)
            {
                ShowToast("无法打开项目：" + exception.Message, ToastKind.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private bool SaveProject(bool forceDialog)
        {
            if (project == null || projectStore == null)
            {
                return false;
            }
            if (runMode != RunMode.Idle && runMode != RunMode.Error)
            {
                ShowToast("录制或回放期间不能保存。", ToastKind.Warning);
                return false;
            }

            string destination = currentPath;
            if (forceDialog || string.IsNullOrEmpty(destination))
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存 InputLab 测试项目";
                    dialog.Filter = "InputLab 测试 (*.gitproj.json)|*.gitproj.json|JSON 文件 (*.json)|*.json";
                    dialog.AddExtension = true;
                    dialog.DefaultExt = "gitproj.json";
                    dialog.FileName = MakeSafeFileName(project.Name) + ".gitproj.json";
                    dialog.InitialDirectory = GetRecordingsDirectory();
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return false;
                    }
                    destination = dialog.FileName;
                }
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                project.Touch();
                projectStore.Save(destination, project);
                currentPath = destination;
                dirty = false;
                UpdateProjectHeader();
                ShowToast("测试已保存。", ToastKind.Success);
                return true;
            }
            catch (Exception exception)
            {
                ShowToast("保存失败：" + exception.Message, ToastKind.Error);
                return false;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void AutoSaveRecording()
        {
            if (projectStore == null || project == null)
            {
                return;
            }

            try
            {
                string directory = GetRecordingsDirectory();
                Directory.CreateDirectory(directory);
                string baseName = MakeSafeFileName(project.Name);
                string fileName = baseName + "_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".gitproj.json";
                string path = Path.Combine(directory, fileName);
                int suffix = 2;
                while (File.Exists(path))
                {
                    path = Path.Combine(directory, baseName + "_" +
                        DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" +
                        suffix + ".gitproj.json");
                    suffix++;
                }

                project.Touch();
                projectStore.Save(path, project);
                currentPath = path;
                dirty = false;
                UpdateProjectHeader();
            }
            catch (Exception exception)
            {
                ShowToast("录制已保留，但自动保存失败：" + exception.Message, ToastKind.Warning);
            }
        }

        private bool ConfirmDiscardChanges()
        {
            if (!dirty || project == null || project.Events.Count == 0)
            {
                return true;
            }

            DialogResult result = MessageBox.Show(
                "当前测试有未保存的修改，先保存吗？",
                "未保存的修改",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (result == DialogResult.Cancel)
            {
                return false;
            }
            if (result == DialogResult.Yes)
            {
                return SaveProject(false);
            }
            return true;
        }

        private void BeginRecording()
        {
            if (runMode == RunMode.Recording)
            {
                StopRecording();
                return;
            }
            if (!AcceptsIdleActions() || recorder == null)
            {
                return;
            }

            if (project != null && project.Events.Count > 0)
            {
                if (!ConfirmDiscardChanges())
                {
                    return;
                }
                DialogResult replace = MessageBox.Show(
                    "开始新录制会替换当前时间线中的全部事件。是否继续？",
                    "准备录制",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (replace != DialogResult.Yes)
                {
                    return;
                }
            }

            BeginPreparation(RunMode.PreparingRecording);
        }

        private void BeginPlayback()
        {
            if (runMode == RunMode.Playing)
            {
                StopPlayback();
                return;
            }
            if (!AcceptsIdleActions() || player == null)
            {
                return;
            }
            if (project == null || project.Events.Count == 0)
            {
                ShowToast("时间线为空，请先录制一轮操作。", ToastKind.Warning);
                return;
            }

            bool hasRelativeCameraData = HasRelativeCameraData(project.Events);
            CameraPlaybackMode selectedMode = GetSelectedCameraMode();
            bool willUseRelativeCamera = selectedMode == CameraPlaybackMode.Relative ||
                (selectedMode == CameraPlaybackMode.Auto && hasRelativeCameraData);
            Rectangle current = SystemInformation.VirtualScreen;
            bool sameDesktop = current.Left == project.ScreenLeft &&
                current.Top == project.ScreenTop &&
                current.Width == project.ScreenWidth &&
                current.Height == project.ScreenHeight;
            if (!sameDesktop && !willUseRelativeCamera)
            {
                DialogResult answer = MessageBox.Show(
                    "桌面布局与录制时不同。\n\n" +
                    "录制：" + project.ScreenWidth + " × " + project.ScreenHeight + "\n" +
                    "当前：" + current.Width + " × " + current.Height + "\n\n" +
                    "鼠标坐标可能偏移，仍要继续吗？",
                    "桌面分辨率已变化",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            BeginPreparation(RunMode.PreparingPlayback);
        }

        private void BeginPreparation(RunMode targetMode)
        {
            prepareTimer.Stop();
            stopButton.Enabled = true;
            recordButton.Enabled = false;
            replayButton.Enabled = false;
            playButton.Enabled = false;
            SetRunMode(targetMode);

            int seconds = (int)countdownInput.Value;
            prepareSecondsLeft = seconds;
            if (seconds <= 0)
            {
                WindowState = FormWindowState.Minimized;
                if (targetMode == RunMode.PreparingRecording)
                {
                    StartActualRecording();
                }
                else if (targetMode == RunMode.PreparingStrategy)
                {
                    StartActualStrategy();
                }
                else if (targetMode == RunMode.PreparingCombatTraining)
                {
                    StartCombatTraining();
                }
                else if (targetMode == RunMode.PreparingCombatAgent)
                {
                    StartCombatAgent();
                }
                else
                {
                    StartActualPlayback();
                }
                return;
            }

            string action = targetMode == RunMode.PreparingRecording
                ? "录制"
                : (targetMode == RunMode.PreparingStrategy
                    ? "智能测试"
                    : (targetMode == RunMode.PreparingCombatTraining
                        ? "战斗示范"
                        : (targetMode == RunMode.PreparingCombatAgent ? "战斗代理" : "回放")));
            statusTitle.Text = seconds + " 秒后开始" + action;
            statusDetail.Text = "请切换到游戏窗口并保持目标窗口在前台";
            globalProgressLabel.Text = seconds + " 秒后开始；按 F12 可紧急停止";
            prepareTimer.Start();
        }

        private void PrepareTimerTick(object sender, EventArgs e)
        {
            prepareSecondsLeft--;
            if (prepareSecondsLeft > 0)
            {
                string action = runMode == RunMode.PreparingRecording
                    ? "录制"
                    : (runMode == RunMode.PreparingCombatTraining
                        ? "战斗示范"
                        : (runMode == RunMode.PreparingCombatAgent
                            ? "战斗代理"
                            : (runMode == RunMode.PreparingStrategy ? "智能测试" : "回放")));
                statusTitle.Text = prepareSecondsLeft + " 秒后开始" + action;
                globalProgressLabel.Text = prepareSecondsLeft + " 秒后开始；请切换至游戏窗口";
                return;
            }

            prepareTimer.Stop();
            WindowState = FormWindowState.Minimized;
            if (runMode == RunMode.PreparingRecording)
            {
                StartActualRecording();
            }
            else if (runMode == RunMode.PreparingPlayback)
            {
                StartActualPlayback();
            }
            else if (runMode == RunMode.PreparingStrategy)
            {
                StartActualStrategy();
            }
            else if (runMode == RunMode.PreparingCombatTraining)
            {
                StartCombatTraining();
            }
            else if (runMode == RunMode.PreparingCombatAgent)
            {
                StartCombatAgent();
            }
        }

        private void StartActualRecording()
        {
            if (recorder == null)
            {
                RestoreMainWindow();
                return;
            }

            lock (captureLock)
            {
                captureBuffer.Clear();
            }

            try
            {
                SetRunMode(RunMode.Recording);
                recordingLimitNotified = false;
                statusTitle.Text = "正在记录操作";
                statusDetail.Text = "完成游玩后按 F8 停止并返回";
                globalProgressLabel.Text = "录制中 · F8 停止 · F12 紧急停止";
                project.ScreenLeft = SystemInformation.VirtualScreen.Left;
                project.ScreenTop = SystemInformation.VirtualScreen.Top;
                project.ScreenWidth = SystemInformation.VirtualScreen.Width;
                project.ScreenHeight = SystemInformation.VirtualScreen.Height;
                project.CreatedAt = DateTime.UtcNow.ToString("o");
                recorder.StartRecording();
            }
            catch (Exception exception)
            {
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                statusTitle.Text = "无法开始录制";
                statusDetail.Text = exception.Message;
                UiToast.Show(this, "无法开始录制：" + exception.Message, ToastKind.Error);
            }
        }

        private void StopRecording()
        {
            if (runMode != RunMode.Recording || recorder == null)
            {
                return;
            }

            try
            {
                recorder.StopRecording();
            }
            catch (Exception exception)
            {
                ShowToast("停止录制时出现问题：" + exception.Message, ToastKind.Warning);
            }

            List<RecordedEvent> captured;
            lock (captureLock)
            {
                captured = new List<RecordedEvent>(captureBuffer);
                captureBuffer.Clear();
            }

            captured.Sort(delegate(RecordedEvent first, RecordedEvent second)
            {
                return first.TimeMs.CompareTo(second.TimeMs);
            });

            project.Events = captured;
            project.Touch();
            dirty = true;
            selectedEventIndex = -1;
            undoBuffer.Clear();
            SetRunMode(RunMode.Idle);
            RefreshProjectView();
            RestoreMainWindow();
            AutoSaveRecording();
            int cameraSamples = 0;
            for (int i = 0; i < captured.Count; i++)
            {
                if (captured[i].Kind == RecordedEventKind.MouseRelativeMove)
                    cameraSamples++;
            }
            ShowToast("录制完成：已记录 " + captured.Count.ToString("N0") + " 个事件" +
                (cameraSamples > 0
                    ? "，其中相对镜头位移 " + cameraSamples.ToString("N0") + " 组。"
                    : "。"),
                ToastKind.Success);
        }

        private CameraPlaybackMode GetSelectedCameraMode()
        {
            if (cameraModeInput == null)
                return CameraPlaybackMode.Auto;
            if (cameraModeInput.SelectedIndex == 1)
                return CameraPlaybackMode.Absolute;
            if (cameraModeInput.SelectedIndex == 2)
                return CameraPlaybackMode.Relative;
            return CameraPlaybackMode.Auto;
        }

        private static bool HasRelativeCameraData(IList<RecordedEvent> events)
        {
            return PlaybackEventSelector.HasRelativeCameraData(events);
        }

        private List<RecordedEvent> BuildPlaybackSnapshot(bool useRelativeCamera)
        {
            return PlaybackEventSelector.SelectForPlayback(project.Events, useRelativeCamera);
        }

        private void StartActualPlayback()
        {
            if (player == null || project == null)
            {
                RestoreMainWindow();
                return;
            }

            CameraPlaybackMode selectedMode = GetSelectedCameraMode();
            bool useRelativeCamera = selectedMode == CameraPlaybackMode.Relative ||
                (selectedMode == CameraPlaybackMode.Auto &&
                 HasRelativeCameraData(project.Events));
            List<RecordedEvent> snapshot = BuildPlaybackSnapshot(useRelativeCamera);
            if (snapshot.Count == 0)
            {
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                ShowToast("当前镜头模式没有可回放的有效事件。", ToastKind.Warning);
                return;
            }

            try
            {
                SetRunMode(RunMode.Playing);
                statusTitle.Text = "正在自动回放";
                statusDetail.Text = (useRelativeCamera ? "相对镜头 + 绝对点击" : "绝对坐标模式") +
                    " · 按 F9 停止";
                globalProgressLabel.Text = (useRelativeCamera
                    ? "正在发送相对镜头位移"
                    : "正在发送绝对鼠标坐标") + " · F9 停止 · F12 紧急停止";
                player.Play(snapshot, (double)speedInput.Value,
                    (int)repeatInput.Value, 0, useRelativeCamera);
            }
            catch (Exception exception)
            {
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                statusTitle.Text = "无法开始回放";
                statusDetail.Text = exception.Message;
                UiToast.Show(this, "无法开始回放：" + exception.Message, ToastKind.Error);
            }
        }

        private void ToggleScreenRecording()
        {
            if (screenRecorder == null)
            {
                ShowToast("屏幕录制器尚未初始化。", ToastKind.Error);
                return;
            }

            if (screenRecorder.IsRecording)
            {
                ShowToast("正在结束并保存录屏…", ToastKind.Info);
                screenRecorder.Stop();
                RefreshVideoButton();
                return;
            }

            try
            {
                string directory = screenRecorder.GetDefaultDirectory();
                Directory.CreateDirectory(directory);
                string baseName = "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(directory, baseName + ".avi");
                int suffix = 2;
                while (File.Exists(path))
                {
                    path = Path.Combine(directory, baseName + "_" + suffix + ".avi");
                    suffix++;
                }

                screenRecorder.Start(path, 15, 72, appSettings.RecordingMaxWidth);
                RefreshVideoButton();
                ShowToast("录屏已开始，按 F10 或再次点击按钮停止。", ToastKind.Success);
            }
            catch (Exception exception)
            {
                RefreshVideoButton();
                ShowToast("无法开始录屏：" + exception.Message, ToastKind.Error);
            }
        }

        private void RefreshVideoButton()
        {
            if (videoRecordButton == null)
                return;
            bool active = screenRecorder != null && screenRecorder.IsRecording;
            videoRecordButton.Text = active ? "■ 停止录屏" : "● 录屏 F10";
            videoRecordButton.Role = active ? ButtonRole.Danger : ButtonRole.Secondary;
            videoRecordButton.Enabled = screenRecorder != null;
            if (settingsButton != null)
                settingsButton.Enabled = !active && AcceptsIdleActions();
            if (mainToolTip != null)
            {
                string tooltip = active
                    ? "正在录制主显示器。点击或按 F10 结束并保存。"
                    : "手动录制主显示器画面（F10）。文件保存到视频\\GameInputLab。";
                if (active && !String.IsNullOrEmpty(screenRecorder.CurrentPath))
                    tooltip += "\n" + screenRecorder.CurrentPath;
                mainToolTip.SetToolTip(videoRecordButton, tooltip);
            }
            RefreshCombatButtons();
        }

        private void OpenSettings()
        {
            if (!AcceptsIdleActions() ||
                (screenRecorder != null && screenRecorder.IsRecording))
            {
                ShowToast("请先停止录制和回放，再修改设置。", ToastKind.Warning);
                return;
            }

            using (SettingsForm dialog = new SettingsForm(appSettings.Clone()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                if (!ConfirmDiscardChanges())
                    return;
                try
                {
                    new AppSettingsStore().Save(dialog.Settings);
                    Application.Restart();
                }
                catch (Exception exception)
                {
                    ShowToast("无法保存设置：" + exception.Message, ToastKind.Error);
                }
            }
        }

        private void OpenStrategyRules()
        {
            if (!AcceptsIdleActions() ||
                (screenRecorder != null && screenRecorder.IsRecording))
            {
                ShowToast("请先停止录制、回放或智能测试，再编辑规则。", ToastKind.Warning);
                return;
            }
            if (project == null)
                return;
            if (project.StrategyRules == null)
                project.StrategyRules = new List<StrategyRule>();

            using (StrategyRulesForm dialog = new StrategyRulesForm(
                project.StrategyRules,
                appSettings.InterfaceScalePercent / 100f))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                List<StrategyRule> rules = new List<StrategyRule>();
                for (int i = 0; i < dialog.Rules.Count; i++)
                {
                    if (dialog.Rules[i] != null)
                    {
                        StrategyRule copy = dialog.Rules[i].Clone();
                        copy.Normalize();
                        rules.Add(copy);
                    }
                }
                project.StrategyRules = rules;
                MarkEdited();
                RefreshStrategyButtons();
                ShowToast("已保存 " + rules.Count + " 条智能策略规则。", ToastKind.Success);
            }
        }

        private void ToggleStrategy()
        {
            if (runMode == RunMode.Strategy)
            {
                StopStrategy();
                return;
            }
            if (runMode == RunMode.PreparingStrategy)
            {
                AbortCurrentOperation(false);
                return;
            }
            if (!AcceptsIdleActions())
            {
                ShowToast("请先停止当前任务。", ToastKind.Warning);
                return;
            }
            if (project == null || project.StrategyRules == null ||
                project.StrategyRules.Count == 0)
            {
                ShowToast("请先添加至少一条智能策略规则。", ToastKind.Warning);
                OpenStrategyRules();
                return;
            }
            BeginPreparation(RunMode.PreparingStrategy);
        }

        private void StartActualStrategy()
        {
            if (strategyEngine == null || project == null ||
                project.StrategyRules == null || project.StrategyRules.Count == 0)
            {
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                ShowToast("没有可运行的智能策略规则。", ToastKind.Error);
                return;
            }

            try
            {
                strategyTriggerCount = 0;
                lastStrategyMessage = String.Empty;
                SetRunMode(RunMode.Strategy);
                strategyRunButton.Text = "■ 停止智能";
                statusTitle.Text = "智能策略运行中";
                statusDetail.Text = "实时检测画面条件 · F11 停止";
                globalProgressLabel.Text = "正在检测策略条件";
                WindowState = FormWindowState.Minimized;
                strategyEngine.Start(project.StrategyRules);
            }
            catch (Exception exception)
            {
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                statusTitle.Text = "智能策略启动失败";
                statusDetail.Text = exception.Message;
                ShowToast("无法启动智能策略：" + exception.Message, ToastKind.Error);
            }
        }

        private void StopStrategy()
        {
            if (strategyEngine != null)
                strategyEngine.Stop();
            int count = strategyTriggerCount;
            SetRunMode(RunMode.Idle);
            RestoreMainWindow();
            statusDetail.Text = "智能策略已停止";
            globalProgressLabel.Text = "本轮规则命中 " + count.ToString("N0") + " 次";
            ShowToast("智能策略已停止，共执行 " + count.ToString("N0") + " 次动作。",
                ToastKind.Info);
        }

        private void RefreshStrategyButtons()
        {
            if (strategyRulesButton == null || strategyRunButton == null)
                return;
            int count = project == null || project.StrategyRules == null
                ? 0
                : project.StrategyRules.Count;
            bool running = runMode == RunMode.Strategy;
            strategyRulesButton.Text = "策略";
            strategyRulesButton.Enabled = AcceptsIdleActions();
            strategyRunButton.Text = running ? "■ 停止智能" : "▶ 智能 F11";
            strategyRunButton.Role = running ? ButtonRole.Danger : ButtonRole.Primary;
            strategyRunButton.Enabled = running ||
                (AcceptsIdleActions() && count > 0);
            if (mainToolTip != null)
            {
                mainToolTip.SetToolTip(strategyRulesButton,
                    "编辑智能策略规则，当前 " + count.ToString("N0") + " 条");
            }
        }

        private void StrategyStatusChanged(string message)
        {
            lastStrategyMessage = message;
        }

        private void StrategyRuleTriggered(string message)
        {
            strategyTriggerCount = strategyEngine == null
                ? strategyTriggerCount + 1
                : strategyEngine.TriggerCount;
            lastStrategyMessage = message;
        }

        private void StrategyError(string message)
        {
            InvokeOnUi(delegate
            {
                if (runMode != RunMode.Strategy &&
                    runMode != RunMode.PreparingStrategy)
                {
                    return;
                }
                SetRunMode(RunMode.Error);
                RestoreMainWindow();
                statusTitle.Text = "智能策略异常";
                statusDetail.Text = message;
                ShowToast(message, ToastKind.Error);
            });
        }

        private void OpenWukongAgent()
        {
            if (!AcceptsIdleActions() ||
                (screenRecorder != null && screenRecorder.IsRecording))
            {
                ShowToast("请先停止录制、回放或战斗代理，再修改配置。", ToastKind.Warning);
                return;
            }
            if (combatProfileStore == null || combatProfile == null)
            {
                ShowToast("战斗代理配置尚未初始化。", ToastKind.Error);
                return;
            }

            using (WukongAgentForm dialog = new WukongAgentForm(
                combatProfile,
                combatProfileDirectory,
                appSettings.InterfaceScalePercent / 100f))
            {
                DialogResult result = dialog.ShowDialog(this);
                if (result != DialogResult.OK)
                {
                    combatProfile = combatProfileStore.LoadDefault();
                    RefreshCombatButtons();
                    return;
                }
                combatProfile = dialog.Profile;
                combatProfile.Normalize();
                try
                {
                    combatProfileStore.Save(combatProfileDirectory, combatProfile);
                }
                catch (Exception exception)
                {
                    ShowToast("无法保存战斗代理配置：" + exception.Message, ToastKind.Error);
                    return;
                }
                RefreshCombatButtons();
                if (dialog.StartTrainingRequested)
                    BeginPreparation(RunMode.PreparingCombatTraining);
                else if (dialog.StartAgentRequested)
                    BeginPreparation(RunMode.PreparingCombatAgent);
            }
        }

        private void ToggleCombatAgent()
        {
            if (runMode == RunMode.CombatTraining)
            {
                StopCombatTraining();
                return;
            }
            if (runMode == RunMode.CombatAgent)
            {
                StopCombatAgent();
                return;
            }
            if (runMode == RunMode.PreparingCombatTraining ||
                runMode == RunMode.PreparingCombatAgent)
            {
                AbortCurrentOperation(false);
                return;
            }
            if (!AcceptsIdleActions())
            {
                ShowToast("请先停止当前任务。", ToastKind.Warning);
                return;
            }
            if (combatProfile == null || combatProfile.Examples == null ||
                combatProfile.Examples.Count == 0)
            {
                ShowToast("请先录制至少一轮战斗示范。", ToastKind.Warning);
                OpenWukongAgent();
                return;
            }
            BeginPreparation(RunMode.PreparingCombatAgent);
        }

        private void StartCombatTraining()
        {
            if (recorder == null || combatProfile == null ||
                combatProfile.ObservationWidth < 32 ||
                combatProfile.ObservationHeight < 32)
            {
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                ShowToast("战斗代理配置不完整，请先选择观察区域。", ToastKind.Error);
                return;
            }
            try
            {
                combatTrainingExamples = 0;
                combatLastAction = String.Empty;
                combatDemoTrainer = new CombatDemoTrainer();
                combatDemoTrainer.ExampleCaptured += CombatExampleCaptured;
                combatDemoTrainer.Error += CombatTrainerError;
                SetRunMode(RunMode.CombatTraining);
                statusTitle.Text = "正在录制战斗示范";
                statusDetail.Text = "正常游玩并按映射动作键 · F8 完成";
                globalProgressLabel.Text = "示范录制中 · F8 完成 · F12 紧急停止";
                recorder.StartRecording();
                combatDemoTrainer.Start(combatProfile, combatProfileDirectory);
                WindowState = FormWindowState.Minimized;
            }
            catch (Exception exception)
            {
                if (recorder != null)
                    recorder.StopRecording();
                if (combatDemoTrainer != null)
                {
                    combatDemoTrainer.ExampleCaptured -= CombatExampleCaptured;
                    combatDemoTrainer.Error -= CombatTrainerError;
                    combatDemoTrainer.Dispose();
                    combatDemoTrainer = null;
                }
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                statusTitle.Text = "无法开始战斗示范";
                statusDetail.Text = exception.Message;
                ShowToast("无法开始战斗示范：" + exception.Message, ToastKind.Error);
            }
        }

        private void StopCombatTraining()
        {
            if (runMode != RunMode.CombatTraining)
                return;
            int learned = StopCombatTrainingResources();
            SetRunMode(RunMode.Idle);
            RestoreMainWindow();
            statusDetail.Text = "战斗示范已完成";
            globalProgressLabel.Text = "已采集 " + learned.ToString("N0") + " 个动作样本";
            ShowToast("示范录制完成，共采集 " + learned.ToString("N0") + " 个动作样本。",
                ToastKind.Success);
        }

        private int StopCombatTrainingResources()
        {
            int learned = combatDemoTrainer == null
                ? combatTrainingExamples
                : combatDemoTrainer.ExamplesCaptured;
            if (recorder != null)
            {
                try
                {
                    recorder.StopRecording();
                }
                catch
                {
                }
            }
            if (combatDemoTrainer != null)
            {
                combatDemoTrainer.ExampleCaptured -= CombatExampleCaptured;
                combatDemoTrainer.Error -= CombatTrainerError;
                combatDemoTrainer.Stop();
                combatDemoTrainer.Dispose();
                combatDemoTrainer = null;
            }
            combatTrainingExamples = learned;
            if (combatProfile != null && combatProfileStore != null)
            {
                try
                {
                    combatProfileStore.Save(combatProfileDirectory, combatProfile);
                }
                catch
                {
                }
            }
            RefreshCombatButtons();
            return learned;
        }

        private void StartCombatAgent()
        {
            if (combatProfile == null || combatProfile.Examples == null ||
                combatProfile.Examples.Count == 0)
            {
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                ShowToast("没有可用的战斗示范，请先完成示范录制。", ToastKind.Warning);
                return;
            }
            if (combatAgent != null)
            {
                combatAgent.Dispose();
                combatAgent = null;
            }

            combatActionCount = 0;
            combatLastAction = String.Empty;
            combatAgent = new AdaptiveCombatAgent();
            combatAgent.StatusChanged += CombatAgentStatusChanged;
            combatAgent.ActionTriggered += CombatAgentActionTriggered;
            combatAgent.Error += CombatAgentError;
            try
            {
                SetRunMode(RunMode.CombatAgent);
                statusTitle.Text = "黑神话战斗代理运行中";
                statusDetail.Text = "实时匹配战斗示范 · F11 停止";
                globalProgressLabel.Text = "战斗代理启动中";
                WindowState = FormWindowState.Minimized;
                combatAgent.Start(combatProfile, combatProfileDirectory);
            }
            catch (Exception exception)
            {
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                statusTitle.Text = "战斗代理启动失败";
                statusDetail.Text = exception.Message;
                ShowToast("无法启动战斗代理：" + exception.Message, ToastKind.Error);
            }
        }

        private void StopCombatAgent()
        {
            int actions = combatActionCount;
            if (combatAgent != null)
            {
                combatAgent.Stop();
                combatAgent.StatusChanged -= CombatAgentStatusChanged;
                combatAgent.ActionTriggered -= CombatAgentActionTriggered;
                combatAgent.Error -= CombatAgentError;
                actions = combatAgent.ActionCount;
                combatAgent.Dispose();
                combatAgent = null;
            }
            SetRunMode(RunMode.Idle);
            RestoreMainWindow();
            statusDetail.Text = "战斗代理已停止";
            globalProgressLabel.Text = "已执行 " + actions.ToString("N0") + " 次动作";
            ShowToast("战斗代理已停止，共执行 " + actions.ToString("N0") + " 次动作。",
                ToastKind.Info);
        }

        private void RefreshCombatButtons()
        {
            if (strategyRulesButton == null || strategyRunButton == null)
                return;
            int examples = combatProfile == null || combatProfile.Examples == null
                ? 0
                : combatProfile.Examples.Count;
            bool training = runMode == RunMode.CombatTraining;
            bool running = runMode == RunMode.CombatAgent;
            bool canConfigure = AcceptsIdleActions() &&
                (screenRecorder == null || !screenRecorder.IsRecording);
            strategyRulesButton.Text = "悟空代理";
            strategyRulesButton.Enabled = canConfigure;
            strategyRunButton.Text = training
                ? "■ 结束示范"
                : (running ? "■ 停止战斗" : "▶ 战斗 F11");
            strategyRunButton.Role = training || running
                ? ButtonRole.Danger
                : ButtonRole.Primary;
            strategyRunButton.Enabled = training || running ||
                (AcceptsIdleActions() && examples > 0);
            if (mainToolTip != null)
            {
                mainToolTip.SetToolTip(strategyRulesButton,
                    "黑神话悟空战斗代理：配置区域、按键和学习示范；当前 " +
                    examples.ToString("N0") + " 个样本");
            }
        }

        private void CombatExampleCaptured(CombatExample example)
        {
            if (combatDemoTrainer != null)
                combatTrainingExamples = combatDemoTrainer.ExamplesCaptured;
            if (example != null)
                combatLastAction = example.Action.ToString();
        }

        private void CombatTrainerError(string message)
        {
            InvokeOnUi(delegate
            {
                if (runMode != RunMode.CombatTraining)
                    return;
                StopCombatTrainingResources();
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                statusTitle.Text = "战斗示范录制异常";
                statusDetail.Text = message;
                ShowToast(message, ToastKind.Error);
            });
        }

        private void CombatAgentStatusChanged(string message)
        {
            combatLastAction = message;
        }

        private void CombatAgentActionTriggered(string message)
        {
            if (combatAgent != null)
                combatActionCount = combatAgent.ActionCount;
            combatLastAction = message;
        }

        private void CombatAgentError(string message)
        {
            InvokeOnUi(delegate
            {
                if (runMode != RunMode.CombatAgent)
                    return;
                if (combatAgent != null)
                {
                    combatAgent.StatusChanged -= CombatAgentStatusChanged;
                    combatAgent.ActionTriggered -= CombatAgentActionTriggered;
                    combatAgent.Error -= CombatAgentError;
                    combatAgent.Dispose();
                    combatAgent = null;
                }
                RestoreMainWindow();
                SetRunMode(RunMode.Error);
                statusTitle.Text = "战斗代理异常";
                statusDetail.Text = message;
                ShowToast(message, ToastKind.Error);
            });
        }

        private void ScreenRecordingCompleted(string path)
        {
            InvokeOnUi(delegate
            {
                RefreshVideoButton();
                ShowToast("录屏已保存：" + Path.GetFileName(path), ToastKind.Success);
            });
        }

        private void ScreenRecordingError(string message)
        {
            InvokeOnUi(delegate
            {
                RefreshVideoButton();
                ShowToast(message, ToastKind.Error);
            });
        }

        private void StopPlayback()
        {
            if (player == null)
            {
                return;
            }
            player.Stop();
            SetRunMode(RunMode.Idle);
            RestoreMainWindow();
            globalProgressLabel.Text = "回放已停止";
            ShowToast("自动回放已停止。", ToastKind.Info);
        }

        private void AbortCurrentOperation(bool closing)
        {
            prepareTimer.Stop();
            try
            {
                if (runMode == RunMode.Recording && recorder != null)
                {
                    recorder.StopRecording();
                }
                if ((runMode == RunMode.Playing || runMode == RunMode.PreparingPlayback) &&
                    player != null)
                {
                    player.Stop();
                }
                if ((runMode == RunMode.Strategy || runMode == RunMode.PreparingStrategy) &&
                    strategyEngine != null)
                {
                    strategyEngine.Stop();
                }
                if (runMode == RunMode.CombatTraining && combatDemoTrainer != null)
                {
                    StopCombatTrainingResources();
                }
                if (runMode == RunMode.CombatAgent && combatAgent != null)
                {
                    combatAgent.Stop();
                    combatAgent.StatusChanged -= CombatAgentStatusChanged;
                    combatAgent.ActionTriggered -= CombatAgentActionTriggered;
                    combatAgent.Error -= CombatAgentError;
                    combatAgent.Dispose();
                    combatAgent = null;
                }
            }
            catch
            {
                // Emergency stop must continue restoring the UI even if a driver call fails.
            }

            if (runMode == RunMode.Recording)
            {
                List<RecordedEvent> partial;
                lock (captureLock)
                {
                    partial = new List<RecordedEvent>(captureBuffer);
                    captureBuffer.Clear();
                }
                partial.Sort(delegate(RecordedEvent a, RecordedEvent b)
                {
                    return a.TimeMs.CompareTo(b.TimeMs);
                });
                if (partial.Count > 0)
                {
                    project.Events = partial;
                    dirty = true;
                }
            }

            SetRunMode(RunMode.Idle);
            if (!closing)
            {
                RestoreMainWindow();
                RefreshProjectView();
                ShowToast("已紧急停止。", ToastKind.Warning);
            }
        }

        private void RestoreMainWindow()
        {
            if (restoringWindow || IsDisposed)
            {
                return;
            }
            restoringWindow = true;
            try
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    Show();
                    WindowState = FormWindowState.Normal;
                }
                ShowInTaskbar = true;
                Activate();
                BringToFront();
            }
            finally
            {
                restoringWindow = false;
            }
        }

        private void RecorderEventRecorded(RecordedEvent item)
        {
            if (item == null)
            {
                return;
            }
            RecordedEvent copy = item.Clone();
            if (runMode == RunMode.CombatTraining && combatDemoTrainer != null)
            {
                combatDemoTrainer.FeedInput(copy);
                return;
            }
            bool limitReached = false;
            lock (captureLock)
            {
                if (captureBuffer.Count < ProjectStore.MaximumEventCount)
                    captureBuffer.Add(copy);
                else
                    limitReached = true;
            }
            if (limitReached && !recordingLimitNotified)
            {
                recordingLimitNotified = true;
                if (IsHandleCreated && !IsDisposed)
                {
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            statusTitle.Text = "已达到安全上限";
                            statusDetail.Text = "已停止追加事件，正在保存当前录制";
                            ShowToast("已达到 250,000 个事件上限，录制自动停止。",
                                ToastKind.Warning);
                            StopRecording();
                        });
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
        }

        private void RecorderHotkeyPressed(Keys key)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)delegate { HandleGlobalHotkey(key); });
            }
            catch (InvalidOperationException)
            {
                // The form is closing.
            }
        }

        private void RecorderError(string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)delegate
                {
                    ShowToast("输入引擎：" + message, ToastKind.Error);
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void HandleGlobalHotkey(Keys key)
        {
            if (key == Keys.F11)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - lastStrategyHotkeyUtc).TotalMilliseconds >= 400)
                {
                    lastStrategyHotkeyUtc = now;
                    ToggleCombatAgent();
                }
                return;
            }

            if (key == Keys.F10)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - lastScreenRecordHotkeyUtc).TotalMilliseconds >= 400)
                {
                    lastScreenRecordHotkeyUtc = now;
                    ToggleScreenRecording();
                }
                return;
            }

            if (key == Keys.F12)
            {
                if (runMode != RunMode.Idle)
                {
                    AbortCurrentOperation(false);
                }
                return;
            }

            if (key == Keys.F8)
            {
                if (runMode == RunMode.CombatTraining)
                {
                    StopCombatTraining();
                }
                else if (runMode == RunMode.Recording)
                {
                    StopRecording();
                }
                else if (AcceptsIdleActions())
                {
                    BeginRecording();
                }
                else if (runMode == RunMode.PreparingRecording)
                {
                    AbortCurrentOperation(false);
                }
                return;
            }

            if (key == Keys.F9)
            {
                if (runMode == RunMode.Playing)
                {
                    StopPlayback();
                }
                else if (AcceptsIdleActions())
                {
                    BeginPlayback();
                }
                else if (runMode == RunMode.PreparingPlayback)
                {
                    AbortCurrentOperation(false);
                }
            }
        }

        private void PlayerProgressChanged(int percent)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)delegate
                {
                    if (runMode != RunMode.Playing)
                    {
                        return;
                    }
                    int clamped = Math.Max(0, Math.Min(100, percent));
                    globalProgressLabel.Text = "自动输入 " + clamped + "% · F9 停止";
                    statusDetail.Text = "已执行 " + clamped + "% · 请勿操作鼠标键盘";
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void PlayerPlaybackCompleted(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)delegate
                {
                    if (runMode != RunMode.Playing)
                    {
                        return;
                    }
                    SetRunMode(RunMode.Idle);
                    RestoreMainWindow();
                    globalProgressLabel.Text = "回放完成";
                    ShowToast("自动测试完成。", ToastKind.Success);
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void PlayerPlaybackFailed(string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)delegate
                {
                    SetRunMode(RunMode.Error);
                    RestoreMainWindow();
                    statusTitle.Text = "回放失败";
                    statusDetail.Text = message;
                    ShowToast("自动回放失败：" + message, ToastKind.Error);
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void StatusTimerTick(object sender, EventArgs e)
        {
            if (screenRecorder != null && screenRecorder.IsRecording && mainToolTip != null)
            {
                TimeSpan elapsed = screenRecorder.Elapsed;
                mainToolTip.SetToolTip(videoRecordButton,
                    "正在录制主显示器 " + elapsed.ToString(@"mm\:ss") +
                    "。点击或按 F10 结束并保存。\n" + screenRecorder.CurrentPath);
            }

            if (runMode == RunMode.Strategy && strategyEngine != null)
            {
                TimeSpan elapsed = strategyEngine.Elapsed;
                statusTitle.Text = "智能策略运行中";
                statusDetail.Text = "已触发 " + strategyTriggerCount.ToString("N0") +
                    " 次 · " + elapsed.ToString(@"mm\:ss") + " · F11 停止";
                globalProgressLabel.Text = String.IsNullOrEmpty(lastStrategyMessage)
                    ? "正在实时检测条件"
                    : lastStrategyMessage;
                return;
            }

            if (runMode == RunMode.CombatTraining)
            {
                statusTitle.Text = "正在录制战斗示范";
                statusDetail.Text = "已采集 " + combatTrainingExamples.ToString("N0") +
                    " 个动作样本 · F8 完成";
                globalProgressLabel.Text = "正常游玩，按映射动作键采集示范";
                return;
            }

            if (runMode == RunMode.CombatAgent && combatAgent != null)
            {
                statusTitle.Text = "黑神话战斗代理运行中";
                statusDetail.Text = "已执行 " + combatActionCount.ToString("N0") +
                    " 次动作 · F11 停止";
                globalProgressLabel.Text = String.IsNullOrEmpty(combatLastAction)
                    ? "正在匹配战斗示范"
                    : combatLastAction;
                return;
            }

            if (runMode != RunMode.Recording)
            {
                return;
            }

            int count;
            lock (captureLock)
            {
                count = captureBuffer.Count;
            }
            statusDetail.Text = "已捕获 " + count.ToString("N0") + " 个事件 · F8 停止";
        }

        private bool AcceptsIdleActions()
        {
            return runMode == RunMode.Idle || runMode == RunMode.Error;
        }

        private void SetRunMode(RunMode mode)
        {
            runMode = mode;
            RunState badgeState;
            switch (mode)
            {
                case RunMode.PreparingRecording:
                case RunMode.PreparingPlayback:
                case RunMode.PreparingCombatTraining:
                case RunMode.PreparingCombatAgent:
                    badgeState = RunState.Preparing;
                    break;
                case RunMode.Recording:
                    badgeState = RunState.Recording;
                    break;
                case RunMode.Playing:
                case RunMode.Strategy:
                case RunMode.CombatTraining:
                case RunMode.CombatAgent:
                    badgeState = RunState.Playing;
                    break;
                case RunMode.PreparingStrategy:
                    badgeState = RunState.Preparing;
                    break;
                case RunMode.Error:
                    badgeState = RunState.Error;
                    break;
                default:
                    badgeState = RunState.Idle;
                    statusTitle.Text = "操作就绪";
                    statusDetail.Text = "录制一次完整游玩并自动回放";
                    globalProgressLabel.Text = "准备就绪";
                    break;
            }
            stateBadge.State = badgeState;

            bool busy = mode != RunMode.Idle && mode != RunMode.Error;
            bool interactive = mode == RunMode.Idle || mode == RunMode.Error;
            recordButton.Enabled = interactive;
            replayButton.Enabled = interactive && project != null &&
                project.Events.Count > 0;
            playButton.Enabled = interactive && project != null &&
                project.Events.Count > 0;
            stopButton.Enabled = busy;
            openButton.Enabled = interactive;
            saveButton.Enabled = interactive;
            newButton.Enabled = interactive;
            deleteButton.Enabled = interactive &&
                eventGrid != null && eventGrid.SelectedRows.Count > 0;
            projectNameBox.ReadOnly = !interactive;
            speedInput.Enabled = interactive;
            repeatInput.Enabled = interactive;
            countdownInput.Enabled = interactive;
            if (cameraModeInput != null)
                cameraModeInput.Enabled = interactive;
            RefreshVideoButton();
            RefreshCombatButtons();

            if (mode == RunMode.Recording)
            {
                recordButton.Text = "■  停止录制";
            }
            else
            {
                recordButton.Text = "●  开始录制";
            }
        }

        private void RefreshProjectView()
        {
            if (eventGrid == null || project == null)
            {
                return;
            }

            loadingProject = true;
            projectNameBox.Text = project.Name ?? "未命名测试";
            loadingProject = false;
            UpdateProjectHeader();
            eventGrid.RowCount = project.Events.Count;
            eventGrid.ClearSelection();
            eventGrid.Invalidate();
            if (project.Events.Count > 0)
            {
                eventGrid.CurrentCell = eventGrid.Rows[0].Cells[0];
                eventGrid.Rows[0].Selected = true;
            }

            preview.SetEvents(project.Events, project.ScreenWidth,
                project.ScreenHeight, project.ScreenLeft, project.ScreenTop);
            UpdateStatistics();
            UpdateSelectionDetails();
            SetRunMode(runMode);
        }

        private void UpdateProjectHeader()
        {
            if (projectPathLabel == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(currentPath))
            {
                projectPathLabel.Text = "尚未保存 · 录制完成后自动保存";
                projectPathLabel.ForeColor = UiTheme.TextFaint;
            }
            else
            {
                projectPathLabel.Text = currentPath;
                projectPathLabel.ForeColor = dirty ? UiTheme.Warning : UiTheme.TextFaint;
            }
        }

        private void UpdateStatistics()
        {
            if (project == null || project.Events == null)
            {
                return;
            }
            int inputCount = 0;
            int keyCount = 0;
            for (int i = 0; i < project.Events.Count; i++)
            {
                RecordedEvent item = project.Events[i];
                if (item == null) continue;
                if (item.Kind == RecordedEventKind.MouseButtonDown ||
                    item.Kind == RecordedEventKind.MouseWheel)
                {
                    inputCount++;
                }
                if (item.Kind == RecordedEventKind.KeyDown)
                {
                    inputCount++;
                    keyCount++;
                }
            }

            eventCountValue.Text = project.Events.Count.ToString("N0");
            clickCountValue.Text = inputCount.ToString("N0");
            keyCountValue.Text = keyCount.ToString("N0");
            durationValue.Text = FormatDuration(project.GetDurationMs());
            timelineCountLabel.Text = project.Events.Count.ToString("N0") +
                " 个事件 · " + durationValue.Text;
            replayButton.Enabled = AcceptsIdleActions() &&
                project.Events.Count > 0;
            playButton.Enabled = AcceptsIdleActions() &&
                project.Events.Count > 0;
        }

        private void EventGridCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (project == null || e.RowIndex < 0 || e.RowIndex >= project.Events.Count)
            {
                e.Value = string.Empty;
                return;
            }
            RecordedEvent item = project.Events[e.RowIndex];
            if (item == null)
            {
                e.Value = string.Empty;
                return;
            }

            switch (e.ColumnIndex)
            {
                case 0:
                    e.Value = (e.RowIndex + 1).ToString();
                    break;
                case 1:
                    e.Value = FormatTimestamp(item.TimeMs);
                    break;
                case 2:
                    e.Value = item.GetKindLabel();
                    break;
                case 3:
                    e.Value = item.GetSummary();
                    break;
                default:
                    e.Value = string.Empty;
                    break;
            }
        }

        private void EventGridPaint(object sender, PaintEventArgs e)
        {
            if (project == null || project.Events.Count != 0)
                return;

            Rectangle body = new Rectangle(
                1, eventGrid.ColumnHeadersHeight,
                Math.Max(1, eventGrid.ClientSize.Width - 2),
                Math.Max(1, eventGrid.ClientSize.Height - eventGrid.ColumnHeadersHeight - 1));
            using (SolidBrush background = new SolidBrush(UiTheme.Surface))
                e.Graphics.FillRectangle(background, body);

            using (Font titleFont = UiTheme.Font(11.2f, true))
            using (Font detailFont = UiTheme.Font(9.3f, false))
            {
                int titleHeight = 34;
                Rectangle titleRect = new Rectangle(
                    body.Left + 20, body.Top + body.Height / 2 - 38,
                    Math.Max(1, body.Width - 40), titleHeight);
                Rectangle detailRect = new Rectangle(
                    body.Left + 20, titleRect.Bottom - 2,
                    Math.Max(1, body.Width - 40), 34);
                TextRenderer.DrawText(e.Graphics, "还没有操作记录", titleFont, titleRect,
                    UiTheme.Text, TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics,
                    "点击“开始录制”记录第一步操作",
                    detailFont, detailRect, UiTheme.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }

        private void EventGridSelectionChanged(object sender, EventArgs e)
        {
            if (eventGrid.Rows.Count == 0 || eventGrid.SelectedRows.Count == 0)
            {
                selectedEventIndex = -1;
                deleteButton.Enabled = false;
            }
            else
            {
                selectedEventIndex = eventGrid.SelectedRows[0].Index;
                deleteButton.Enabled = AcceptsIdleActions();
            }
            preview.SelectEventIndex(selectedEventIndex);
            UpdateSelectionDetails();
        }

        private void UpdateSelectionDetails()
        {
            if (project == null || selectedEventIndex < 0 ||
                selectedEventIndex >= project.Events.Count)
            {
                selectedKindLabel.Text = "尚未选择事件";
                selectedSummaryLabel.Text = "在左侧时间线中选择一行";
                selectedTimeLabel.Text = "—";
                return;
            }

            RecordedEvent item = project.Events[selectedEventIndex];
            if (item == null)
            {
                return;
            }
            selectedKindLabel.Text = item.GetKindLabel();
            selectedSummaryLabel.Text = item.GetSummary();
            selectedTimeLabel.Text = "偏移 " + FormatTimestamp(item.TimeMs) +
                "  ·  事件 #" + (selectedEventIndex + 1);
        }

        private void EventGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && project != null && e.RowIndex < project.Events.Count)
            {
                RecordedEvent item = project.Events[e.RowIndex];
                CopyText(FormatTimestamp(item.TimeMs) + "  " +
                    item.GetKindLabel() + "  " + item.GetSummary());
                ShowToast("事件摘要已复制。", ToastKind.Info);
            }
        }

        private void EventGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && AcceptsIdleActions())
            {
                DeleteSelectedEvents();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedEvents(sender, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void EventGridCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                eventGrid.ClearSelection();
                eventGrid.Rows[e.RowIndex].Selected = true;
                eventGrid.CurrentCell = eventGrid.Rows[e.RowIndex].Cells[0];
                eventMenu.Show(eventGrid, e.Location);
            }
        }

        private void DeleteSelectedEvents()
        {
            if (!AcceptsIdleActions() || eventGrid.SelectedRows.Count == 0)
            {
                return;
            }

            List<int> indexes = new List<int>();
            for (int i = 0; i < eventGrid.SelectedRows.Count; i++)
            {
                indexes.Add(eventGrid.SelectedRows[i].Index);
            }
            indexes.Sort();

            undoBuffer.Clear();
            for (int i = 0; i < indexes.Count; i++)
            {
                int index = indexes[i];
                if (index >= 0 && index < project.Events.Count)
                {
                    undoBuffer.Add(project.Events[index].Clone());
                }
            }
            for (int i = indexes.Count - 1; i >= 0; i--)
            {
                int index = indexes[i];
                if (index >= 0 && index < project.Events.Count)
                {
                    project.Events.RemoveAt(index);
                }
            }
            MarkEdited();
            RefreshProjectView();
            undoButton.Enabled = undoBuffer.Count > 0;
            ShowToast("已删除 " + indexes.Count + " 个事件。", ToastKind.Info);
        }

        private void ClearTimeline()
        {
            if (!AcceptsIdleActions() || project.Events.Count == 0)
            {
                return;
            }
            DialogResult result = MessageBox.Show(
                "清空当前时间线的全部事件？",
                "清空时间线",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                return;
            }

            undoBuffer.Clear();
            for (int i = 0; i < project.Events.Count; i++)
            {
                if (project.Events[i] != null)
                {
                    undoBuffer.Add(project.Events[i].Clone());
                }
            }
            project.Events.Clear();
            MarkEdited();
            RefreshProjectView();
            undoButton.Enabled = undoBuffer.Count > 0;
        }

        private void UndoClicked(object sender, EventArgs e)
        {
            if (!AcceptsIdleActions() || undoBuffer.Count == 0)
            {
                return;
            }
            for (int i = 0; i < undoBuffer.Count; i++)
            {
                project.Events.Add(undoBuffer[i].Clone());
            }
            project.Events.Sort(delegate(RecordedEvent first, RecordedEvent second)
            {
                return first.TimeMs.CompareTo(second.TimeMs);
            });
            undoBuffer.Clear();
            undoButton.Enabled = false;
            MarkEdited();
            RefreshProjectView();
            ShowToast("已恢复上一次删除的事件。", ToastKind.Success);
        }

        private void CopySelectedEvents(object sender, EventArgs e)
        {
            if (eventGrid == null || eventGrid.SelectedRows.Count == 0)
            {
                return;
            }
            List<string> lines = new List<string>();
            for (int i = 0; i < eventGrid.SelectedRows.Count; i++)
            {
                int index = eventGrid.SelectedRows[i].Index;
                if (index < 0 || index >= project.Events.Count) continue;
                RecordedEvent item = project.Events[index];
                lines.Add((index + 1) + "\t" + FormatTimestamp(item.TimeMs) + "\t" +
                    item.GetKindLabel() + "\t" + item.GetSummary());
            }
            CopyText(string.Join(Environment.NewLine, lines.ToArray()));
            ShowToast("已复制 " + lines.Count + " 条事件。", ToastKind.Info);
        }

        private void CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
            }
            catch
            {
                ShowToast("剪贴板当前不可用。", ToastKind.Warning);
            }
        }

        private void MarkEdited()
        {
            dirty = true;
            project.Touch();
            UpdateProjectHeader();
            UpdateStatistics();
        }

        private void ProjectNameChanged(object sender, EventArgs e)
        {
            if (loadingProject || project == null)
            {
                return;
            }
            string value = projectNameBox.Text.Trim();
            if (value.Length == 0)
            {
                return;
            }
            project.Name = value;
            MarkEdited();
        }

        private void ShowToast(string message, ToastKind kind)
        {
            if (!IsDisposed)
            {
                UiToast.Show(this, message, kind);
            }
        }

        private string GetRecordingsDirectory()
        {
            if (projectStore != null)
            {
                return projectStore.GetDefaultRecordingsDirectory();
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GameInputLab", "Recordings");
        }

        private static string MakeSafeFileName(string value)
        {
            string name = string.IsNullOrEmpty(value) ? "未命名测试" : value.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                name = name.Replace(invalid[i], '_');
            }
            name = name.TrimEnd('.', ' ');
            if (name.Length > 72)
            {
                name = name.Substring(0, 72);
            }
            return string.IsNullOrEmpty(name) ? "未命名测试" : name;
        }

        private static string FormatTimestamp(long timeMs)
        {
            if (timeMs < 0) timeMs = 0;
            TimeSpan time = TimeSpan.FromMilliseconds(timeMs);
            return string.Format("{0:00}:{1:00}:{2:00}.{3:000}",
                (int)time.TotalHours, time.Minutes, time.Seconds, time.Milliseconds);
        }

        private static string FormatDuration(long timeMs)
        {
            if (timeMs < 0) timeMs = 0;
            TimeSpan time = TimeSpan.FromMilliseconds(timeMs);
            if (time.TotalHours >= 1)
            {
                return string.Format("{0:00}:{1:00}:{2:00}",
                    (int)time.TotalHours, time.Minutes, time.Seconds);
            }
            return string.Format("{0:00}:{1:00}.{2:0}",
                time.Minutes, time.Seconds, time.Milliseconds / 100);
        }

        private void InvokeOnUi(Action action)
        {
            if (action == null || IsDisposed || Disposing || !IsHandleCreated)
                return;
            try
            {
                if (InvokeRequired)
                    BeginInvoke(action);
                else
                    action();
            }
            catch (InvalidOperationException)
            {
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveProject(false);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.S))
            {
                SaveProject(true);
                return true;
            }
            if (keyData == (Keys.Control | Keys.O))
            {
                OpenProject();
                return true;
            }
            if (keyData == (Keys.Control | Keys.N))
            {
                NewProject();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private sealed class BufferedGrid : DataGridView
        {
            public BufferedGrid()
            {
                DoubleBuffered = true;
            }
        }
    }
}
