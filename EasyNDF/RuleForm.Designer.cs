namespace EasyNDF
{
    partial class RuleForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            ListViewGroup listViewGroup1 = new ListViewGroup("ListViewGroup", HorizontalAlignment.Left);
            ListViewGroup listViewGroup2 = new ListViewGroup("ListViewGroup", HorizontalAlignment.Left);
            RuleNameBox = new TextBox();
            RuleNameLabel = new Label();
            ApplyButton = new Button();
            AddActionButton = new Button();
            AddConditionButton = new Button();
            label1 = new Label();
            label2 = new Label();
            ConditionListView = new ListView();
            ruleNameHeader = new ColumnHeader();
            ActionListView = new ListView();
            columnHeader1 = new ColumnHeader();
            SuspendLayout();
            // 
            // RuleNameBox
            // 
            RuleNameBox.BackColor = Color.FromArgb(15, 15, 15);
            RuleNameBox.BorderStyle = BorderStyle.FixedSingle;
            RuleNameBox.Font = new Font("Microsoft Sans Serif", 12F);
            RuleNameBox.ForeColor = Color.Linen;
            RuleNameBox.Location = new Point(11, 39);
            RuleNameBox.Name = "RuleNameBox";
            RuleNameBox.PlaceholderText = "Enter rule name";
            RuleNameBox.Size = new Size(359, 26);
            RuleNameBox.TabIndex = 0;
            // 
            // RuleNameLabel
            // 
            RuleNameLabel.AutoSize = true;
            RuleNameLabel.Font = new Font("Verdana", 12F);
            RuleNameLabel.ForeColor = Color.Linen;
            RuleNameLabel.Location = new Point(11, 18);
            RuleNameLabel.Name = "RuleNameLabel";
            RuleNameLabel.Size = new Size(62, 18);
            RuleNameLabel.TabIndex = 1;
            RuleNameLabel.Text = "Name:";
            // 
            // ApplyButton
            // 
            ApplyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ApplyButton.BackColor = Color.YellowGreen;
            ApplyButton.FlatStyle = FlatStyle.Flat;
            ApplyButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            ApplyButton.Location = new Point(280, 555);
            ApplyButton.Name = "ApplyButton";
            ApplyButton.Size = new Size(91, 32);
            ApplyButton.TabIndex = 5;
            ApplyButton.Text = "Apply";
            ApplyButton.UseVisualStyleBackColor = false;
            ApplyButton.Click += ApplyButton_Click;
            // 
            // AddActionButton
            // 
            AddActionButton.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            AddActionButton.BackColor = Color.YellowGreen;
            AddActionButton.FlatStyle = FlatStyle.Flat;
            AddActionButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            AddActionButton.Location = new Point(343, 314);
            AddActionButton.Name = "AddActionButton";
            AddActionButton.Size = new Size(28, 28);
            AddActionButton.TabIndex = 3;
            AddActionButton.Text = "+";
            AddActionButton.UseVisualStyleBackColor = false;
            AddActionButton.Click += AddActionButton_Click;
            // 
            // AddConditionButton
            // 
            AddConditionButton.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            AddConditionButton.BackColor = Color.YellowGreen;
            AddConditionButton.FlatStyle = FlatStyle.Flat;
            AddConditionButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            AddConditionButton.Location = new Point(343, 72);
            AddConditionButton.Name = "AddConditionButton";
            AddConditionButton.Size = new Size(28, 28);
            AddConditionButton.TabIndex = 1;
            AddConditionButton.Text = "+";
            AddConditionButton.UseVisualStyleBackColor = false;
            AddConditionButton.Click += AddConditionButton_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Verdana", 12F);
            label1.ForeColor = Color.Linen;
            label1.Location = new Point(11, 82);
            label1.Name = "label1";
            label1.Size = new Size(102, 18);
            label1.TabIndex = 12;
            label1.Text = "Conditions:";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Verdana", 12F);
            label2.ForeColor = Color.Linen;
            label2.Location = new Point(11, 324);
            label2.Name = "label2";
            label2.Size = new Size(75, 18);
            label2.TabIndex = 13;
            label2.Text = "Actions:";
            // 
            // ConditionListView
            // 
            ConditionListView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ConditionListView.AutoArrange = false;
            ConditionListView.BackColor = Color.FromArgb(15, 15, 15);
            ConditionListView.BorderStyle = BorderStyle.FixedSingle;
            ConditionListView.Columns.AddRange(new ColumnHeader[] { ruleNameHeader });
            ConditionListView.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            ConditionListView.ForeColor = Color.Linen;
            ConditionListView.FullRowSelect = true;
            listViewGroup1.CollapsedState = ListViewGroupCollapsedState.Expanded;
            listViewGroup1.Header = "ListViewGroup";
            listViewGroup1.Name = "listViewGroup1";
            ConditionListView.Groups.AddRange(new ListViewGroup[] { listViewGroup1 });
            ConditionListView.HeaderStyle = ColumnHeaderStyle.None;
            ConditionListView.Location = new Point(9, 106);
            ConditionListView.Name = "ConditionListView";
            ConditionListView.ShowGroups = false;
            ConditionListView.Size = new Size(361, 202);
            ConditionListView.TabIndex = 2;
            ConditionListView.UseCompatibleStateImageBehavior = false;
            ConditionListView.View = View.Details;
            ConditionListView.MouseDown += ConditionListView_MouseDown;
            // 
            // ruleNameHeader
            // 
            ruleNameHeader.Text = "Rules";
            ruleNameHeader.Width = 300;
            // 
            // ActionListView
            // 
            ActionListView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ActionListView.AutoArrange = false;
            ActionListView.BackColor = Color.FromArgb(15, 15, 15);
            ActionListView.BorderStyle = BorderStyle.FixedSingle;
            ActionListView.Columns.AddRange(new ColumnHeader[] { columnHeader1 });
            ActionListView.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            ActionListView.ForeColor = Color.Linen;
            ActionListView.FullRowSelect = true;
            listViewGroup2.CollapsedState = ListViewGroupCollapsedState.Expanded;
            listViewGroup2.Header = "ListViewGroup";
            listViewGroup2.Name = "listViewGroup1";
            ActionListView.Groups.AddRange(new ListViewGroup[] { listViewGroup2 });
            ActionListView.HeaderStyle = ColumnHeaderStyle.None;
            ActionListView.Location = new Point(11, 348);
            ActionListView.Name = "ActionListView";
            ActionListView.ShowGroups = false;
            ActionListView.Size = new Size(360, 202);
            ActionListView.TabIndex = 4;
            ActionListView.UseCompatibleStateImageBehavior = false;
            ActionListView.View = View.Details;
            ActionListView.MouseDown += ActionListView_MouseDown;
            // 
            // columnHeader1
            // 
            columnHeader1.Text = "Rules";
            columnHeader1.Width = 300;
            // 
            // RuleForm
            // 
            AcceptButton = ApplyButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(30, 30, 30);
            ClientSize = new Size(384, 606);
            Controls.Add(ActionListView);
            Controls.Add(ConditionListView);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(AddConditionButton);
            Controls.Add(AddActionButton);
            Controls.Add(ApplyButton);
            Controls.Add(RuleNameLabel);
            Controls.Add(RuleNameBox);
            MaximumSize = new Size(800, 645);
            MinimumSize = new Size(400, 645);
            Name = "RuleForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Rule Configuration";
            Load += ConfigForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        public TextBox RuleNameBox;
        public Label RuleNameLabel;
        public Button ApplyButton;
        private Button AddActionButton;
        private Button AddConditionButton;
        public Label label1;
        public Label label2;
        private ColumnHeader ruleNameHeader;
        private ColumnHeader columnHeader1;
        public ListView ConditionListView;
        public ListView ActionListView;
    }
}