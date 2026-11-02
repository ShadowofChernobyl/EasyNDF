namespace EasyNdf.Data;

/// <summary>
/// What is typed in a Find box, matched against a name or a descriptor: in any case, and with a space and an
/// underscore taken as the same, so "M1A1 Abrams" finds Descriptor_Unit_M1A1_Abrams_US as well as the unit's name.
/// </summary>
public static class Typed
{
    /// <summary>Whether <paramref name="text"/> holds what was typed; true when nothing was.</summary>
    public static bool IsIn(string text, string typed)
    {
        string wanted = typed.Trim().Replace('_', ' ');
        return wanted == "" || text.Replace('_', ' ').Contains(wanted, StringComparison.CurrentCultureIgnoreCase);
    }
}
