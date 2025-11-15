namespace EasyNDF
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            ListViewGroup listViewGroup1 = new ListViewGroup("ListViewGroup", HorizontalAlignment.Left);
            NewRuleButton = new Button();
            HelpMenuButton = new Button();
            ImportButton = new Button();
            ExportButton = new Button();
            PresetComboBox = new ComboBox();
            SettingsButton = new Button();
            PresetLabel = new Label();
            AddPresetButton = new Button();
            RuleListView = new ListView();
            ruleNameHeader = new ColumnHeader();
            label1 = new Label();
            FileNameBox = new TextBox();
            MainFormToolTip = new ToolTip(components);
            SuspendLayout();
            // 
            // NewRuleButton
            // 
            NewRuleButton.BackColor = Color.SteelBlue;
            NewRuleButton.FlatStyle = FlatStyle.Flat;
            NewRuleButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            NewRuleButton.Location = new Point(321, 12);
            NewRuleButton.Name = "NewRuleButton";
            NewRuleButton.Size = new Size(105, 33);
            NewRuleButton.TabIndex = 1;
            NewRuleButton.Text = "New Rule";
            NewRuleButton.UseVisualStyleBackColor = false;
            NewRuleButton.Click += NewRuleButton_Click;
            // 
            // HelpMenuButton
            // 
            HelpMenuButton.BackColor = Color.SteelBlue;
            HelpMenuButton.FlatStyle = FlatStyle.Flat;
            HelpMenuButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            HelpMenuButton.Location = new Point(280, 12);
            HelpMenuButton.Name = "HelpMenuButton";
            HelpMenuButton.Size = new Size(35, 33);
            HelpMenuButton.TabIndex = 2;
            HelpMenuButton.Text = "?";
            HelpMenuButton.UseVisualStyleBackColor = false;
            // 
            // ImportButton
            // 
            ImportButton.BackColor = Color.YellowGreen;
            ImportButton.FlatStyle = FlatStyle.Flat;
            ImportButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            ImportButton.Location = new Point(11, 12);
            ImportButton.Name = "ImportButton";
            ImportButton.Size = new Size(84, 33);
            ImportButton.TabIndex = 3;
            ImportButton.Text = "Import";
            ImportButton.UseVisualStyleBackColor = false;
            ImportButton.Click += ImportButton_Click;
            // 
            // ExportButton
            // 
            ExportButton.BackColor = Color.SteelBlue;
            ExportButton.FlatStyle = FlatStyle.Flat;
            ExportButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            ExportButton.Location = new Point(101, 12);
            ExportButton.Name = "ExportButton";
            ExportButton.Size = new Size(78, 33);
            ExportButton.TabIndex = 4;
            ExportButton.Text = "Export";
            ExportButton.UseVisualStyleBackColor = false;
            ExportButton.Click += ExportButton_Click;
            // 
            // PresetComboBox
            // 
            PresetComboBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            PresetComboBox.BackColor = Color.FromArgb(15, 15, 15);
            PresetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            PresetComboBox.FlatStyle = FlatStyle.Popup;
            PresetComboBox.Font = new Font("Microsoft Sans Serif", 12F);
            PresetComboBox.ForeColor = Color.Linen;
            PresetComboBox.FormattingEnabled = true;
            PresetComboBox.Location = new Point(12, 390);
            PresetComboBox.Name = "PresetComboBox";
            PresetComboBox.Size = new Size(378, 28);
            PresetComboBox.TabIndex = 5;
            PresetComboBox.MouseDown += PresetComboBox_MouseDown;
            // 
            // SettingsButton
            // 
            SettingsButton.BackColor = Color.SteelBlue;
            SettingsButton.FlatStyle = FlatStyle.Flat;
            SettingsButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            SettingsButton.Location = new Point(185, 12);
            SettingsButton.Name = "SettingsButton";
            SettingsButton.Size = new Size(89, 33);
            SettingsButton.TabIndex = 7;
            SettingsButton.Text = "Settings";
            SettingsButton.UseVisualStyleBackColor = false;
            SettingsButton.Click += SettingsButton_Click;
            // 
            // PresetLabel
            // 
            PresetLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            PresetLabel.AutoSize = true;
            PresetLabel.Font = new Font("Verdana", 12F);
            PresetLabel.ForeColor = Color.Linen;
            PresetLabel.Location = new Point(12, 369);
            PresetLabel.Name = "PresetLabel";
            PresetLabel.Size = new Size(118, 18);
            PresetLabel.TabIndex = 8;
            PresetLabel.Text = "Rule Presets:";
            // 
            // AddPresetButton
            // 
            AddPresetButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            AddPresetButton.BackColor = Color.SteelBlue;
            AddPresetButton.FlatStyle = FlatStyle.Flat;
            AddPresetButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            AddPresetButton.Location = new Point(396, 389);
            AddPresetButton.Name = "AddPresetButton";
            AddPresetButton.Size = new Size(30, 30);
            AddPresetButton.TabIndex = 9;
            AddPresetButton.Text = "+";
            AddPresetButton.UseVisualStyleBackColor = false;
            AddPresetButton.Click += AddPresetButton_Click;
            // 
            // RuleListView
            // 
            RuleListView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            RuleListView.AutoArrange = false;
            RuleListView.BackColor = Color.FromArgb(15, 15, 15);
            RuleListView.BorderStyle = BorderStyle.FixedSingle;
            RuleListView.CheckBoxes = true;
            RuleListView.Columns.AddRange(new ColumnHeader[] { ruleNameHeader });
            RuleListView.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            RuleListView.ForeColor = Color.Linen;
            RuleListView.FullRowSelect = true;
            listViewGroup1.CollapsedState = ListViewGroupCollapsedState.Expanded;
            listViewGroup1.Header = "ListViewGroup";
            listViewGroup1.Name = "listViewGroup1";
            RuleListView.Groups.AddRange(new ListViewGroup[] { listViewGroup1 });
            RuleListView.HeaderStyle = ColumnHeaderStyle.None;
            RuleListView.Location = new Point(12, 86);
            RuleListView.Name = "RuleListView";
            RuleListView.ShowGroups = false;
            RuleListView.Size = new Size(413, 266);
            RuleListView.TabIndex = 10;
            RuleListView.UseCompatibleStateImageBehavior = false;
            RuleListView.View = View.Details;
            RuleListView.SizeChanged += RuleListView_SizeChanged;
            RuleListView.MouseDown += RuleListView_MouseDown;
            // 
            // ruleNameHeader
            // 
            ruleNameHeader.Text = "Rules";
            ruleNameHeader.Width = 300;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Verdana", 12F);
            label1.ForeColor = Color.Linen;
            label1.Location = new Point(12, 61);
            label1.Name = "label1";
            label1.Size = new Size(60, 18);
            label1.TabIndex = 11;
            label1.Text = "Rules:";
            // 
            // FileNameBox
            // 
            FileNameBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            FileNameBox.BackColor = Color.FromArgb(30, 30, 30);
            FileNameBox.BorderStyle = BorderStyle.None;
            FileNameBox.Font = new Font("Microsoft Sans Serif", 12F);
            FileNameBox.ForeColor = Color.Linen;
            FileNameBox.Location = new Point(78, 61);
            FileNameBox.Name = "FileNameBox";
            FileNameBox.ReadOnly = true;
            FileNameBox.RightToLeft = RightToLeft.Yes;
            FileNameBox.ShortcutsEnabled = false;
            FileNameBox.Size = new Size(349, 19);
            FileNameBox.TabIndex = 13;
            FileNameBox.TabStop = false;
            // 
            // MainForm
            // 
            AcceptButton = NewRuleButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(30, 30, 30);
            ClientSize = new Size(437, 430);
            Controls.Add(FileNameBox);
            Controls.Add(label1);
            Controls.Add(RuleListView);
            Controls.Add(AddPresetButton);
            Controls.Add(PresetLabel);
            Controls.Add(SettingsButton);
            Controls.Add(PresetComboBox);
            Controls.Add(ExportButton);
            Controls.Add(ImportButton);
            Controls.Add(HelpMenuButton);
            Controls.Add(NewRuleButton);
            HelpButton = true;
            MinimumSize = new Size(453, 350);
            Name = "MainForm";
            RightToLeft = RightToLeft.No;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "EasyNDF";
            Load += MainForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Button NewRuleButton;
        private Button HelpMenuButton;
        private Button ImportButton;
        private Button ExportButton;
        private ComboBox PresetComboBox;
        private Button SettingsButton;
        private Label PresetLabel;
        private Button AddPresetButton;
        private ListView RuleListView;
        private ColumnHeader ruleNameHeader;
        private Label label1;
        public TextBox FileNameBox;
        private ToolTip MainFormToolTip;
    }
}
