using System;
using System.Drawing;
using System.Windows.Forms;
using GameInputLab.Core;
using GameInputLab.UI;

namespace GameInputLab
{
    internal sealed class SettingsForm : Form
    {
        private const int DesignWidth = 760;
        private const int DesignHeight = 760;

        private readonly AppSettings _source;
        private ComboBox _windowModeInput;
        private UiNumeric _windowWidthInput;
        private UiNumeric _windowHeightInput;
        private UiNumeric _interfaceScaleInput;
        private UiNumeric _fontScaleInput;
        private ComboBox _recordingResolutionInput;
        private Button _backgroundButton;
        private Button _accentButton;
        private Color _backgroundColor;
        private Color _accentColor;

        public AppSettings Settings { get; private set; }

        public SettingsForm(AppSettings settings)
        {
            _source = settings == null ? new AppSettings() : settings.Clone();
            _source.Normalize();
            Settings = _source.Clone();

            Text = "界面与录屏设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(DesignWidth, DesignHeight);
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            Font = UiTheme.Font(9f, false);
            AutoScroll = true;

            BuildInterface();
            LoadValues();
            ApplyDialogScale();
        }

        private void BuildInterface()
        {
            Panel viewport = new Panel();
            viewport.Dock = DockStyle.Fill;
            viewport.AutoScroll = true;
            viewport.BackColor = UiTheme.Background;
            Controls.Add(viewport);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Location = new Point(24, 18);
            layout.Size = new Size(712, 724);
            layout.ColumnCount = 1;
            layout.RowCount = 7;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 244f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 106f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 206f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            viewport.Controls.Add(layout);
            viewport.AutoScrollMinSize = layout.Size;

            layout.Controls.Add(BuildHeader(), 0, 0);
            layout.Controls.Add(BuildWindowCard(), 0, 1);
            layout.Controls.Add(BuildRecordingCard(), 0, 3);
            layout.Controls.Add(BuildAppearanceCard(), 0, 5);
            layout.Controls.Add(BuildFooter(), 0, 6);
        }

        private Control BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0);
            header.BackColor = Color.Transparent;

            Label title = CreateLabel("界面与录屏设置", 16f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(0, 2);
            title.Size = new Size(500, 37);
            header.Controls.Add(title);

            Label subtitle = CreateLabel(
                "调整窗口、录屏、缩放与颜色；保存后会重新启动软件。",
                9f, FontStyle.Regular, UiTheme.TextMuted);
            subtitle.Location = new Point(1, 41);
            subtitle.Size = new Size(650, 25);
            header.Controls.Add(subtitle);
            return header;
        }

