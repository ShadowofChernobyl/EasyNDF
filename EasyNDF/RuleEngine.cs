using NDFParser;
using NDFParser.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EasyNDF.FileManager;
using static EasyNDF.RuleEngine;

namespace EasyNDF
{
    public class RuleEngine
    {
        public struct Rule
        {
            public Rule(string name, Condition[] conditions, Action[] actions)
            {
                Name = name;
                Conditions = conditions;
                Actions = actions;
            }

            public string Name { get; set; }
            public Condition[] Conditions { get; set; } = new[] { Condition.Empty() };
            public Action[] Actions { get; set; }
        }

        public struct Condition
        {
            public Condition(string name, string operand, string @operator, string value)
            {
                Name = name;
                Operand = operand;
                Operator = @operator;
                Value = value;
            }

            public string Name { get; set; }
            public string Operand { get; set; }
            public string Operator { get; set; }
            public string Value { get; set; }

            public static Condition Empty() => new Condition("", "", "", "");

            public static Condition[] ListViewItemsToConditions(ListView.ListViewItemCollection items)
            {
                var list = new List<Condition>();
                foreach (ListViewItem item in items)
                {
                    if (item?.Tag is Condition taggedCondition)
                    {
                        // If the UI stored the original Condition in Tag, reuse it
                        list.Add(taggedCondition);
                    }
                    else
                    {
                        // Otherwise construct a minimal Condition from the item's text to avoid invalid cast exceptions
                        list.Add(new Condition(item?.Text ?? "", "", "", ""));
                    }
                }
                return list.ToArray();
            }

        }

        public struct Action
        {
            public Action(string name, string target, string @operator, string value)
            {
                Name = name;
                Target = target;
                Operator = @operator;
                Value = value;
            }

            public string Name { get; set; }
            public string Target { get; set; }
            public string Operator { get; set; }
            public string Value { get; set; }

            public static Action Empty() => new Action("", "", "", "");

            public static Action[] ListViewItemsToActions(ListView.ListViewItemCollection items)
            {
                var list = new List<Action>();
                foreach (ListViewItem item in items)
                {
                    if (item?.Tag is Action taggedAction)
                    {
                        // If the UI stored the original Action in Tag, reuse it
                        list.Add(taggedAction);
                    }
                    else
                    {
                        // Otherwise construct a minimal Action from the item's text to avoid invalid cast exceptions
                        list.Add(new Action(item?.Text ?? "", "", "", ""));
                    }
                }
                return list.ToArray();
            }
        }

        #region Conditions
        public Boolean StartsWith(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand) {
                case "Descriptor Title":
                    if (descriptor.Name.StartsWith(condition.Value)) { return true; } break;
                    
                case "Descriptor Type":
                    if (descriptor.Type.StartsWith(condition.Value)) { return true; } break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    { if (property.Item1 != condition.Operand) continue;
                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;
                       
                        if (propertyValue.ToString()!.StartsWith(condition.Value)) { return true; }
                    }
                break;
            }

