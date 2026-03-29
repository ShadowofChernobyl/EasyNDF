using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using static EasyNDF.FileManager;
using static EasyNDF.OpenForms;

namespace EasyNDF
{
    public partial class RuleForm : Form
    {

        public RuleForm()
        {
            InitializeComponent();
            ConditionListView.KeyDown += ConditionListView_KeyDown; // Facilitate keyboard shortcuts for Conditions
            ConditionListView.MouseDoubleClick += ConfigureCondition_ContextClick; // Double-click to configure action
            ActionListView.KeyDown += ActionListView_KeyDown; // Facilitate keyboard shortcuts for Actions
            ActionListView.MouseDoubleClick += ConfigureAction_ContextClick; // Double-click to configure action
        }

        #region Condition Category

        #region Control Functions
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
        private void MoveConditionUpButton_Click(object sender, EventArgs e)
        {
            MoveConditionUp_ContextClick(sender, e);
        }
        private void MoveConditionDownButton_Click(object sender, EventArgs e)
        {
            MoveConditionDown_ContextClick(sender, e);
        }
        private void ConditionListView_MouseDown(object sender, MouseEventArgs e)
        {
            ConditionListView.ContextMenuStrip = null;
            ContextMenuStrip cm = new ContextMenuStrip();
            if (e.Button == MouseButtons.Right && ConditionListView.GetItemAt(e.X, e.Y) != null)
            {
                cm.Items.Add("Configure", null, new EventHandler(ConfigureCondition_ContextClick));
                cm.Items.Add("Duplicate", null, new EventHandler(DuplicateCondition_ContextClick));
                cm.Items.Add("Move Up", null, new EventHandler(MoveConditionUp_ContextClick));
                cm.Items.Add("Move Down", null, new EventHandler(MoveConditionDown_ContextClick));
                cm.Items.Add("Copy", null, new EventHandler(CopyCondition_ContextClick));
                cm.Items.Add("Delete", null, new EventHandler(DeleteCondition_ContextClick));
            }
            if (e.Button == MouseButtons.Right
            && ConditionListView.GetItemAt(e.X, e.Y) == null)
            {
                cm.Items.Add("Create New Condition", null, new EventHandler(AddConditionButton_Click));
                try
                {
                    // Only show Paste option if clipboard contains valid Condition JSON
                    var conditions = JsonSerializer.Deserialize<RuleEngine.Condition[]>(Clipboard.GetText());
                    if (FileManager.importedFile.ParsedFile != null) // Don't show Paste option if no NDF file has been imported, even if clipboard contains valid Condition JSON
                    {
                        foreach (RuleEngine.Condition condition in conditions)
                        {
                            if (!string.IsNullOrWhiteSpace(condition.Name)) // If at least one valid condition is found in the clipboard JSON, show the Paste option.
                            { cm.Items.Add("Paste", null, new EventHandler(PasteCondition_ContextClick)); break; }
                        }
                    }
                }
                catch (Exception) { } // If the clipboard does not contain a valid Condition JSON, do nothing
            }
            ConditionListView.ContextMenuStrip = cm;
        }
        private void ConditionListView_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up:
                    if (e.Control)
                    { MoveConditionUp_ContextClick(sender, e); e.Handled = true; }
                    else
                    { EnsureConditionSelection(ConditionListView.Items); }
                    break;

                case Keys.Down:
                    if (e.Control)
                    { MoveConditionDown_ContextClick(sender, e); e.Handled = true; }
                    else
                    { EnsureConditionSelection(ConditionListView.Items); }
                    break;

                case Keys.C:
                    if (e.Control)
                    { CopyCondition_ContextClick(sender, e); e.Handled = true; }
                    break;

                case Keys.V:
                    if (e.Control)
                    { PasteCondition_ContextClick(sender, e); e.Handled = true; }
                    break;

                case Keys.Delete:
                    DeleteCondition_ContextClick(sender, e); e.Handled = true; break;