        private Control BuildWindowCard()
        {
            RoundedPanel card = CreateCard(244);
            Label title = CreateLabel("窗口与界面", 10.8f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(18, 13);
            title.Size = new Size(220, 27);
            card.Controls.Add(title);

            Label modeLabel = CreateFieldLabel("启动窗口");
            modeLabel.Location = new Point(18, 51);
            card.Controls.Add(modeLabel);

            _windowModeInput = CreateCombo();
            _windowModeInput.Items.Add("铺满屏幕");
            _windowModeInput.Items.Add("自定义大小");
            _windowModeInput.Location = new Point(112, 44);
            _windowModeInput.Size = new Size(190, 34);
            _windowModeInput.SelectedIndexChanged += delegate { UpdateWindowSizeState(); };
            card.Controls.Add(_windowModeInput);

            Label widthLabel = CreateFieldLabel("窗口宽度");
            widthLabel.Location = new Point(330, 51);
            card.Controls.Add(widthLabel);

            _windowWidthInput = CreateNumeric(900m, 5000m, 50m, 1400m, 0);
            _windowWidthInput.Location = new Point(424, 44);
            _windowWidthInput.Size = new Size(124, 34);
            card.Controls.Add(_windowWidthInput);

            Label heightLabel = CreateFieldLabel("窗口高度");
            heightLabel.Location = new Point(18, 96);
            card.Controls.Add(heightLabel);

            _windowHeightInput = CreateNumeric(600m, 3500m, 50m, 880m, 0);
            _windowHeightInput.Location = new Point(112, 89);
            _windowHeightInput.Size = new Size(124, 34);
            card.Controls.Add(_windowHeightInput);

            Label interfaceLabel = CreateFieldLabel("界面缩放");
            interfaceLabel.Location = new Point(330, 96);
            card.Controls.Add(interfaceLabel);

            _interfaceScaleInput = CreateNumeric(75m, 150m, 5m, 100m, 0);
            _interfaceScaleInput.Location = new Point(424, 89);
            _interfaceScaleInput.Size = new Size(92, 34);
            card.Controls.Add(_interfaceScaleInput);

            Label interfaceSuffix = CreateFieldLabel("%");
            interfaceSuffix.Location = new Point(521, 96);
            interfaceSuffix.Size = new Size(30, 22);
            card.Controls.Add(interfaceSuffix);

            Label fontLabel = CreateFieldLabel("字体大小");
            fontLabel.Location = new Point(18, 141);
            card.Controls.Add(fontLabel);

            _fontScaleInput = CreateNumeric(80m, 160m, 5m, 100m, 0);
            _fontScaleInput.Location = new Point(112, 134);
            _fontScaleInput.Size = new Size(92, 34);
            card.Controls.Add(_fontScaleInput);

            Label fontSuffix = CreateFieldLabel("%");
            fontSuffix.Location = new Point(209, 141);
            fontSuffix.Size = new Size(30, 22);
            card.Controls.Add(fontSuffix);

            Label note = CreateLabel(
                "窗口尺寸使用逻辑像素；高分屏会自动叠加系统 DPI 缩放。",
                8.6f, FontStyle.Regular, UiTheme.TextFaint);
            note.Location = new Point(18, 190);
            note.Size = new Size(660, 28);
            card.Controls.Add(note);
            return card;
        }

        private Control BuildRecordingCard()
        {
            RoundedPanel card = CreateCard(106);
            Label title = CreateLabel("录屏设置", 10.8f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(18, 12);
            title.Size = new Size(200, 27);
            card.Controls.Add(title);

            Label resolutionLabel = CreateFieldLabel("输出分辨率");
            resolutionLabel.Location = new Point(18, 54);
            card.Controls.Add(resolutionLabel);

            _recordingResolutionInput = CreateCombo();
            _recordingResolutionInput.Location = new Point(132, 46);
            _recordingResolutionInput.Size = new Size(220, 34);
            _recordingResolutionInput.Items.Add(new ResolutionOption("原始分辨率", 0));
            _recordingResolutionInput.Items.Add(new ResolutionOption("2560 px", 2560));
            _recordingResolutionInput.Items.Add(new ResolutionOption("1920 px（推荐）", 1920));
            _recordingResolutionInput.Items.Add(new ResolutionOption("1280 px", 1280));
            _recordingResolutionInput.Items.Add(new ResolutionOption("960 px", 960));
            card.Controls.Add(_recordingResolutionInput);

            Label note = CreateLabel(
                "15 FPS · MJPEG AVI，分辨率越低文件越小。",
                8.6f, FontStyle.Regular, UiTheme.TextFaint);
            note.Location = new Point(380, 53);
            note.Size = new Size(310, 28);
            card.Controls.Add(note);
            return card;
        }

        private Control BuildAppearanceCard()
        {
            RoundedPanel card = CreateCard(206);
            Label title = CreateLabel("颜色", 10.8f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(18, 12);
            title.Size = new Size(120, 27);
            card.Controls.Add(title);

            Label backgroundLabel = CreateFieldLabel("界面背景");
            backgroundLabel.Location = new Point(18, 54);
            card.Controls.Add(backgroundLabel);

            _backgroundButton = CreateColorButton();
            _backgroundButton.Location = new Point(132, 45);
            _backgroundButton.Click += delegate { PickBackgroundColor(); };
            card.Controls.Add(_backgroundButton);

            Label accentLabel = CreateFieldLabel("强调颜色");
            accentLabel.Location = new Point(330, 54);
            card.Controls.Add(accentLabel);

            _accentButton = CreateColorButton();
            _accentButton.Location = new Point(432, 45);
            _accentButton.Click += delegate { PickAccentColor(); };
            card.Controls.Add(_accentButton);

            Label presetLabel = CreateFieldLabel("常用背景");
            presetLabel.Location = new Point(18, 102);
            card.Controls.Add(presetLabel);

            FlowLayoutPanel presets = new FlowLayoutPanel();
            presets.Location = new Point(132, 92);
            presets.Size = new Size(540, 48);
            presets.WrapContents = false;
            presets.BackColor = Color.Transparent;
            AddPresetButton(presets, "暖白", Color.FromArgb(250, 249, 247));
            AddPresetButton(presets, "冷灰", Color.FromArgb(247, 247, 248));
            AddPresetButton(presets, "淡蓝", Color.FromArgb(242, 247, 252));
            AddPresetButton(presets, "薄荷", Color.FromArgb(241, 249, 246));
            card.Controls.Add(presets);

            Label note = CreateLabel(
                "为保证黑色文字清晰，背景只允许浅色；强调色用于状态、轨迹与焦点。",
                8.6f, FontStyle.Regular, UiTheme.TextFaint);
            note.Location = new Point(18, 154);
            note.Size = new Size(660, 28);
            card.Controls.Add(note);
            return card;
        }

        private Control BuildFooter()
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0);
            footer.BackColor = Color.Transparent;

            Label note = CreateLabel(
                "保存后程序将自动重启，当前项目需先保存。",
                8.8f, FontStyle.Regular, UiTheme.TextMuted);
            note.Location = new Point(0, 18);
            note.Size = new Size(390, 28);
            footer.Controls.Add(note);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.Width = 350;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 10, 0, 0);

            RoundedButton reset = CreateDialogButton("恢复默认", ButtonRole.Quiet, 88);
            reset.Click += delegate { ResetToDefaults(); };
            buttons.Controls.Add(reset);

            RoundedButton cancel = CreateDialogButton("取消", ButtonRole.Secondary, 82);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);

