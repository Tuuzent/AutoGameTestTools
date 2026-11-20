using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using GameInputLab.Core;
using GameInputLab.UI;

namespace GameInputLab
{
    internal sealed class StrategyRulesForm : Form
    {
        private const int DesignWidth = 1100;
        private const int DesignHeight = 740;

        private readonly List<StrategyRule> _rules;
        private readonly float _interfaceScale;
        private ListBox _ruleList;
        private Panel _editorPanel;
        private TextBox _nameBox;
        private CheckBox _enabledBox;
        private UiNumeric _priorityInput;
        private ComboBox _triggerTypeInput;
        private Label _regionLabel;
        private UiNumeric _toleranceInput;
        private UiNumeric _matchPercentInput;
        private UiNumeric _cooldownInput;
        private Button _pickRegionButton;
        private Button _colorButton;
        private ComboBox _actionTypeInput;
        private TextBox _keyBox;
        private UiNumeric _holdInput;
        private UiNumeric _deltaXInput;
        private UiNumeric _deltaYInput;
        private TextBox _notesBox;
        private Label _statusLabel;
        private bool _syncing;
        private bool _capturingKey;
        private Color _targetColor;

        public List<StrategyRule> Rules { get; private set; }

        public StrategyRulesForm(IList<StrategyRule> rules)
            : this(rules, 1f)
        {
        }

        public StrategyRulesForm(IList<StrategyRule> rules, float interfaceScale)
        {
            _interfaceScale = Math.Max(0.75f, Math.Min(1.5f, interfaceScale));
            _rules = new List<StrategyRule>();
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    if (rules[i] != null)
                    {
                        StrategyRule clone = rules[i].Clone();
                        clone.Normalize();
                        _rules.Add(clone);
                    }
                }
            }
            SortRules();
            Rules = new List<StrategyRule>();

