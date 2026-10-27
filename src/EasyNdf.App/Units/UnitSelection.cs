using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// Which units are selected, shared by the tiles and rows that show units, so each can show whether it is one of them.
/// A click selects one unit; Ctrl+Click adds a unit or takes it away; Shift+Click selects every unit from the one
/// clicked last to the one clicked; Ctrl+A selects every unit shown. The unit clicked last is the one the Unit pane
/// shows.
/// </summary>
public sealed class UnitSelection
{
    /// <summary>The units selected, in the order they were selected.</summary>
    private readonly List<Unit> selectedUnits = [];

    /// <summary>The same units, to tell quickly whether a unit is one of them: the list asks for every tile it shows.</summary>
    private readonly HashSet<Unit> lookup = new(ReferenceEqualityComparer.Instance);

    /// <summary>The unit a Shift+Click selects from: the one clicked last without Shift.</summary>
    private Unit? rangeStart;

    /// <summary>
    /// The unit clicked last, which the Unit pane shows; null for none. Setting it selects that unit alone, as a plain
    /// click does.
    /// </summary>
    public Unit? Selected
    {
        get => selectedUnits.Count == 0 ? null : selectedUnits[^1];
        set
        {
            if (selectedUnits.Count <= 1 && ReferenceEquals(Selected, value)) return;
            selectedUnits.Clear();
            lookup.Clear();
            if (value is not null) Add(value);
            rangeStart = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Every unit selected, the one clicked last at the end.</summary>
    public IReadOnlyList<Unit> All => selectedUnits;

    /// <summary>Whether a unit is one of those selected.</summary>
    public bool IsSelected(Unit unit) => lookup.Contains(unit);

    /// <summary>Ctrl+Click: adds a unit to those selected, or takes it away when it is one of them already.</summary>
    public void Toggle(Unit unit)
    {
        if (lookup.Remove(unit)) selectedUnits.Remove(unit);
        else Add(unit);
        rangeStart = unit;
        Changed?.Invoke();
    }

    /// <summary>
    /// Shift+Click: selects every unit from the one clicked last to this one, in the order they are shown, and only
    /// those. With none clicked before, or that one no longer shown, it selects this unit alone.
    /// </summary>
    /// <param name="shown">The units shown, in the order they are on screen.</param>
    /// <param name="unit">The unit clicked.</param>
    public void SelectRange(IReadOnlyList<Unit> shown, Unit unit)
    {
        int from = rangeStart is null ? -1 : IndexOf(shown, rangeStart);
        int to = IndexOf(shown, unit);
        if (from < 0 || to < 0)
        {
            Selected = unit;
            return;
        }
        Unit start = rangeStart!;
        selectedUnits.Clear();
        lookup.Clear();
        int step = from <= to ? 1 : -1;
        for (int at = from; at != to + step; at += step) Add(shown[at]);
        // the next Shift+Click selects from the same unit again, as in a file list
        rangeStart = start;
        Changed?.Invoke();
    }

    /// <summary>Ctrl+A: selects every unit shown; the one clicked last stays the one the Unit pane shows.</summary>
    /// <param name="shown">The units shown, in the order they are on screen.</param>
    public void SelectAll(IReadOnlyList<Unit> shown)
    {
        Unit? last = Selected;
        selectedUnits.Clear();
        lookup.Clear();
        foreach (Unit unit in shown)
            if (!ReferenceEquals(unit, last)) Add(unit);
        if (last is not null && IndexOf(shown, last) >= 0) Add(last);
        Changed?.Invoke();
    }

    /// <summary>Selects these units and only these, the last of them the one the Unit pane would show.</summary>
    public void SetAll(IEnumerable<Unit> units)
    {
        selectedUnits.Clear();
        lookup.Clear();
        foreach (Unit unit in units) Add(unit);
        rangeStart = Selected;
        Changed?.Invoke();
    }

    /// <summary>
    /// The units selected again after the units were read again (when the open mod changed): each is replaced by its
    /// new copy, and one no longer there is let go.
    /// </summary>
    /// <param name="now">A unit's new copy; null when it is no longer there.</param>
    public void Renew(Func<Unit, Unit?> now)
    {
        List<Unit> renewed = [.. selectedUnits.Select(now).OfType<Unit>()];
        Unit? start = rangeStart is null ? null : now(rangeStart);
        selectedUnits.Clear();
        lookup.Clear();
        foreach (Unit unit in renewed) Add(unit);
        rangeStart = start;
        Changed?.Invoke();
    }

    /// <summary>
    /// With several units selected, lets go of those the filters no longer show, so a menu for the units selected never
    /// acts on units out of sight. A single unit stays selected, for the Unit pane to keep showing it.
    /// </summary>
    /// <param name="shown">The units the filters leave.</param>
    public void KeepOnly(IReadOnlyList<Unit> shown)
    {
        if (selectedUnits.Count <= 1) return;
        var stillShown = new HashSet<Unit>(shown, ReferenceEqualityComparer.Instance);
        if (selectedUnits.All(stillShown.Contains)) return;
        List<Unit> kept = [.. selectedUnits.Where(stillShown.Contains)];
        selectedUnits.Clear();
        lookup.Clear();
        foreach (Unit unit in kept) Add(unit);
        Changed?.Invoke();
    }

    /// <summary>Raised when the units selected change.</summary>
    public event Action? Changed;

    private void Add(Unit unit)
    {
        if (lookup.Add(unit)) selectedUnits.Add(unit);
    }

    private static int IndexOf(IReadOnlyList<Unit> units, Unit unit)
    {
        for (int at = 0; at < units.Count; at++)
            if (ReferenceEquals(units[at], unit)) return at;
        return -1;
    }
}