            RoundedButton apply = CreateDialogButton("保存并重启", ButtonRole.Primary, 132);
            apply.Click += delegate { ApplySettings(); };
            buttons.Controls.Add(apply);

            footer.Controls.Add(buttons);
            AcceptButton = apply;
            CancelButton = cancel;
            return footer;
        }

        private void LoadValues()
        {
            _windowModeInput.SelectedIndex = _source.StartMaximized ? 0 : 1;
            _windowWidthInput.Value = _source.WindowWidth;
            _windowHeightInput.Value = _source.WindowHeight;
            _interfaceScaleInput.Value = _source.InterfaceScalePercent;
            _fontScaleInput.Value = _source.FontScalePercent;

            int selectedResolution = 2;
            for (int i = 0; i < _recordingResolutionInput.Items.Count; i++)
            {
                ResolutionOption option = _recordingResolutionInput.Items[i] as ResolutionOption;
                if (option != null && option.Value == _source.RecordingMaxWidth)
                {
                    selectedResolution = i;
                    break;
                }
            }
            _recordingResolutionInput.SelectedIndex = selectedResolution;

            Color color;
            if (!AppSettings.TryParseColor(_source.BackgroundColor, out color))
                color = Color.FromArgb(247, 247, 248);
            _backgroundColor = color;
            if (!AppSettings.TryParseColor(_source.AccentColor, out color))
                color = Color.FromArgb(15, 139, 126);
            _accentColor = color;
            UpdateColorButtons();
            UpdateWindowSizeState();
        }

        private void UpdateWindowSizeState()
        {
            bool custom = _windowModeInput.SelectedIndex == 1;
            _windowWidthInput.Enabled = custom;
            _windowHeightInput.Enabled = custom;
        }

        private void PickBackgroundColor()
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.Color = _backgroundColor;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (dialog.Color.GetBrightness() < 0.72f)
                    {
                        MessageBox.Show(this, "请选择较浅的背景色，以保证黑色文字清晰可见。",
                            "颜色不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _backgroundColor = dialog.Color;
                    UpdateColorButtons();
                }
            }
        }

        private void PickAccentColor()
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.Color = _accentColor;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (dialog.Color.GetBrightness() > 0.9f)
                    {
                        MessageBox.Show(this, "这个颜色过浅，作为强调色会不够明显。",
                            "颜色不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _accentColor = dialog.Color;
                    UpdateColorButtons();
                }
            }
        }

        private void ResetToDefaults()
        {
            AppSettings defaults = new AppSettings();
            _windowModeInput.SelectedIndex = defaults.StartMaximized ? 0 : 1;
            _windowWidthInput.Value = defaults.WindowWidth;
            _windowHeightInput.Value = defaults.WindowHeight;
            _interfaceScaleInput.Value = defaults.InterfaceScalePercent;
            _fontScaleInput.Value = defaults.FontScalePercent;
            _recordingResolutionInput.SelectedIndex = 2;
            _backgroundColor = ColorTranslator.FromHtml(defaults.BackgroundColor);
            _accentColor = ColorTranslator.FromHtml(defaults.AccentColor);
            UpdateColorButtons();
            UpdateWindowSizeState();
        }

        private void ApplySettings()
        {
            if (_backgroundColor.GetBrightness() < 0.72f)
            {
                MessageBox.Show(this, "界面背景必须使用浅色。",
                    "颜色不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppSettings result = _source.Clone();
            result.StartMaximized = _windowModeInput.SelectedIndex == 0;
            result.WindowWidth = (int)_windowWidthInput.Value;
            result.WindowHeight = (int)_windowHeightInput.Value;
            result.InterfaceScalePercent = (int)_interfaceScaleInput.Value;
            result.FontScalePercent = (int)_fontScaleInput.Value;
            ResolutionOption resolution =
                _recordingResolutionInput.SelectedItem as ResolutionOption;
            result.RecordingMaxWidth = resolution == null ? 1920 : resolution.Value;
            result.BackgroundColor = ToHex(_backgroundColor);
            result.AccentColor = ToHex(_accentColor);
            result.Normalize();
            Settings = result;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateColorButtons()
        {
            StyleColorButton(_backgroundButton, _backgroundColor);
            StyleColorButton(_accentButton, _accentColor);
        }

        private static void StyleColorButton(Button button, Color color)
        {
            button.BackColor = color;
            button.ForeColor = color.GetBrightness() > 0.55f ? Color.Black : Color.White;
            button.Text = ToHex(color);
        }

        private void AddPresetButton(FlowLayoutPanel parent, string label, Color color)
        {
            Button button = new Button();
            button.Text = label;
            button.Size = new Size(112, 36);
            button.Margin = new Padding(0, 0, 8, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = UiTheme.Border;
            button.BackColor = color;
            button.ForeColor = UiTheme.Text;
            button.Font = UiTheme.Font(8.5f, true);
            button.Click += delegate
            {
                _backgroundColor = color;
                UpdateColorButtons();
            };
            parent.Controls.Add(button);
        }

        private RoundedPanel CreateCard(int height)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);
            card.BackColor = UiTheme.Surface;
            card.BorderColor = UiTheme.BorderSoft;
            card.Height = height;
            return card;
        }

        private static Label CreateFieldLabel(string text)
        {
            Label label = CreateLabel(text, 8.8f, FontStyle.Bold, UiTheme.TextMuted);
            label.Size = new Size(92, 24);
            return label;
        }

        private static Label CreateLabel(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Font = UiTheme.Font(size, (style & FontStyle.Bold) != 0);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static ComboBox CreateCombo()
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = UiTheme.SurfaceRaised;
            combo.ForeColor = UiTheme.Text;
            combo.Font = UiTheme.Font(8.8f, false);
            return combo;
        }

        private static UiNumeric CreateNumeric(decimal min, decimal max,
            decimal increment, decimal value, int decimals)
        {
            UiNumeric input = new UiNumeric(min, max, increment, value, decimals);
            input.Font = UiTheme.Mono(9.2f, true);
            return input;
        }

        private static Button CreateColorButton()
        {
            Button button = new Button();
            button.Size = new Size(150, 36);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = UiTheme.Border;
            button.Font = UiTheme.Mono(8.6f, true);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static RoundedButton CreateDialogButton(string text,
            ButtonRole role, int width)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Role = role;
            button.Size = new Size(width, 42);
            button.Margin = new Padding(8, 0, 0, 0);
            button.Font = UiTheme.Font(9f, true);
            return button;
        }

        private static string ToHex(Color color)
        {
            return "#" + color.R.ToString("X2") +
                color.G.ToString("X2") + color.B.ToString("X2");
        }

        private void ApplyDialogScale()
        {
            float systemScale;
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                systemScale = graphics.DpiX / 96f;
            float scale = systemScale *
                (Math.Max(75, _source.InterfaceScalePercent) / 100f);
            if (scale > 1.01f)
            {
                SuspendLayout();
                Scale(new SizeF(scale, scale));
                ResumeLayout(true);
                PerformLayout();
            }

            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(
                Math.Min(Width, Math.Max(520, workArea.Width - 32)),
                Math.Min(Height, Math.Max(480, workArea.Height - 32)));
        }

        private sealed class ResolutionOption
        {
            public string Label { get; private set; }
            public int Value { get; private set; }

            public ResolutionOption(string label, int value)
            {
                Label = label;
                Value = value;
            }

            public override string ToString()
            {
                return Label;
            }
        }
    }
}