                default: break;
            }
        }
        #endregion

        #region Context Menu Functions
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
        private void DuplicateCondition_ContextClick(object? sender, EventArgs e)
        {
            // Initialize selected item and sub-items into callable variables
            var selectedCondition = (RuleEngine.Condition)ConditionListView.SelectedItems[0].Tag!;
            ListViewItem newItem = ConditionListView.Items.Add(selectedCondition.Name);
            newItem.Tag = selectedCondition;
        }
        private void DeleteCondition_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                DialogResult dialogResult = MessageBox.Show("Are you sure you want to delete the Condition '" + ConditionListView.SelectedItems[0].Text + "'?", "Confirm Condition Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dialogResult == DialogResult.Yes)
                {
                    ConditionListView.SelectedItems[0].Remove();
                }
            }
            catch (Exception)
            { } // Ignore errors when no item is selected
        }
        private void CopyCondition_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                RuleEngine ruleEngine = new RuleEngine();
                Clipboard.SetText(ruleEngine.ConvertItemsToJSON(ConditionListView.SelectedItems));
            }
            catch (Exception)
            { } // Ignore errors when no item is selected
        }
        private void PasteCondition_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                RuleEngine ruleEngine = new RuleEngine();
                List<ListViewItem> items = ruleEngine.ConvertJSONToItems(Clipboard.GetText());

                foreach (ListViewItem item in items)
                {
                    if (item.Tag is RuleEngine.Condition condition)
                    {
                        ListViewItem newItem = ConditionListView.Items.Add(condition.Name);
                        newItem.Tag = condition;
                    }
                    else
                    {
                        MessageBox.Show($"Copied item data type did not match destination data type.",
                                       "Failed to paste",
                                       MessageBoxButtons.OK,
                                       MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception)
            { } // Ignore errors when clipboard data is invalid
        }
        private void MoveConditionUp_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                ListViewItem selectedItem = ConditionListView.SelectedItems[0];
                int selectedIndex = selectedItem.Index;
                if (selectedIndex > 0)
                {
                    ConditionListView.Items.RemoveAt(selectedIndex);
                    ConditionListView.Items.Insert(selectedIndex - 1, selectedItem);
                }
            }
            catch (Exception)
            {
                // Ignore errors when trying to move the top item up
            }
        }
        private void MoveConditionDown_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                ListViewItem selectedItem = ConditionListView.SelectedItems[0];
                int selectedIndex = selectedItem.Index;
                if (selectedIndex < ConditionListView.Items.Count - 1)
                {
                    ConditionListView.Items.RemoveAt(selectedIndex);
                    ConditionListView.Items.Insert(selectedIndex + 1, selectedItem);
                }
            }
            catch (Exception)
            {
                // Ignore errors when trying to move the top item up
            }
        }
        #endregion

        #region Automatic Functions
        private void EnsureConditionSelection(ListView.ListViewItemCollection items)
        {
            try
            { items[ConditionListView.SelectedIndices[0]].Focused = true; }
            catch (Exception)
            { } // Ignore errors when no item is selected

        }
        #endregion

        #endregion


        #region Action Category

        #region Control Functions
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
        private void MoveActionUpButton_Click(object sender, EventArgs e)
        {
            MoveActionUp_ContextClick(sender, e);
        }
        private void MoveActionDownButton_Click(object sender, EventArgs e)
        {
            MoveActionDown_ContextClick(sender, e);
        }
        private void ActionListView_MouseDown(object sender, MouseEventArgs e)
        {
            ActionListView.ContextMenuStrip = null;
            ContextMenuStrip cm = new ContextMenuStrip();
            if (e.Button == MouseButtons.Right && ActionListView.GetItemAt(e.X, e.Y) != null)
            {
                cm.Items.Add("Configure", null, new EventHandler(ConfigureAction_ContextClick));
                cm.Items.Add("Duplicate", null, new EventHandler(DuplicateAction_ContextClick));
                cm.Items.Add("Move Up", null, new EventHandler(MoveActionUp_ContextClick));
                cm.Items.Add("Move Down", null, new EventHandler(MoveActionDown_ContextClick));
                cm.Items.Add("Copy", null, new EventHandler(CopyAction_ContextClick));
                cm.Items.Add("Delete", null, new EventHandler(DeleteAction_ContextClick));
            }
            if (e.Button == MouseButtons.Right
            && ActionListView.GetItemAt(e.X, e.Y) == null)
            {
                cm.Items.Add("Create New Action", null, new EventHandler(AddActionButton_Click));
                try
                {
                    // Only show Paste option if clipboard contains valid Action JSON
                    var actions = JsonSerializer.Deserialize<RuleEngine.Action[]>(Clipboard.GetText());
                    if (FileManager.importedFile.ParsedFile != null) // Don't show Paste option if no NDF file has been imported, even if clipboard contains valid Action JSON
                    {
                        foreach (RuleEngine.Action action in actions)
                        {
                            if (!string.IsNullOrWhiteSpace(action.Name)) // If at least one valid action is found in the clipboard JSON, show the Paste option.
                            { cm.Items.Add("Paste", null, new EventHandler(PasteAction_ContextClick)); break; }
                        }
                    }
                }
                catch (Exception) { } // If the clipboard does not contain a valid Action JSON, do nothing
            }
            ActionListView.ContextMenuStrip = cm;
        }
        private void ActionListView_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up:
                    if (e.Control)
                    { MoveActionUp_ContextClick(sender, e); e.Handled = true; }
                    else
                    { EnsureActionSelection(ActionListView.Items); }
                    break;

                case Keys.Down:
                    if (e.Control)
                    { MoveActionDown_ContextClick(sender, e); e.Handled = true; }
                    else
                    { EnsureActionSelection(ActionListView.Items); }
                    break;

                case Keys.C:
                    if (e.Control)
                    { CopyAction_ContextClick(sender, e); e.Handled = true; }
                    break;

                case Keys.V:
                    if (e.Control)
                    { PasteAction_ContextClick(sender, e); e.Handled = true; }
                    break;

                case Keys.Delete:
                    DeleteAction_ContextClick(sender, e); e.Handled = true; break;

                default: break;
            }
        }
        #endregion

        #region Context Menu Functions
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
            try
            {
                DialogResult dialogResult = MessageBox.Show("Are you sure you want to delete the Action '" + ActionListView.SelectedItems[0].Text + "'?", "Confirm Action Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dialogResult == DialogResult.Yes)
                {
                    ActionListView.SelectedItems[0].Remove();
                }
            }
            catch { } // Ignore errors when no item is selected
        }
        private void CopyAction_ContextClick(object? sender, EventArgs e)
        {
            RuleEngine ruleEngine = new RuleEngine();
            Clipboard.SetText(ruleEngine.ConvertItemsToJSON(ActionListView.SelectedItems));
        }
        private void PasteAction_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                RuleEngine ruleEngine = new RuleEngine();
                List<ListViewItem> items = ruleEngine.ConvertJSONToItems(Clipboard.GetText());

                foreach (ListViewItem item in items)
                {
                    if (item.Tag is RuleEngine.Action action)
                    {
                        ListViewItem newItem = ActionListView.Items.Add(action.Name);
                        newItem.Tag = action;
                    }
                    else
                    {
                        MessageBox.Show($"Copied item data type did not match destination data type.",
                                       "Failed to paste",
                                       MessageBoxButtons.OK,
                                       MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception)
            { } // Ignore errors when clipboard data is invalid
        }
        private void MoveActionUp_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                ListViewItem selectedItem = ActionListView.SelectedItems[0];
                int selectedIndex = selectedItem.Index;
                if (selectedIndex > 0)
                {
                    ActionListView.Items.RemoveAt(selectedIndex);
                    ActionListView.Items.Insert(selectedIndex - 1, selectedItem);
                }
            }
            catch (Exception)
            {
                // Ignore errors when trying to move the top item up
            }
        }
        private void MoveActionDown_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                ListViewItem selectedItem = ActionListView.SelectedItems[0];
                int selectedIndex = selectedItem.Index;
                if (selectedIndex < ActionListView.Items.Count - 1)
                {
                    ActionListView.Items.RemoveAt(selectedIndex);
                    ActionListView.Items.Insert(selectedIndex + 1, selectedItem);
                }
            }
            catch (Exception)
            {
                // Ignore errors when trying to move the top item up
            }
        }
        #endregion

        #region Automatic Functions
        private void EnsureActionSelection(ListView.ListViewItemCollection items)
        {
            try
            { items[ActionListView.SelectedIndices[0]].Focused = true; }
            catch (Exception)
            { } // Ignore errors when no item is selected

        }
        #endregion

        #endregion

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RuleNameBox.Text))
            { MessageBox.Show("Rule must have a name.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            else
            { DialogResult = DialogResult.OK; this.Close(); }
        }
    }
}
