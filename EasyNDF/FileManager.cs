using NDFParser;
using NDFParser.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace EasyNDF
{
    public class FileManager
    {
        public static NDF_File importedFile = new NDF_File(); 
        public static NDF_File exportedFile = new NDF_File();

        static string userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static readonly string presetsPath = Environment.ExpandEnvironmentVariables(userPath + "\\AppData\\Local\\EasyNDF\\RulePresets\\");

        public struct NDF_File
        {
            public NDF_File(OpenFileDialog fileDialog, FileDeclaration parsedFile)
            {
                WindowsFile = fileDialog;
                ParsedFile = parsedFile;
            }
            public FileDialog WindowsFile { get; set; }
            public FileDeclaration ParsedFile { get; set; }
        }
        public struct Descriptor
        {
            public Descriptor(bool exported, string name, string type, (string, IValue)[] properties, string parsedVersion)
            {
                Exported = exported;
                Name = name;
                Type = type;
                Properties = properties;
                ParsedVersion = parsedVersion;
            }
            public bool Exported { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public (string, IValue)[] Properties { get; set; }
            public string ParsedVersion { get; set; }
        }

        public static NDF_File ImportFile()
        {
            // Open a file prompt window for the user to select an .ndf file to import
            OpenFileDialog importFileDialog = new OpenFileDialog();
            importFileDialog.Title = "Import";
            importFileDialog.Filter = "NDF files (*.ndf)|*.ndf|All files (*.*)|*.*";

            if (importFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                importedFile.WindowsFile = importFileDialog;
                importedFile.ParsedFile = Parser.ParseFromFile(importFileDialog.FileName);
            }
            return importedFile;
        }
        public static ComboBox.ObjectCollection FetchOperandList()
        {
            ComboBox.ObjectCollection operandList = new ComboBox().Items;
            PropertyFinder finder = new PropertyFinder();
            importedFile.ParsedFile?.Accept(finder);

            foreach (var field in finder.PropertyNames)
            {
                operandList.Add(field);
            }

            return operandList;
        }
        public static ComboBox.ObjectCollection FetchPresetList()
        { 
            ComboBox.ObjectCollection presetList = new ComboBox().Items;
            if (!Directory.Exists(presetsPath))
                Directory.CreateDirectory(presetsPath);
            foreach (var file in Directory.GetFiles(presetsPath, "*.json"))
            {
                var presetName = Path.GetFileNameWithoutExtension(file);
                presetList.Add(presetName);
            }

            return presetList; 
        }
        public static void SavePresetFile(string presetName, RuleEngine.Rule[] rules)
        {
            try
            {
                Directory.CreateDirectory(presetsPath);
                
                // Remove any spaces or trailing dots (Windows forbids final spaces / dots)
                var sanitizedName = presetName.Trim().TrimEnd(' ', '.');
                var fileName = sanitizedName + ".json";

                // Prepare preset object for serialization (uses RuleEngine.Preset in RuleEngine.cs)
                var preset = new RuleEngine.Preset(sanitizedName, rules?.ToArray() ?? Array.Empty<RuleEngine.Rule>());
                var options = new JsonSerializerOptions { WriteIndented = true };
                var fullPath = Path.Combine(presetsPath, fileName);

                // Serialize to JSON and write to file
                var json = JsonSerializer.Serialize(preset, options);
                File.WriteAllText(fullPath, json);

            }
            catch (Exception ex)
            { MessageBox.Show($"Failed to save preset:\r\n{ex.Message}", "Save Preset", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        public static RuleEngine.Rule[] LoadPresetFile(string presetName)
        { 
            var preset = JsonSerializer.Deserialize<RuleEngine.Preset>(File.ReadAllText(Path.Combine(presetsPath, $"{presetName}.json")));
            if (preset.Rules != null) { return preset.Rules; }
            else { return new RuleEngine.Rule[0]; }

        }
        public static void DeletePresetFile(string presetName)
        {
            try
            {
                File.Delete(Path.Combine(presetsPath, $"{presetName}.json"));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete preset:\r\n{ex.Message}", "Delete Preset", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }        
        public static void WriteNDF(FileManager.NDF_File importedFile, FileManager.NDF_File exportedFile, RuleEngine.Rule[] rules)
        {
            RuleEngine ruleEngine = new RuleEngine();
            
            for (var i = 0; i < importedFile.ParsedFile.Declarations.Length; i++)
            {
                var declaration = importedFile.ParsedFile.Declarations[i];

                // only process AssignDeclarations with ObjectValues
                if (declaration is not AssignDeclaration assignDeclaration) 
                    continue;
                if (assignDeclaration.Value is not ObjectValue objectValue) 
                    continue;

                // Create a descriptor from the current assignDeclaration and objectValue
                Descriptor descriptor = new Descriptor(
                    assignDeclaration.Exported,
                    assignDeclaration.Name,
                    objectValue.Type,
                    objectValue.Properties,
                    Writer.WriteToString(declaration)
                );
       
                foreach (var rule in rules) 
                {
                    if (!rule.Enabled) continue; // If the rule is not enabled, continue to the next rule
                    
                    bool conditionsMet = ruleEngine.CheckConditions(descriptor, rule.Conditions, rule.LogicGate); // evaluate all conditions in the current rule

                    if (!conditionsMet) continue; // If conditions are not met, continue to the next rule

                    descriptor = ruleEngine.ApplyActions(descriptor, rule.Actions); // apply all actions in the current rule

                    importedFile.ParsedFile.Declarations[i] = new AssignDeclaration
                    (
                        descriptor.Exported,
                        descriptor.Name,
                        new ObjectValue(descriptor.Type, descriptor.Properties)
                    ); // update assignDeclaration with modified objectValue
                }
            }
            using (var file = File.CreateText(exportedFile.WindowsFile.FileName))
            {
                importedFile.ParsedFile.Accept(new Writer(file));
            }  
        }
        public static void ExportFile(RuleEngine.Rule[] rules)
        {
            // Open a save file prompt window for the user to select an .ndf file to import
            SaveFileDialog exportFileDialog = new SaveFileDialog();
            exportFileDialog.Title = "Export";
            exportFileDialog.Filter = "NDF files (*.ndf)|*.ndf|All files (*.*)|*.*";
            if (exportFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                exportedFile.WindowsFile = exportFileDialog;
                WriteNDF(importedFile, exportedFile, rules); // Write the new NDF file with the applied rules
                importedFile.ParsedFile = Parser.ParseFromFile(importedFile.WindowsFile.FileName); // Re-parse the imported file to reset any changes made during export
            }
        }
    }
}
