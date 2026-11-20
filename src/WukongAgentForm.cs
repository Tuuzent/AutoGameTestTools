using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GameInputLab.Core;
using GameInputLab.UI;

namespace GameInputLab
{
    internal sealed class WukongAgentForm : Form
    {
        private const int DesignWidth = 900;
        private const int DesignHeight = 720;

        private readonly string _profileDirectory;
        private readonly float _interfaceScale;
        private readonly Dictionary<CombatActionKind, Button> _bindingButtons =
            new Dictionary<CombatActionKind, Button>();

        private CombatProfile _working;
        private Button _regionButton;
        private Label _exampleSummaryLabel;
        private UiNumeric _sensitivityInput;
        private UiNumeric _attackIntervalInput;
        private CheckBox _autoAttackCheck;
        private bool _capturingKey;
        private CombatActionKind _captureAction;
        private RoundedButton _runButton;
        private RoundedButton _trainingButton;

        public CombatProfile Profile { get; private set; }
        public bool StartTrainingRequested { get; private set; }
        public bool StartAgentRequested { get; private set; }

        public WukongAgentForm(CombatProfile profile, string profileDirectory)
            : this(profile, profileDirectory, 1f)
        {
        }

        public WukongAgentForm(CombatProfile profile, string profileDirectory,
            float interfaceScale)
        {
            _profileDirectory = profileDirectory;
            _interfaceScale = Math.Max(0.75f, Math.Min(1.5f, interfaceScale));
            _working = profile == null ? new CombatProfile() : profile.Clone();
            _working.Examples = new List<CombatExample>();
            if (profile != null && profile.Examples != null)
            {
                for (int i = 0; i < profile.Examples.Count; i++)
                {
                    if (profile.Examples[i] != null)
                        _working.Examples.Add(profile.Examples[i].Clone());
                }
            }
            _working.Normalize();
            Profile = _working;

            Text = "黑神话：悟空 · 战斗代理";
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
            KeyPreview = true;
            AutoScroll = true;

            BuildInterface();
            UpdateBindingButtons();
            UpdateExampleSummary();
            ApplyDialogScale();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_capturingKey)
            {
                Keys key = keyData & Keys.KeyCode;
                if (key == Keys.Escape)
                {
                    _capturingKey = false;
                    SetStatus("已取消按键捕获。", false);
                    return true;
                }
                if (key == Keys.ShiftKey || key == Keys.ControlKey ||
                    key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                {
                    return true;
                }
                if (key == Keys.F8 || key == Keys.F9 || key == Keys.F10 ||
                    key == Keys.F11 || key == Keys.F12)
                {
                    _capturingKey = false;
                    SetStatus("F8–F12 是软件全局快捷键，请选择其他按键。", true);
                    return true;
                }

                CombatInputBinding binding = _working.GetBinding(_captureAction);
                if (binding != null)
                {
                    binding.Kind = CombatBindingKind.Keyboard;
                    binding.VirtualKey = (int)key;
                    binding.ScanCode = (int)MapVirtualKey((uint)key, 0);
                    binding.IsExtended = (key & Keys.KeyCode) == Keys.Left ||
                        (key & Keys.KeyCode) == Keys.Right;
                    binding.KeyName = key.ToString();
                    binding.Normalize();
                }
                _capturingKey = false;
                UpdateBindingButtons();
                SetStatus("动作映射已更新。", false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
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
            layout.Size = new Size(852, 674);
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 330f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            viewport.Controls.Add(layout);
            viewport.AutoScrollMinSize = layout.Size;

            layout.Controls.Add(BuildHeader(), 0, 0);
            layout.Controls.Add(BuildObservationCard(), 0, 1);
            layout.Controls.Add(BuildLearningCard(), 0, 3);
            layout.Controls.Add(BuildFooter(), 0, 5);
        }

        private Control BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0);
            header.BackColor = Color.Transparent;

            Label title = CreateLabel(
                "黑神话：悟空 · 战斗代理",
                16f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(0, 0);
            title.Size = new Size(600, 36);
            header.Controls.Add(title);

            Label subtitle = CreateLabel(
                "一次示范，适应不同的攻击节奏：选择观察区域 → 映射动作 → 录制示范",
                9f, FontStyle.Regular, UiTheme.TextMuted);
            subtitle.Location = new Point(1, 37);
            subtitle.Size = new Size(820, 24);
            header.Controls.Add(subtitle);
            return header;
        }

        private Control BuildObservationCard()
        {
            RoundedPanel card = CreateCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            Label title = CreateLabel(
                "1 观察区域与动作映射",
                10.8f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(18, 13);
            title.Size = new Size(430, 27);
            card.Controls.Add(title);

            Label regionLabel = CreateFieldLabel("观察区域");
            regionLabel.Location = new Point(18, 53);
            card.Controls.Add(regionLabel);

            _regionButton = CreateValueButton();
            _regionButton.Location = new Point(112, 45);
            _regionButton.Size = new Size(440, 36);
            _regionButton.Click += delegate { PickObservationRegion(); };
            card.Controls.Add(_regionButton);

            Label regionHint = CreateLabel(
                "建议框选屏幕中央的 Boss 身体和攻击前摇",
                8.5f, FontStyle.Regular, UiTheme.TextFaint);
            regionHint.Location = new Point(568, 50);
            regionHint.Size = new Size(260, 28);
            card.Controls.Add(regionHint);

            TableLayoutPanel mapping = new TableLayoutPanel();
            mapping.Location = new Point(18, 96);
            mapping.Size = new Size(816, 168);
            mapping.ColumnCount = 2;
            mapping.RowCount = 3;
            mapping.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            mapping.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            mapping.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            mapping.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            mapping.RowStyles.Add(new RowStyle(SizeType.Percent, 33.334f));
            card.Controls.Add(mapping);

            mapping.Controls.Add(CreateBindingCell(
                CombatActionKind.Dodge, "闪避"), 0, 0);
            mapping.Controls.Add(CreateBindingCell(
                CombatActionKind.LightAttack, "轻攻击"), 1, 0);
            mapping.Controls.Add(CreateBindingCell(
                CombatActionKind.HeavyAttack, "重攻击"), 0, 1);
            mapping.Controls.Add(CreateBindingCell(
                CombatActionKind.Heal, "喝药"), 1, 1);
            mapping.Controls.Add(CreateBindingCell(
                CombatActionKind.LockTarget, "锁定目标"), 0, 2);

            Label mappingHint = CreateLabel(
                "左键点击动作按钮后按键盘；右键可选择鼠标左键或右键。",
                8.6f, FontStyle.Regular, UiTheme.TextFaint);
            mappingHint.Location = new Point(18, 278);
            mappingHint.Size = new Size(780, 26);
            card.Controls.Add(mappingHint);
            return card;
        }

        private Control BuildLearningCard()
        {
            RoundedPanel card = CreateCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            Label title = CreateLabel(
                "2 示范学习与安全设置",
                10.8f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(18, 12);
            title.Size = new Size(400, 27);
            card.Controls.Add(title);

            _exampleSummaryLabel = CreateLabel(
                "尚未录制示范", 8.8f, FontStyle.Regular, UiTheme.TextMuted);
            _exampleSummaryLabel.Location = new Point(18, 40);
            _exampleSummaryLabel.Size = new Size(360, 42);
            card.Controls.Add(_exampleSummaryLabel);

            _trainingButton = CreateActionButton("开始录制示范", ButtonRole.Primary, 132);
            _trainingButton.Location = new Point(398, 38);
            _trainingButton.Click += delegate { RequestTraining(); };
            card.Controls.Add(_trainingButton);

            RoundedButton clear = CreateActionButton("清空示范", ButtonRole.Quiet, 100);
            clear.Role = ButtonRole.Secondary;
            clear.Location = new Point(540, 38);
            clear.Click += delegate { ClearExamples(); };
            card.Controls.Add(clear);

            Label sensitivityLabel = CreateFieldLabel("识别灵敏度");
            sensitivityLabel.Size = new Size(100, 24);
            sensitivityLabel.Location = new Point(18, 88);
            card.Controls.Add(sensitivityLabel);

            _sensitivityInput = CreateNumeric(70m, 99.5m, 0.5m,
                (decimal)(_working.MatchThreshold * 100.0), 1);
            _sensitivityInput.Location = new Point(122, 80);
            _sensitivityInput.Size = new Size(92, 34);
            card.Controls.Add(_sensitivityInput);

            Label percent = CreateFieldLabel("%");
            percent.Location = new Point(219, 87);
            percent.Size = new Size(26, 24);
            card.Controls.Add(percent);

            _autoAttackCheck = new CheckBox();
            _autoAttackCheck.Text = "没有匹配示范时自动轻击";
            _autoAttackCheck.Checked = _working.AutoAttackEnabled;
            _autoAttackCheck.Location = new Point(330, 83);
            _autoAttackCheck.Size = new Size(265, 30);
            _autoAttackCheck.Font = UiTheme.Font(8.8f, false);
            _autoAttackCheck.ForeColor = UiTheme.Text;
            _autoAttackCheck.BackColor = Color.Transparent;
            _autoAttackCheck.CheckedChanged += delegate { UpdateAttackIntervalState(); };
            card.Controls.Add(_autoAttackCheck);

            _attackIntervalInput = CreateNumeric(
                200m, 5000m, 50m, _working.AutoAttackIntervalMs, 0);
            _attackIntervalInput.Location = new Point(610, 80);
            _attackIntervalInput.Size = new Size(88, 34);
            card.Controls.Add(_attackIntervalInput);

            Label intervalSuffix = CreateFieldLabel("ms");
            intervalSuffix.Location = new Point(703, 87);
            intervalSuffix.Size = new Size(36, 24);
            card.Controls.Add(intervalSuffix);

            Label hint = CreateLabel(
                "录制时正常游玩；按下映射动作前会保存约 350ms 内的画面。示范越多，代理越能区分不同攻击。",
                8.6f, FontStyle.Regular, UiTheme.TextFaint);
            hint.Location = new Point(18, 134);
            hint.Size = new Size(780, 42);
            card.Controls.Add(hint);
            return card;
        }

        private Control BuildFooter()
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0);
            footer.BackColor = Color.Transparent;

            Label hint = CreateLabel(
                "按 F8 结束示范；按 F11 开始或停止战斗代理。",
                8.8f, FontStyle.Regular, UiTheme.TextMuted);
            hint.Location = new Point(0, 20);
            hint.Size = new Size(390, 28);
            footer.Controls.Add(hint);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.Width = 430;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 9, 0, 0);

            RoundedButton cancel = CreateActionButton(
                "取消", ButtonRole.Secondary, 82);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);