            return false; // Default to false if no match found
        }
        public Boolean EndsWith(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Descriptor Title":
                    if (descriptor.Name.EndsWith(condition.Value)) { return true; }
                    break;

                case "Descriptor Type":
                    if (descriptor.Type.EndsWith(condition.Value)) { return true; }
                    break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        if (property.Item1 != condition.Operand) continue;
                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (propertyValue.ToString()!.EndsWith(condition.Value)) { return true; }
                    }
                break;
            }

            return false; // Default to false if no match found
        }
        public Boolean Contains(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Entire Descriptor":
                    if (descriptor.ParsedVersion.ToString().Contains(condition.Value)) { return true; } break;

                case "Descriptor Title":
                    if (descriptor.Name.Contains(condition.Value)) { return true; } break;

                case "Descriptor Type":
                    if (descriptor.Type.Contains(condition.Value)) { return true; } break;
                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        if (property.Item1 != condition.Operand) continue;
                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (propertyValue.ToString()!.Contains(condition.Value)) { return true; }
                    }
                break;
            }

            return false; // Default to false if no match found
        }
        public Boolean DoesNotContain(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Entire Descriptor":
                    if (!descriptor.ParsedVersion.Contains(condition.Value)) { return true; }
                    break;

                case "Descriptor Title":
                    if (!descriptor.Name.Contains(condition.Value)) { return true; }
                    break;

                case "Descriptor Type":
                    if (!descriptor.Type.Contains(condition.Value)) { return true; }
                    break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        if (property.Item1 != condition.Operand) continue;
                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (!propertyValue.ToString()!.Contains(condition.Value)) { return true; }
                    }
                    break;
            }

            return false; // Default to false if no match found
        }
        public Boolean IsEqualTo(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Descriptor Title":
                    if (descriptor.Name.Equals(condition.Value)) { return true; }
                    break;

                case "Descriptor Type":
                    if (descriptor.Type.Equals(condition.Value)) { return true; }
                    break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        if (property.Item1 != condition.Operand) continue;
                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (propertyValue.ToString()!.Equals(condition.Value)) { return true; }
                    }
                    break;
            }

            return false; // Default to false if no match found
        }
        public Boolean IsNotEqualTo(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Descriptor Title":
                    if (!descriptor.Name.Equals(condition.Value)) { return true; }
                    break;

                case "Descriptor Type":
                    if (!descriptor.Type.Equals(condition.Value)) { return true; }
                    break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        if (property.Item1 != condition.Operand) continue;
                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (!propertyValue.ToString()!.Equals(condition.Value)) { return true; }
                    }
                    break;
            }

            return false; // Default to false if no match found
        }
        public Boolean IsLessThan(FileManager.Descriptor descriptor, Condition condition)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
               if (property.Item1 != condition.Operand) continue;
               var propertyName = property.Item1;
               var propertyValue = property.Item2;

               if (Convert.ToDouble(propertyValue) < Convert.ToDouble(condition.Value)) { return true; }

            }

            return false; // Default to false if no match found
        }
        public Boolean IsLessThanOrEqualTo(FileManager.Descriptor descriptor, Condition condition)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                if (property.Item1 != condition.Operand) continue;
                var propertyName = property.Item1;
                var propertyValue = property.Item2;

                if (Convert.ToDouble(propertyValue) <= Convert.ToDouble(condition.Value)) { return true; }

            }

            return false; // Default to false if no match found
        }
        public Boolean IsGreaterThan(FileManager.Descriptor descriptor, Condition condition)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                if (property.Item1 != condition.Operand) continue;
                var propertyName = property.Item1;
                var propertyValue = property.Item2;

                if (Convert.ToDouble(propertyValue) > Convert.ToDouble(condition.Value)) { return true; }

            }

            return false; // Default to false if no match found
        }
        public Boolean IsGreaterThanOrEqualTo(FileManager.Descriptor descriptor, Condition condition)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                if (property.Item1 != condition.Operand) continue;
                var propertyName = property.Item1;
                var propertyValue = property.Item2;

                if (Convert.ToDouble(propertyValue) >= Convert.ToDouble(condition.Value)) { return true; }

            }

            return false; // Default to false if no match found
        }
        public Boolean ContainsAnyOf(FileManager.Descriptor descriptor, Condition condition)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                if (property.Item1 != condition.Operand) continue;
                var propertyName = property.Item1;
                var propertyValue = property.Item2;

                foreach (var value in propertyValue.ToString()!.Split(',').Select(v => v.Trim()))
                { if (propertyValue.ToString()!.Contains(value)) { return true; }
                }
            }

            return false; // Default to false if no match found
        }
        public Boolean ContainsAllOf(Condition condition, string parsedValue)
        {
            var values = condition.Value.Split(',').Select(v => v.Trim());
            return values.All(v => parsedValue.Contains(v));
        }
        #endregion

        #region Actions
        public (string, IValue) Add(Action action, (string, IValue) property)
        {
            return (property.Item1, new NumericLiteral((Convert.ToDouble(((NumericLiteral)property.Item2).Value) + Convert.ToDouble(action.Value)).ToString()));
        }
        public (string, IValue) Subtract(Action action, (string, IValue) property)
        {
            return (property.Item1, new NumericLiteral((Convert.ToDouble(((NumericLiteral)property.Item2).Value) - Convert.ToDouble(action.Value)).ToString()));
        }
        public (string, IValue) Multiply(Action action, (string, IValue) property)
        {
            return (property.Item1, new NumericLiteral((Convert.ToDouble(((NumericLiteral)property.Item2).Value) * Convert.ToDouble(action.Value)).ToString()));
        }
        public (string, IValue) Divide(Action action, (string, IValue) property)
        {
            return (property.Item1, new NumericLiteral((Convert.ToDouble(((NumericLiteral)property.Item2).Value) / Convert.ToDouble(action.Value)).ToString()));
        }
        public (string, IValue) SetTo(Action action, (string, IValue) property)
        {           
            return (property.Item1, new StringLiteral(action.Value));
        }
        public (string, IValue) Insert(Action action, (string, IValue) property)
        {
            // insert action.Value at the array index specified within action.Value "index:value". Accept "LastIndex" and "FirstIndex" keywords
            string[] splitString = (action.Value.Split(new char[] { ':' }, 2));
            int index;
            string stringValue = splitString[1];
            
            if (splitString[0] == "LastIndex")
            { index = ((ArrayValue)property.Item2).Values.Length; }  // Set index to last position
            else if (splitString[0] == "FirstIndex")
            { index = 0; }  // Set index to first position
            else
            { index = Convert.ToInt32(splitString[0]); } // Convert index from string to int

            var currentArray = ((ArrayValue)property.Item2).Values.ToList(); // Convert existing array to list
            currentArray.Insert(index, new StringLiteral(stringValue)); // Insert the new value at the specified index
            return (property.Item1, new ArrayValue(currentArray.ToArray())); // Return updated array

        }
        public (string, IValue) Append(Action action, (string, IValue) property)
        {
            return (property.Item1, new StringLiteral(property.Item2 + action.Value));
        }   
        public (string, IValue) Prepend(Action action, (string, IValue) property)
        {
            return (property.Item1, new StringLiteral(action.Value + property.Item2));
        }
        #endregion
    }
}
