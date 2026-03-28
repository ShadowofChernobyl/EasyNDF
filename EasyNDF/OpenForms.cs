using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using static Antlr4.Runtime.Atn.SemanticContext;
using static EasyNDF.OpenForms;
using static EasyNDF.RuleEngine;
using static System.Windows.Forms.Design.AxImporter;

namespace EasyNDF
{

    internal class OpenForms
    {
        public RuleEngine.Rule OpenRuleForm(RuleEngine.Rule rule)
        {
            RuleForm ruleForm = new RuleForm();
            

            // Populate controls with saved rule information.
            ruleForm.RuleNameBox.Text = rule.Name;
            ruleForm.FilterComboBox.Text = rule.LogicGate;
            ruleForm.EnableCheckBox.Checked = rule.Enabled;

            foreach (var condition in rule.Conditions) 
            {
                if (condition.Name == "") continue;
                var newItem = ruleForm.ConditionListView.Items.Add(condition.Name);
                newItem.Tag = condition;
            };
            
            foreach (var action in rule.Actions) 
            {
                if (action.Name == "") continue;
                var newItem = ruleForm.ActionListView.Items.Add(action.Name);
                newItem.Tag = action;
            };
            

            DialogResult result = ruleForm.ShowDialog();
            // If user clicks OK, save information; Otherwise, do nothing.
            if (result == DialogResult.OK)
            {
                rule.Name = ruleForm.RuleNameBox.Text;
                rule.Conditions = RuleEngine.Condition.ListViewItemsToConditions(ruleForm.ConditionListView.Items);
                rule.Actions = RuleEngine.Action.ListViewItemsToActions(ruleForm.ActionListView.Items);
                rule.Enabled = ruleForm.EnableCheckBox.Checked;
                rule.LogicGate = ruleForm.FilterComboBox.Text;
            }
            else
            {

            }

            ruleForm.Dispose();
            return rule;
        }
        public RuleEngine.Condition OpenConditionForm(RuleEngine.Condition condition)
        {
            ConditionForm conditionForm = new ConditionForm();
            conditionForm.OperandComboBox.Items.AddRange(FileManager.FetchOperandList().Cast<string>().ToArray());

            if (condition.Name != null)
                conditionForm.ConditionNameBox.Text = condition.Name;

            if (condition.Operand != null)
                conditionForm.OperandComboBox.Text = condition.Operand;

            if (condition.Operator != null)
                conditionForm.OperatorComboBox.Text = condition.Operator;

            if (condition.Value != null)
                conditionForm.ValueTextBox.Text = condition.Value;

            if (condition.CustomOperand != null)
                conditionForm.OverrideTextBox.Text = condition.CustomOperand;

            if (condition.ManualOverride)
            {
                conditionForm.OverrideCheckBox.Checked = true;
            }

            DialogResult result = conditionForm.ShowDialog();

            if (result == DialogResult.OK) 
            {
                condition.Name = conditionForm.ConditionNameBox.Text;
                condition.Operand = conditionForm.OperandComboBox.Text;
                condition.Operator = conditionForm.OperatorComboBox.Text;
                condition.Value = conditionForm.ValueTextBox.Text;
                condition.ManualOverride = conditionForm.OverrideCheckBox.Checked;
                condition.CustomOperand = conditionForm.OverrideTextBox.Text;
            }

            conditionForm.Dispose();
            return condition;

            
        }
        public RuleEngine.Action OpenActionForm(RuleEngine.Action action)
        {
            ActionForm actionForm = new ActionForm();
            actionForm.TargetComboBox.Items.AddRange(FileManager.FetchOperandList().Cast<string>().ToArray());

            if (action.Name != null)
                actionForm.ActionNameBox.Text = action.Name;

            if (action.Target != null)
                actionForm.TargetComboBox.Text = action.Target;

            if (action.Operator != null)
                actionForm.OperatorComboBox.Text = action.Operator;

            if (action.Value != null)
                actionForm.ValueTextBox.Text = action.Value;

            if (action.CustomTarget != null)
                actionForm.OverrideTextBox.Text = action.CustomTarget;

            if (action.ManualOverride)
            {
                actionForm.OverrideCheckBox.Checked = true;
            }

            if (action.Comment.Text != null && action.Comment.Text != "")
            {
                actionForm.CommentCheckBox.Checked = action.Comment.Enabled;
                actionForm.CommentTextBox.Text = action.Comment.Text;
                actionForm.CommentTimestampCheckBox.Checked = action.Comment.IncludeTimestamp;
                actionForm.CommentOriginalValueCheckBox.Checked = action.Comment.IncludeOriginalValue;
            }

            DialogResult result = actionForm.ShowDialog();

            if (result == DialogResult.OK)
            {
                action.Name = actionForm.ActionNameBox.Text;
                action.Target = actionForm.TargetComboBox.Text;
                action.Operator = actionForm.OperatorComboBox.Text;
                action.Value = actionForm.ValueTextBox.Text;
                action.ManualOverride = actionForm.OverrideCheckBox.Checked;
                action.CustomTarget = actionForm.OverrideTextBox.Text;

                // Fix for CS1612: Work with a local copy of Comment, then assign it back
                var comment = action.Comment;
                comment.Text = actionForm.CommentTextBox.Text;
                comment.Enabled = actionForm.CommentCheckBox.Checked;
                comment.IncludeTimestamp = actionForm.CommentTimestampCheckBox.Checked;
                comment.IncludeOriginalValue = actionForm.CommentOriginalValueCheckBox.Checked;
                action.Comment = comment;
            }

            actionForm.Dispose();
            return action;
        }
        public string OpenSettingsForm()
        {
            SettingsForm settingsForm = new SettingsForm();
            DialogResult result = settingsForm.ShowDialog();
            if (result == DialogResult.OK)
            {
                // TO DO
            }
            settingsForm.Dispose();
            return "";
        }
        public String OpenTextEntryForm(string windowName, string labelText, string textBoxText, string placeHolderText, bool isForPresetNaming)
        {
            TextEntryForm textEntryForm = new TextEntryForm();
            textEntryForm.Text = windowName;
            textEntryForm.TextBox.Text = textBoxText;
            textEntryForm.TextEntryLabel.Text = labelText;
            textEntryForm.TextBox.PlaceholderText = placeHolderText;
            textEntryForm.isForPresetNaming = isForPresetNaming;
            DialogResult result = textEntryForm.ShowDialog();
            

            string text = textEntryForm.TextBox.Text;

            if (result == DialogResult.OK)
            {
                textEntryForm.Dispose();
                return text;
            }
            else
            {
                textEntryForm.Dispose();
                return "";
            }
        }
    }
}
