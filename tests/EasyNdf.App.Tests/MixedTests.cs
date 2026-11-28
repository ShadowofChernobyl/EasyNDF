using System.Text.Json.Nodes;
using EasyNdf.App.Rules;
using EasyNdf.Engine.Mods;
using Xunit;

namespace EasyNdf.App.Tests;

/// <summary>Several rules configured as one: what they share is shown, and only what was changed is written into each.</summary>
public class MixedTests
{
    private static JsonObject Rule(string name, string target, string value, bool comment)
    {
        JsonObject rule = RuleLoading.NewRule("Edit");
        rule["Name"] = name;
        var action = (JsonObject)rule["Actions"]![0]!;
        action["Target"] = target;
        action["Value"] = value;
        action["Comment"]!["Enabled"] = comment;
        return rule;
    }

    [Fact]
    public void WhatDiffersIsMarkedAndTheRestIsShared()
    {
        JsonObject merged = Mixed.Merge([Rule("A", "Cost", "1", true), Rule("B", "Cost", "2", true)]);
        Assert.True(Mixed.Is(merged["Name"]));
        Assert.Equal("Cost", (string?)merged["Actions"]![0]!["Target"]);
        Assert.True(Mixed.Is(merged["Actions"]![0]!["Value"]));
        Assert.True((bool)merged["Actions"]![0]!["Comment"]!["Enabled"]!);
    }

    [Fact]
    public void OnlyWhatWasChangedIsWritten()
    {
        JsonObject one = Rule("A", "Cost", "1", true), two = Rule("B", "Cost", "2", false);
        JsonObject merged = Mixed.Merge([one, two]);
        Dictionary<JsonObject, int> places = Mixed.RowPlaces(merged);
        var before = (JsonObject)merged.DeepClone();
        Assert.Null(Mixed.Checked(merged["Actions"]![0]!["Comment"]!["Enabled"]));
        merged["Actions"]![0]!["Comment"]!["Enabled"] = false;
        Mixed.Apply(before, merged, [one, two], places);
        Assert.False((bool)one["Actions"]![0]!["Comment"]!["Enabled"]!);
        Assert.False((bool)two["Actions"]![0]!["Comment"]!["Enabled"]!);
        Assert.Equal("A", (string?)one["Name"]);
        Assert.Equal("2", (string?)two["Actions"]![0]!["Value"]);
    }

    [Fact]
    public void ARowAddedOrDeletedIsSoInEachRule()
    {
        JsonObject one = Rule("A", "Cost", "1", true), two = Rule("B", "Cost", "2", true);
        ((JsonArray)two["Actions"]!).Add(RuleLoading.NewAction());
        JsonObject merged = Mixed.Merge([one, two]);
        Dictionary<JsonObject, int> places = Mixed.RowPlaces(merged);
        var before = (JsonObject)merged.DeepClone();
        var rows = (JsonArray)merged["Actions"]!;
        Assert.Equal(2, rows.Count);
        JsonObject added = RuleLoading.NewAction();
        added["Target"] = "Speed";
        rows.Insert(0, added);
        rows.RemoveAt(2);
        Mixed.Apply(before, merged, [one, two], places);
        foreach (JsonObject rule in new[] { one, two })
        {
            var own = (JsonArray)rule["Actions"]!;
            Assert.Equal(2, own.Count);
            Assert.Equal("Speed", (string?)own[0]!["Target"]);
        }
        Assert.Equal("1", (string?)one["Actions"]![1]!["Value"]);
        Assert.Equal("2", (string?)two["Actions"]![1]!["Value"]);
    }

    [Fact]
    public void RowsOfOneRuleAreChangedTogether()
    {
        JsonObject first = RuleLoading.NewAction(), second = RuleLoading.NewAction();
        first["Value"] = "1";
        second["Value"] = "2";
        second.Remove("Comment");
        JsonObject merged = Mixed.Merge([first, second]);
        var before = (JsonObject)merged.DeepClone();
        merged["Comment"]!["Enabled"] = true;
        Mixed.Apply(before, merged, [first, second]);
        Assert.True((bool)first["Comment"]!["Enabled"]!);
        Assert.True((bool)second["Comment"]!["Enabled"]!);
        Assert.Equal("2", (string?)second["Value"]);
    }
}
