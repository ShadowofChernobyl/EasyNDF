using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static EasyNDF.OpenForms;
using static EasyNDF.FileManager;

namespace EasyNDF
{
    public partial class RuleForm : Form
    {
        public RuleForm()
        {
            InitializeComponent();
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {

        }


        #region ConditionControls
        private void AddConditionButton_Click(object? sender, EventArgs e)
        {
            OpenForms openForms = new OpenForms();
            RuleEngine.Condition condition = openForms.OpenConditionForm(RuleEngine.Condition.Empty());

            if (!string.IsNullOrWhiteSpace(condition.Name))
            {
                ListViewItem newItem = ConditionListView.Items.Add(condition.Name);
                newItem.Tag = condition;
            }
        }

        private void ConditionListView_MouseDown(object sender, MouseEventArgs e)
        {
            ConditionListView.ContextMenuStrip = null;
            ContextMenuStrip cm = new ContextMenuStrip();
            if (e.Button == MouseButtons.Right && ConditionListView.GetItemAt(e.X, e.Y) != null)
            {
                cm.Items.Add("Configure", null, new EventHandler(ConfigureCondition_ContextClick));
                cm.Items.Add("Duplicate", null, new EventHandler(DuplicateCondition_ContextClick));
                cm.Items.Add("Delete", null, new EventHandler(DeleteCondition_ContextClick));
            }
            if (e.Button == MouseButtons.Right
            && ConditionListView.GetItemAt(e.X, e.Y) == null)
            {
                cm.Items.Add("Create New Condition", null, new EventHandler(AddConditionButton_Click));
            }
            ConditionListView.ContextMenuStrip = cm;
        }

        private void ConfigureCondition_ContextClick(object? sender, EventArgs e)
        {
            // Initialize selected item into a callable variable
            var selectedCondition = (RuleEngine.Condition)ConditionListView.SelectedItems[0].Tag!;

            OpenForms openForms = new OpenForms();
            RuleEngine.Condition condition = openForms.OpenConditionForm(selectedCondition);
            // Save the information returned by OpenConditionForm() as long as the condition has a name
            if (!string.IsNullOrWhiteSpace(condition.Name))
            {
                ConditionListView.SelectedItems[0].Text = condition.Name;
                ConditionListView.SelectedItems[0].Tag = condition;
            }
        }

        public void DuplicateCondition_ContextClick(object? sender, EventArgs e)
        {
            // Initialize selected item and sub-items into callable variables
            var selectedCondition = (RuleEngine.Condition)ConditionListView.SelectedItems[0].Tag!;
            ListViewItem newItem = ConditionListView.Items.Add(selectedCondition.Name);
            newItem.Tag = selectedCondition;

        }

        private void DeleteCondition_ContextClick(object? sender, EventArgs e)
        {
            ConditionListView.SelectedItems[0].Remove();
        }
        #endregion


        #region ActionControls
        private void AddActionButton_Click(object? sender, EventArgs e)
        {
            OpenForms openForms = new OpenForms();
            RuleEngine.Action action = openForms.OpenActionForm(RuleEngine.Action.Empty());

            if (!string.IsNullOrWhiteSpace(action.Name))
            {
                ListViewItem newItem = ActionListView.Items.Add(action.Name);
                newItem.Tag = action;
            }
        }

        private void ActionListView_MouseDown(object sender, MouseEventArgs e)
        {
            ActionListView.ContextMenuStrip = null;
            ContextMenuStrip cm = new ContextMenuStrip();
            if (e.Button == MouseButtons.Right && ActionListView.GetItemAt(e.X, e.Y) != null)
            {
                cm.Items.Add("Configure", null, new EventHandler(ConfigureAction_ContextClick));
                cm.Items.Add("Duplicate", null, new EventHandler(DuplicateAction_ContextClick));
                cm.Items.Add("Delete", null, new EventHandler(DeleteAction_ContextClick));
            }
            if (e.Button == MouseButtons.Right
            && ActionListView.GetItemAt(e.X, e.Y) == null)
            {
                cm.Items.Add("Create New Action", null, new EventHandler(AddActionButton_Click));
            }
            ActionListView.ContextMenuStrip = cm;
        }

        private void ConfigureAction_ContextClick(object? sender, EventArgs e)
        {
            // Initialize selected item into a callable variable
            var selectedAction = (RuleEngine.Action)ActionListView.SelectedItems[0].Tag!;

            OpenForms openForms = new OpenForms();
            RuleEngine.Action action = openForms.OpenActionForm(selectedAction);
            // Save the information returned by OpenActionForm() as long as the action has a name
            if (!string.IsNullOrWhiteSpace(action.Name))
            {
                ActionListView.SelectedItems[0].Text = action.Name;
                ActionListView.SelectedItems[0].Tag = action;
            }
        }

        private void DuplicateAction_ContextClick(object? sender, EventArgs e)
        {
            // Initialize selected item and sub-items into callable variables
            var selectedAction = (RuleEngine.Action)ActionListView.SelectedItems[0].Tag!;
            ListViewItem newItem = ActionListView.Items.Add(selectedAction.Name);
            newItem.Tag = selectedAction;
        }

        private void DeleteAction_ContextClick(object? sender, EventArgs e)
        {
            ActionListView.SelectedItems[0].Remove();
        }
        #endregion

        // Apply
        private void ApplyButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