            RoundedButton save = CreateActionButton(
                "只保存", ButtonRole.Secondary, 92);
            save.Click += delegate { SaveConfiguration(false, false); };
            buttons.Controls.Add(save);

            _runButton = CreateActionButton(
                "保存并战斗", ButtonRole.Primary, 128);
            _runButton.Click += delegate { SaveConfiguration(false, true); };
            buttons.Controls.Add(_runButton);

            footer.Controls.Add(buttons);
            AcceptButton = _runButton;
            CancelButton = cancel;
            return footer;
        }

        private Control CreateBindingCell(CombatActionKind action, string labelText)
        {
            Panel cell = new Panel();
            cell.Dock = DockStyle.Fill;
            cell.Margin = new Padding(0, 0, 8, 7);
            cell.BackColor = Color.Transparent;

            Label label = CreateFieldLabel(labelText);
            label.Location = new Point(0, 7);
            label.Size = new Size(88, 28);
            cell.Controls.Add(label);

            Button button = CreateValueButton();
            button.Location = new Point(96, 1);
            button.Size = new Size(212, 38);
            button.Tag = action;
            button.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                Button source = sender as Button;
                if (source == null)
                    return;
                if (e.Button == MouseButtons.Right)
                    ShowMouseButtonMenu(source, action);
                else if (e.Button == MouseButtons.Left)
                    BeginKeyboardCapture(source, action);
            };
            cell.Controls.Add(button);
            _bindingButtons[action] = button;
            return cell;
        }

        private void BeginKeyboardCapture(Button source, CombatActionKind action)
        {
            _capturingKey = true;
            _captureAction = action;
            source.Focus();
            SetStatus("请按下“" + GetActionLabel(action) + "”对应的键盘按键，Esc 取消。", false);
        }

        private void ShowMouseButtonMenu(Button source, CombatActionKind action)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = UiTheme.SurfaceRaised;
            menu.ForeColor = UiTheme.Text;
            menu.ShowImageMargin = false;
            ToolStripMenuItem left = new ToolStripMenuItem("鼠标左键");
            left.Click += delegate { SetMouseBinding(action, CombatBindingKind.MouseLeft); };
            menu.Items.Add(left);
            ToolStripMenuItem right = new ToolStripMenuItem("鼠标右键");
            right.Click += delegate { SetMouseBinding(action, CombatBindingKind.MouseRight); };
            menu.Items.Add(right);
            menu.Closed += delegate { menu.Dispose(); };
            menu.Show(source, new Point(0, source.Height));
        }

        private void SetMouseBinding(CombatActionKind action, CombatBindingKind kind)
        {
            CombatInputBinding binding = _working.GetBinding(action);
            if (binding == null)
                return;
            binding.Kind = kind;
            binding.VirtualKey = 0;
            binding.ScanCode = 0;
            binding.IsExtended = false;
            binding.KeyName = kind == CombatBindingKind.MouseLeft
                ? "鼠标左键"
                : "鼠标右键";
            UpdateBindingButtons();
            SetStatus("动作映射已更新。", false);
        }

        private void PickObservationRegion()
        {
            using (RegionPickerForm picker = new RegionPickerForm())
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;
                Rectangle region = picker.SelectedRegion;
                if (region.Width < 32 || region.Height < 32)
                {
                    SetStatus("观察区域太小，请重新框选。", true);
                    return;
                }
                _working.ObservationLeft = region.Left;
                _working.ObservationTop = region.Top;
                _working.ObservationWidth = region.Width;
                _working.ObservationHeight = region.Height;
                UpdateRegionButton();
                SetStatus("观察区域已更新。", false);
            }
        }

        private void RequestTraining()
        {
            if (!SaveValuesToProfile())
                return;
            StartTrainingRequested = true;
            StartAgentRequested = false;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ClearExamples()
        {
            if (_working.Examples.Count == 0)
                return;
            if (MessageBox.Show(this, "清空当前已录制的全部战斗示范？",
                "清空示范", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            new CombatProfileStore().ClearExamples(_profileDirectory, _working);
            UpdateExampleSummary();
            SetStatus("示范数据已清空。", false);
        }

        private void SaveConfiguration(bool training, bool run)
        {
            if (!SaveValuesToProfile())
                return;
            StartTrainingRequested = training;
            StartAgentRequested = run;
            if (run && _working.Examples.Count == 0)
            {
                StartAgentRequested = false;
                SetStatus("请先录制至少一轮战斗示范。", true);
                return;
            }
            Profile = _working;
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool SaveValuesToProfile()
        {
            if (_working.ObservationWidth < 32 || _working.ObservationHeight < 32)
            {
                SetStatus("请先选择观察区域。", true);
                return false;
            }
            CombatInputBinding dodge = _working.GetBinding(CombatActionKind.Dodge);
            if (dodge == null || !dodge.IsConfigured())
            {
                SetStatus("请先为“闪避”设置键盘或鼠标动作。", true);
                return false;
            }

            _working.AutoAttackEnabled = _autoAttackCheck.Checked;
            _working.AutoAttackIntervalMs = (int)_attackIntervalInput.Value;
            _working.MatchThreshold = (double)_sensitivityInput.Value / 100.0;
            _working.Normalize();
            UpdateExampleSummary();
            return true;
        }

        private void UpdateBindingButtons()
        {
            foreach (KeyValuePair<CombatActionKind, Button> pair in _bindingButtons)
            {
                CombatInputBinding binding = _working.GetBinding(pair.Key);
                pair.Value.Text = binding == null
                    ? "未设置"
                    : binding.GetDisplayName();
            }
        }

        private void UpdateExampleSummary()
        {
            int dodge = CountExamples(CombatActionKind.Dodge);
            int light = CountExamples(CombatActionKind.LightAttack);
            int heavy = CountExamples(CombatActionKind.HeavyAttack);
            int heal = CountExamples(CombatActionKind.Heal);
            int lockCount = CountExamples(CombatActionKind.LockTarget);
            _exampleSummaryLabel.Text = "共 " + _working.Examples.Count +
                " 个示范 · 闪避 " + dodge + " · 喝药 " + heal +
                "\r\n轻击 " + light + " · 重击 " + heavy +
                " · 锁定 " + lockCount;
            _runButton.Enabled = _working.Examples.Count > 0;
            UpdateRegionButton();
            UpdateAttackIntervalState();
        }

        private int CountExamples(CombatActionKind action)
        {
            int count = 0;
            for (int i = 0; i < _working.Examples.Count; i++)
            {
                if (_working.Examples[i] != null &&
                    _working.Examples[i].Action == action)
                {
                    count++;
                }
            }
            return count;
        }

        private void UpdateRegionButton()
        {
            if (_regionButton == null)
                return;
            if (_working.ObservationWidth <= 0 || _working.ObservationHeight <= 0)
                _regionButton.Text = "点击选择观察区域";
            else
                _regionButton.Text = _working.ObservationLeft + ", " +
                    _working.ObservationTop + "   " +
                    _working.ObservationWidth + " × " + _working.ObservationHeight;
        }

        private void UpdateAttackIntervalState()
        {
            if (_attackIntervalInput != null)
                _attackIntervalInput.Enabled = _autoAttackCheck.Checked;
        }

        private void SetStatus(string text, bool error)
        {
            // The form remains uncluttered; status messages are shown as a short overlay.
            if (IsDisposed || !IsHandleCreated)
                return;
            UiToast.Show(this, text, error ? ToastKind.Warning : ToastKind.Info);
        }

        private void ApplyDialogScale()
        {
            float systemScale;
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                systemScale = graphics.DpiX / 96f;
            float scale = systemScale * _interfaceScale;
            if (scale > 1.01f)
            {
                SuspendLayout();
                Scale(new SizeF(scale, scale));
                ResumeLayout(true);
                PerformLayout();
            }

            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(
                Math.Min(Width, Math.Max(620, workArea.Width - 32)),
                Math.Min(Height, Math.Max(540, workArea.Height - 32)));
        }

        private static string GetActionLabel(CombatActionKind action)
        {
            switch (action)
            {
                case CombatActionKind.Dodge:
                    return "闪避";
                case CombatActionKind.LightAttack:
                    return "轻攻击";
                case CombatActionKind.HeavyAttack:
                    return "重攻击";
                case CombatActionKind.Heal:
                    return "喝药";
                case CombatActionKind.LockTarget:
                    return "锁定目标";
                default:
                    return action.ToString();
            }
        }

        private static RoundedPanel CreateCard()
        {
            RoundedPanel card = new RoundedPanel();
            card.BackColor = UiTheme.Surface;
            card.BorderColor = UiTheme.BorderSoft;
            card.CornerRadius = 12;
            return card;
        }

        private static Label CreateFieldLabel(string text)
        {
            Label label = CreateLabel(text, 8.7f, FontStyle.Bold, UiTheme.TextMuted);
            label.Size = new Size(90, 24);
            return label;
        }

        private static Label CreateLabel(string text, float size,
            FontStyle style, Color color)
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

        private static Button CreateValueButton()
        {
            Button button = new Button();
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = UiTheme.Border;
            button.BackColor = UiTheme.SurfaceRaised;
            button.ForeColor = UiTheme.Text;
            button.Font = UiTheme.Font(8.8f, false);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static UiNumeric CreateNumeric(decimal min, decimal max,
            decimal increment, decimal value, int decimals)
        {
            UiNumeric input = new UiNumeric(min, max, increment, value, decimals);
            input.Font = UiTheme.Mono(9.1f, true);
            return input;
        }

        private static RoundedButton CreateActionButton(
            string text, ButtonRole role, int width)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Role = role;
            button.Size = new Size(width, 42);
            button.Margin = new Padding(8, 0, 0, 0);
            button.Font = UiTheme.Font(9f, true);
            return button;
        }

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);
    }
}