            Text = "智能策略规则";
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
            RefreshRuleList(null);
            if (_rules.Count > 0)
                _ruleList.SelectedIndex = 0;
            else
                ShowEmptyEditor();
            ApplyDialogScale();
        }

        public void SetStatus(string text, bool error)
        {
            if (_statusLabel == null)
                return;
            _statusLabel.Text = text ?? String.Empty;
            _statusLabel.ForeColor = error ? UiTheme.Record : UiTheme.TextMuted;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_capturingKey)
            {
                Keys key = keyData & Keys.KeyCode;
                if (key == Keys.Escape)
                {
                    _capturingKey = false;
                    SyncEditorFromRule();
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
                    SetStatus("F8–F12 已被录制与紧急停止功能占用。", true);
                    _capturingKey = false;
                    SyncEditorFromRule();
                    return true;
                }

                StrategyRule rule = SelectedRule();
                if (rule != null)
                {
                    rule.VirtualKey = (int)key;
                    rule.ScanCode = 0;
                    rule.IsExtended = false;
                    rule.KeyName = key.ToString();
                    CommitEditor();
                }
                _capturingKey = false;
                _keyBox.Text = key.ToString();
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
            layout.Size = new Size(1052, 700);
            layout.ColumnCount = 3;
            layout.RowCount = 3;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 304f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
            viewport.Controls.Add(layout);
            viewport.AutoScrollMinSize = layout.Size;

            Panel header = BuildHeader();
            layout.Controls.Add(header, 0, 0);
            layout.SetColumnSpan(header, 3);

            RoundedPanel listCard = BuildRuleListCard();
            listCard.Margin = new Padding(0);
            layout.Controls.Add(listCard, 0, 1);

            RoundedPanel editorCard = BuildEditorCard();
            editorCard.Margin = new Padding(0);
            layout.Controls.Add(editorCard, 2, 1);

            Panel footer = BuildFooter();
            footer.Margin = new Padding(0);
            layout.Controls.Add(footer, 0, 2);
            layout.SetColumnSpan(footer, 3);
        }

        private Panel BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Color.Transparent;

            Label title = CreateLabel("智能策略规则", 17f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(0, 1);
            title.Size = new Size(420, 38);
            header.Controls.Add(title);

            Label subtitle = CreateLabel(
                "按优先级检测屏幕区域；规则命中后才执行按键或鼠标动作。",
                9f, FontStyle.Regular, UiTheme.TextMuted);
            subtitle.Location = new Point(1, 39);
            subtitle.Size = new Size(760, 25);
            header.Controls.Add(subtitle);
            return header;
        }

        private RoundedPanel BuildRuleListCard()
        {
            RoundedPanel card = CreateCard();
            card.Dock = DockStyle.Fill;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14, 12, 14, 12);
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
            card.Controls.Add(layout);

            Panel heading = new Panel();
            heading.Dock = DockStyle.Fill;
            heading.BackColor = Color.Transparent;
            Label title = CreateLabel("规则列表", 10.5f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(0, 0);
            title.Size = new Size(260, 20);
            heading.Controls.Add(title);
            _statusLabel = CreateLabel("按优先级从高到低执行", 8.2f, FontStyle.Regular, UiTheme.TextFaint);
            _statusLabel.Location = new Point(0, 22);
            _statusLabel.Size = new Size(260, 24);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            heading.Controls.Add(_statusLabel);
            layout.Controls.Add(heading, 0, 0);

            _ruleList = new ListBox();
            _ruleList.Dock = DockStyle.Fill;
            _ruleList.BorderStyle = BorderStyle.None;
            _ruleList.DrawMode = DrawMode.OwnerDrawFixed;
            _ruleList.ItemHeight = 58;
            _ruleList.BackColor = UiTheme.Surface;
            _ruleList.ForeColor = UiTheme.Text;
            _ruleList.Font = UiTheme.Font(8.8f, false);
            _ruleList.IntegralHeight = false;
            _ruleList.DrawItem += DrawRuleListItem;
            _ruleList.SelectedIndexChanged += delegate
            {
                StrategyRule rule = SelectedRule();
                if (rule == null)
                    ShowEmptyEditor();
                else
                    SyncEditorFromRule();
            };
            layout.Controls.Add(_ruleList, 0, 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.WrapContents = false;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.Padding = new Padding(0, 9, 0, 0);

            RoundedButton add = CreateSmallButton("新增");
            add.Role = ButtonRole.Primary;
            add.Click += delegate { AddRule(); };
            buttons.Controls.Add(add);

            RoundedButton duplicate = CreateSmallButton("复制");
            duplicate.Click += delegate { DuplicateRule(); };
            buttons.Controls.Add(duplicate);

            RoundedButton delete = CreateSmallButton("删除");
            delete.Role = ButtonRole.Danger;
            delete.Click += delegate { DeleteRule(); };
            buttons.Controls.Add(delete);
            layout.Controls.Add(buttons, 0, 2);
            return card;
        }

        private RoundedPanel BuildEditorCard()
        {
            RoundedPanel card = CreateCard();
            card.Dock = DockStyle.Fill;
            _editorPanel = new Panel();
            _editorPanel.Dock = DockStyle.Fill;
            _editorPanel.BackColor = Color.Transparent;
            _editorPanel.AutoScroll = true;
            card.Controls.Add(_editorPanel);

            Label title = CreateLabel("规则设置", 10.5f, FontStyle.Bold, UiTheme.Text);
            title.Location = new Point(18, 14);
            title.Size = new Size(180, 28);
            _editorPanel.Controls.Add(title);

            AddFieldLabel(_editorPanel, "规则名称", 18, 57);
            _nameBox = CreateTextBox();
            _nameBox.Location = new Point(108, 49);
            _nameBox.Size = new Size(230, 34);
            _nameBox.TextChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_nameBox);

            _enabledBox = new CheckBox();
            _enabledBox.Text = "启用";
            _enabledBox.Location = new Point(340, 53);
            _enabledBox.Size = new Size(90, 26);
            _enabledBox.Font = UiTheme.Font(8.8f, false);
            _enabledBox.ForeColor = UiTheme.Text;
            _enabledBox.BackColor = Color.Transparent;
            _enabledBox.CheckedChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_enabledBox);

            AddFieldLabel(_editorPanel, "优先级", 438, 57);
            _priorityInput = CreateNumeric(530, 49, 82, -100m, 100m, 1m, 0m, 0);
            _priorityInput.ValueChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_priorityInput);

            AddSeparator(_editorPanel, 103);

            Label triggerTitle = CreateLabel("触发条件", 9.5f, FontStyle.Bold, UiTheme.Text);
            triggerTitle.Location = new Point(18, 114);
            triggerTitle.Size = new Size(130, 24);
            _editorPanel.Controls.Add(triggerTitle);

            AddFieldLabel(_editorPanel, "触发方式", 18, 153);
            _triggerTypeInput = CreateCombo();
            _triggerTypeInput.Location = new Point(108, 145);
            _triggerTypeInput.Size = new Size(142, 34);
            _triggerTypeInput.Items.Add("区域颜色");
            _triggerTypeInput.Items.Add("固定节拍");
            _triggerTypeInput.SelectedIndexChanged += delegate
            {
                CommitEditor();
                UpdateFieldAvailability();
            };
            _editorPanel.Controls.Add(_triggerTypeInput);

            AddFieldLabel(_editorPanel, "检测区域", 252, 153);
            _regionLabel = CreateLabel("未选择", 8.5f, FontStyle.Regular, UiTheme.TextMuted);
            _regionLabel.Location = new Point(344, 145);
            _regionLabel.Size = new Size(190, 34);
            _regionLabel.BorderStyle = BorderStyle.FixedSingle;
            _regionLabel.TextAlign = ContentAlignment.MiddleCenter;
            _regionLabel.BackColor = UiTheme.SurfaceRaised;
            _editorPanel.Controls.Add(_regionLabel);

            _pickRegionButton = CreateColorButton("屏幕拾取");
            _pickRegionButton.Location = new Point(540, 145);
            _pickRegionButton.Size = new Size(112, 34);
            _pickRegionButton.Click += delegate { PickRegion(); };
            _editorPanel.Controls.Add(_pickRegionButton);

            AddFieldLabel(_editorPanel, "目标颜色", 18, 199);
            _colorButton = CreateColorButton("#FF3B30");
            _colorButton.Location = new Point(108, 191);
            _colorButton.Size = new Size(130, 34);
            _colorButton.Click += delegate { PickColor(); };
            _editorPanel.Controls.Add(_colorButton);

            AddFieldLabel(_editorPanel, "颜色容差", 244, 199);
            _toleranceInput = CreateNumeric(334, 191, 72, 0m, 128m, 1m, 48m, 0);
            _toleranceInput.ValueChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_toleranceInput);

            AddFieldLabel(_editorPanel, "匹配比例", 408, 199);
            _matchPercentInput = CreateNumeric(498, 191, 60, 1m, 100m, 1m, 20m, 0);
            _matchPercentInput.ValueChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_matchPercentInput);
            Label percentSuffix = CreateLabel("%", 8.8f, FontStyle.Bold, UiTheme.TextMuted);
            percentSuffix.Location = new Point(562, 197);
            percentSuffix.Size = new Size(24, 24);
            _editorPanel.Controls.Add(percentSuffix);

            AddSeparator(_editorPanel, 239);

            Label actionTitle = CreateLabel("执行动作", 9.5f, FontStyle.Bold, UiTheme.Text);
            actionTitle.Location = new Point(18, 250);
            actionTitle.Size = new Size(130, 24);
            _editorPanel.Controls.Add(actionTitle);

            AddFieldLabel(_editorPanel, "动作类型", 18, 289);
            _actionTypeInput = CreateCombo();
            _actionTypeInput.Location = new Point(108, 281);
            _actionTypeInput.Size = new Size(150, 34);
            _actionTypeInput.Items.Add("按键");
            _actionTypeInput.Items.Add("鼠标左键");
            _actionTypeInput.Items.Add("鼠标右键");
            _actionTypeInput.Items.Add("相对镜头");
            _actionTypeInput.SelectedIndexChanged += delegate
            {
                CommitEditor();
                UpdateFieldAvailability();
            };
            _editorPanel.Controls.Add(_actionTypeInput);

            AddFieldLabel(_editorPanel, "按键", 262, 289);
            _keyBox = CreateTextBox();
            _keyBox.Location = new Point(350, 281);
            _keyBox.Size = new Size(110, 34);
            _keyBox.ReadOnly = true;
            _keyBox.Cursor = Cursors.Hand;
            _keyBox.Click += delegate
            {
                _capturingKey = true;
                _keyBox.Text = "请按一个键…";
                _keyBox.Focus();
            };
            _editorPanel.Controls.Add(_keyBox);

            AddFieldLabel(_editorPanel, "按住", 470, 289);
            _holdInput = CreateNumeric(560, 281, 64, 0m, 5000m, 10m, 60m, 0);
            _holdInput.ValueChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_holdInput);
            Label holdSuffix = CreateLabel("ms", 8.2f, FontStyle.Regular, UiTheme.TextMuted);
            holdSuffix.Location = new Point(628, 287);
            holdSuffix.Size = new Size(38, 22);
            _editorPanel.Controls.Add(holdSuffix);

            AddFieldLabel(_editorPanel, "冷却间隔", 18, 335);
            _cooldownInput = CreateNumeric(108, 327, 86, 80m, 60000m, 50m, 600m, 0);
            _cooldownInput.ValueChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_cooldownInput);
            Label cooldownSuffix = CreateLabel("ms", 8.2f, FontStyle.Regular, UiTheme.TextMuted);
            cooldownSuffix.Location = new Point(198, 333);
            cooldownSuffix.Size = new Size(38, 22);
            _editorPanel.Controls.Add(cooldownSuffix);

            AddFieldLabel(_editorPanel, "镜头 X", 244, 335);
            _deltaXInput = CreateNumeric(334, 327, 74, -2000m, 2000m, 1m, 0m, 0);
            _deltaXInput.ValueChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_deltaXInput);

            AddFieldLabel(_editorPanel, "镜头 Y", 416, 335);
            _deltaYInput = CreateNumeric(506, 327, 74, -2000m, 2000m, 1m, 0m, 0);
            _deltaYInput.ValueChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_deltaYInput);

            AddFieldLabel(_editorPanel, "备注", 18, 381);
            _notesBox = CreateTextBox();
            _notesBox.Location = new Point(108, 373);
            _notesBox.Size = new Size(534, 58);
            _notesBox.Multiline = true;
            _notesBox.ScrollBars = ScrollBars.Vertical;
            _notesBox.TextChanged += delegate { CommitEditor(); };
            _editorPanel.Controls.Add(_notesBox);

            Label hint = CreateLabel(
                "颜色规则会持续等待条件出现；固定节拍规则按冷却时间循环执行。规则按优先级从高到低匹配，命中一条后跳过其余规则。",
                8.4f, FontStyle.Regular, UiTheme.TextFaint);
            hint.Location = new Point(18, 448);
            hint.Size = new Size(610, 48);
            _editorPanel.Controls.Add(hint);

            return card;
        }

        private Panel BuildFooter()
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Color.Transparent;

            Label hint = CreateLabel(
                "颜色条件建议选择攻击预警、技能特效或血条的小区域。",
                8.6f, FontStyle.Regular, UiTheme.TextMuted);
            hint.Location = new Point(0, 18);
            hint.Size = new Size(620, 28);
            footer.Controls.Add(hint);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.Width = 280;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 7, 0, 0);

            RoundedButton cancel = new RoundedButton();
            cancel.Text = "取消";
            cancel.Role = ButtonRole.Secondary;
            cancel.Size = new Size(92, 42);
            cancel.Margin = new Padding(8, 0, 0, 0);
            cancel.Font = UiTheme.Font(9f, true);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);

            RoundedButton save = new RoundedButton();
            save.Text = "保存规则";
            save.Role = ButtonRole.Primary;
            save.Size = new Size(132, 42);
            save.Margin = new Padding(8, 0, 0, 0);
            save.Font = UiTheme.Font(9f, true);
            save.Click += delegate { SaveRules(); };
            buttons.Controls.Add(save);
            footer.Controls.Add(buttons);
            AcceptButton = save;
            CancelButton = cancel;
            return footer;
        }

        private void AddRule()
        {
            StrategyRule rule = new StrategyRule();
            Rectangle screen = SystemInformation.VirtualScreen;
            rule.RegionLeft = screen.Left + screen.Width / 2 - 60;
            rule.RegionTop = screen.Top + screen.Height / 2 - 40;
            rule.Priority = _rules.Count == 0
                ? 100
                : Math.Min(100, _rules.Max(delegate(StrategyRule item) { return item.Priority; }) + 1);
            rule.Name = "新规则 " + (_rules.Count + 1);
            _rules.Add(rule);
            SortRules();
            RefreshRuleList(rule);
            _ruleList.SelectedItem = FindRuleItem(rule);
        }

        private void DuplicateRule()
        {
            StrategyRule source = SelectedRule();
            if (source == null)
                return;
            StrategyRule copy = source.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = source.Name + " 副本";
            copy.Priority = Math.Min(100, source.Priority + 1);
            _rules.Add(copy);
            SortRules();
            RefreshRuleList(copy);
            _ruleList.SelectedItem = FindRuleItem(copy);
        }

        private void DeleteRule()
        {
            StrategyRule rule = SelectedRule();
            if (rule == null)
                return;
            if (MessageBox.Show(this, "删除规则“" + rule.Name + "”？",
                "删除规则", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            int index = Math.Max(0, _rules.IndexOf(rule) - 1);
            _rules.Remove(rule);
            RefreshRuleList(null);
            if (_rules.Count > 0)
                _ruleList.SelectedIndex = Math.Min(index, _rules.Count - 1);
            else
                ShowEmptyEditor();
        }

        private void SaveRules()
        {
            CommitEditor();
            StrategyRule selected = SelectedRule();
            if (_capturingKey)
            {
                SetStatus("请先完成按键捕获。", true);
                return;
            }
            if (_rules.Count == 0)
            {
                Rules = new List<StrategyRule>();
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            for (int i = 0; i < _rules.Count; i++)
            {
                StrategyRule rule = _rules[i];
                rule.Normalize();
                if (rule.TriggerType == StrategyTriggerType.ColorMatch &&
                    (rule.RegionWidth <= 0 || rule.RegionHeight <= 0))
                {
                    _ruleList.SelectedItem = FindRuleItem(rule);
                    SetStatus("颜色规则“" + rule.Name + "”还没有选择检测区域。", true);
                    return;
                }
                if (rule.ActionType == StrategyActionType.KeyPress &&
                    rule.VirtualKey <= 0)
                {
                    _ruleList.SelectedItem = FindRuleItem(rule);
                    SetStatus("按键规则“" + rule.Name + "”还没有设置按键。", true);
                    return;
                }
            }

            SortRules();
            Rules = _rules.Select(delegate(StrategyRule item) { return item.Clone(); }).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void RefreshRuleList(StrategyRule selected)
        {
            _ruleList.BeginUpdate();
            try
            {
                _ruleList.Items.Clear();
                for (int i = 0; i < _rules.Count; i++)
                    _ruleList.Items.Add(new RuleItem(_rules[i]));
            }
            finally
            {
                _ruleList.EndUpdate();
            }
            if (selected != null)
                _ruleList.SelectedItem = FindRuleItem(selected);
        }

        private void DrawRuleListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _ruleList.Items.Count)
                return;
            RuleItem item = _ruleList.Items[e.Index] as RuleItem;
            if (item == null)
                return;

            StrategyRule rule = item.Rule;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush background = new SolidBrush(
                selected ? UiTheme.SurfaceHover : UiTheme.Surface))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
            }
            Color text = selected ? UiTheme.Text : UiTheme.Text;
            Color muted = selected ? UiTheme.TextMuted : UiTheme.TextFaint;
            Color stateColor = rule.Enabled ? UiTheme.Accent : UiTheme.TextFaint;
            Rectangle bounds = e.Bounds;
            using (SolidBrush state = new SolidBrush(stateColor))
                e.Graphics.FillEllipse(state, bounds.Left + 10, bounds.Top + 13, 7, 7);
            TextRenderer.DrawText(e.Graphics,
                "P" + rule.Priority + "  " + rule.Name,
                UiTheme.Font(9.2f, true),
                new Rectangle(bounds.Left + 24, bounds.Top + 4, bounds.Width - 30, 24),
                text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics,
                rule.GetTriggerText() + "  →  " + rule.GetActionText(),
                UiTheme.Font(8.1f, false),
                new Rectangle(bounds.Left + 24, bounds.Top + 27, bounds.Width - 30, 21),
                muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private StrategyRule SelectedRule()
        {
            RuleItem item = _ruleList == null ? null : _ruleList.SelectedItem as RuleItem;
            return item == null ? null : item.Rule;
        }

        private void SyncEditorFromRule()
        {
            StrategyRule rule = SelectedRule();
            if (rule == null)
            {
                ShowEmptyEditor();
                return;
            }
            _syncing = true;
            try
            {
                _editorPanel.Enabled = true;
                _nameBox.Text = rule.Name;
                _enabledBox.Checked = rule.Enabled;
                _priorityInput.Value = rule.Priority;
                _triggerTypeInput.SelectedIndex = (int)rule.TriggerType;
                _actionTypeInput.SelectedIndex = (int)rule.ActionType;
                _regionLabel.Text = FormatRegion(rule);
                Color target;
                if (!AppSettings.TryParseColor(rule.TargetColor, out target))
                    target = Color.FromArgb(255, 59, 48);
                _targetColor = target;
                StyleColorButton(_colorButton, target);
                _toleranceInput.Value = rule.ColorTolerance;
                _matchPercentInput.Value = rule.MatchPercent;
                _cooldownInput.Value = rule.CooldownMs;
                _holdInput.Value = rule.HoldMs;
                _deltaXInput.Value = rule.DeltaX;
                _deltaYInput.Value = rule.DeltaY;
                _keyBox.Text = String.IsNullOrWhiteSpace(rule.KeyName)
                    ? ((Keys)rule.VirtualKey).ToString()
                    : rule.KeyName;
                _notesBox.Text = rule.Notes ?? String.Empty;
                UpdateFieldAvailability();
            }
            finally
            {
                _syncing = false;
            }
        }

        private void CommitEditor()
        {
            if (_syncing)
                return;
            StrategyRule rule = SelectedRule();
            if (rule == null)
                return;
            rule.Name = String.IsNullOrWhiteSpace(_nameBox.Text) ? "未命名规则" : _nameBox.Text.Trim();
            rule.Enabled = _enabledBox.Checked;
            rule.Priority = (int)_priorityInput.Value;
            rule.TriggerType = _triggerTypeInput.SelectedIndex == 1
                ? StrategyTriggerType.Periodic
                : StrategyTriggerType.ColorMatch;
            rule.TargetColor = ToHex(_targetColor);
            rule.ColorTolerance = (int)_toleranceInput.Value;
            rule.MatchPercent = (int)_matchPercentInput.Value;
            rule.CooldownMs = (int)_cooldownInput.Value;
            rule.ActionType = (StrategyActionType)Math.Max(0, _actionTypeInput.SelectedIndex);
            rule.HoldMs = (int)_holdInput.Value;
            rule.DeltaX = (int)_deltaXInput.Value;
            rule.DeltaY = (int)_deltaYInput.Value;
            rule.Notes = _notesBox.Text == null ? String.Empty : _notesBox.Text.Trim();
            rule.Normalize();
            UpdateRuleListItem(rule);
            UpdateFieldAvailability();
        }

        private void UpdateRuleListItem(StrategyRule rule)
        {
            RuleItem item = FindRuleItem(rule);
            if (item == null)
                return;
            int index = _ruleList.Items.IndexOf(item);
            if (index >= 0)
                _ruleList.Invalidate(_ruleList.GetItemRectangle(index));
        }

        private RuleItem FindRuleItem(StrategyRule rule)
        {
            for (int i = 0; i < _ruleList.Items.Count; i++)
            {
                RuleItem item = _ruleList.Items[i] as RuleItem;
                if (item != null && Object.ReferenceEquals(item.Rule, rule))
                    return item;
            }
            return null;
        }

        private void UpdateFieldAvailability()
        {
            if (_syncing || _triggerTypeInput == null)
                return;
            bool colorTrigger = _triggerTypeInput.SelectedIndex == 0;
            _pickRegionButton.Enabled = colorTrigger;
            _colorButton.Enabled = colorTrigger;
            _toleranceInput.Enabled = colorTrigger;
            _matchPercentInput.Enabled = colorTrigger;

            StrategyActionType action = (StrategyActionType)Math.Max(0,
                _actionTypeInput == null ? 0 : _actionTypeInput.SelectedIndex);
            bool keyAction = action == StrategyActionType.KeyPress;
            bool relativeAction = action == StrategyActionType.RelativeMouseMove;
            _keyBox.Enabled = keyAction;
            _holdInput.Enabled = keyAction;
            _deltaXInput.Enabled = relativeAction;
            _deltaYInput.Enabled = relativeAction;
        }

        private void ShowEmptyEditor()
        {
            if (_editorPanel != null)
                _editorPanel.Enabled = false;
            if (_ruleList != null)
                _ruleList.ClearSelected();
        }

        private void PickRegion()
        {
            StrategyRule rule = SelectedRule();
            if (rule == null)
                return;
            using (RegionPickerForm picker = new RegionPickerForm())
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;
                Rectangle region = picker.SelectedRegion;
                if (region.Width < 2 || region.Height < 2)
                    return;
                rule.RegionLeft = region.Left;
                rule.RegionTop = region.Top;
                rule.RegionWidth = region.Width;
                rule.RegionHeight = region.Height;
                _targetColor = picker.SelectedColor;
                _regionLabel.Text = FormatRegion(rule);
                StyleColorButton(_colorButton, _targetColor);
                CommitEditor();
            }
        }

        private void PickColor()
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.Color = _targetColor;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _targetColor = dialog.Color;
                    StyleColorButton(_colorButton, _targetColor);
                    CommitEditor();
                }
            }
        }

        private static string FormatRegion(StrategyRule rule)
        {
            if (rule.RegionWidth <= 0 || rule.RegionHeight <= 0)
                return "未选择";
            return rule.RegionLeft + "," + rule.RegionTop + "  " +
                rule.RegionWidth + "×" + rule.RegionHeight;
        }

        private static void StyleColorButton(Button button, Color color)
        {
            if (button == null)
                return;
            button.BackColor = color;
            button.ForeColor = color.GetBrightness() > 0.55f ? Color.Black : Color.White;
            button.Text = ToHex(color);
        }

        private static string ToHex(Color color)
        {
            return "#" + color.R.ToString("X2") +
                color.G.ToString("X2") + color.B.ToString("X2");
        }

        private void SortRules()
        {
            _rules.Sort(delegate(StrategyRule first, StrategyRule second)
            {
                int priority = second.Priority.CompareTo(first.Priority);
                if (priority != 0)
                    return priority;
                return String.Compare(first.Name, second.Name,
                    StringComparison.CurrentCultureIgnoreCase);
            });
        }

        private void AddSeparator(Control parent, int top)
        {
            Panel line = new Panel();
            line.Location = new Point(18, top);
            line.Size = new Size(610, 1);
            line.BackColor = UiTheme.BorderSoft;
            parent.Controls.Add(line);
        }

        private static void AddFieldLabel(Control parent, string text, int x, int y)
        {
            Label label = CreateLabel(text, 8.6f, FontStyle.Bold, UiTheme.TextMuted);
            label.Location = new Point(x, y);
            label.Size = new Size(86, 28);
            parent.Controls.Add(label);
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

        private static TextBox CreateTextBox()
        {
            TextBox box = new TextBox();
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = UiTheme.SurfaceRaised;
            box.ForeColor = UiTheme.Text;
            box.Font = UiTheme.Font(9f, false);
            return box;
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

        private static UiNumeric CreateNumeric(int x, int y, int width,
            decimal min, decimal max, decimal increment, decimal value, int decimals)
        {
            UiNumeric input = new UiNumeric(min, max, increment, value, decimals);
            input.Location = new Point(x, y);
            input.Size = new Size(width, 34);
            input.Font = UiTheme.Mono(9.1f, true);
            return input;
        }

        private static Button CreateColorButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = UiTheme.Border;
            button.BackColor = UiTheme.SurfaceRaised;
            button.ForeColor = UiTheme.Text;
            button.Font = UiTheme.Mono(8.6f, true);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static RoundedButton CreateSmallButton(string text)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Role = ButtonRole.Secondary;
            button.Size = new Size(82, 38);
            button.Margin = new Padding(0, 0, 7, 0);
            button.Font = UiTheme.Font(8.6f, true);
            return button;
        }

        private static RoundedPanel CreateCard()
        {
            RoundedPanel card = new RoundedPanel();
            card.BackColor = UiTheme.Surface;
            card.BorderColor = UiTheme.BorderSoft;
            card.CornerRadius = 12;
            return card;
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
                Math.Min(Height, Math.Max(520, workArea.Height - 32)));
        }

        private sealed class RuleItem
        {
            public StrategyRule Rule { get; private set; }

            public RuleItem(StrategyRule rule)
            {
                Rule = rule;
            }

            public override string ToString()
            {
                return "P" + Rule.Priority + " " + Rule.Name + " · " +
                    Rule.GetTriggerText() + " → " + Rule.GetActionText();
            }
        }
    }

    internal sealed class RegionPickerForm : Form
    {
        private Point _start;
        private Rectangle _selection;
        private bool _dragging;

        public Rectangle SelectedRegion { get; private set; }
        public Color SelectedColor { get; private set; }

        public RegionPickerForm()
        {
            Text = "拾取检测区域";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Opacity = 0.28d;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            DoubleBuffered = true;
            SelectedColor = Color.Empty;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _start = e.Location;
                _selection = new Rectangle(_start, Size.Empty);
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                _selection = NormalizeRect(_start, e.Location);
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging && e.Button == MouseButtons.Left)
            {
                _dragging = false;
                _selection = NormalizeRect(_start, e.Location);
                if (_selection.Width >= 2 && _selection.Height >= 2)
                    CompleteSelection();
                else
                    Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            Rectangle hint = new Rectangle(width / 2 - 300, 24, 600, 50);
            using (SolidBrush back = new SolidBrush(Color.FromArgb(190, 20, 22, 25)))
            using (SolidBrush text = new SolidBrush(Color.White))
            using (Font font = UiTheme.Font(10f, true))
            {
                e.Graphics.FillRectangle(back, hint);
                TextRenderer.DrawText(e.Graphics,
                    "拖拽选择怪物攻击预警或技能提示区域，松开鼠标完成；Esc 取消",
                    font, Rectangle.Inflate(hint, -12, -4), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }

            if (_selection.Width > 0 && _selection.Height > 0)
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(45, UiTheme.Accent)))
                using (Pen border = new Pen(UiTheme.Accent, 2f))
                {
                    e.Graphics.FillRectangle(fill, _selection);
                    e.Graphics.DrawRectangle(border, _selection.Left, _selection.Top,
                        Math.Max(1, _selection.Width - 1), Math.Max(1, _selection.Height - 1));
                }
                string size = _selection.Width + " × " + _selection.Height;
                Point labelPoint = new Point(_selection.Right + 8, _selection.Top);
                using (Font font = UiTheme.Mono(9f, true))
                using (SolidBrush back = new SolidBrush(Color.FromArgb(210, 0, 0, 0)))
                using (SolidBrush text = new SolidBrush(Color.White))
                {
                    Size textSize = TextRenderer.MeasureText(size, font);
                    e.Graphics.FillRectangle(back, labelPoint.X, labelPoint.Y,
                        textSize.Width + 10, textSize.Height + 4);
                    e.Graphics.DrawString(size, font, text, labelPoint.X + 5, labelPoint.Y + 2);
                }
            }
        }

        private void CompleteSelection()
        {
            Rectangle selected = _selection;
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            Rectangle absoluteRegion = new Rectangle(
                selected.Left + virtualScreen.Left,
                selected.Top + virtualScreen.Top,
                selected.Width,
                selected.Height);
            Hide();
            Application.DoEvents();
            System.Threading.Thread.Sleep(70);
            try
            {
                using (Bitmap bitmap = new Bitmap(
                    Math.Max(1, selected.Width), Math.Max(1, selected.Height)))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(absoluteRegion.Left, absoluteRegion.Top, 0, 0,
                            absoluteRegion.Size, CopyPixelOperation.SourceCopy);
                    }
                    SelectedColor = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
                }
                SelectedRegion = absoluteRegion;
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                MessageBox.Show("无法读取所选屏幕区域：" + exception.Message,
                    "读取区域失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.Cancel;
            }
            Close();
        }

        private static Rectangle NormalizeRect(Point first, Point second)
        {
            return Rectangle.FromLTRB(
                Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y));
        }
    }
}
