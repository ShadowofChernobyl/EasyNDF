namespace EasyNdf.App.Editor;

/// <summary>What the Filter Descriptors window needs from the descriptor viewer it filters.</summary>
public interface IDescriptorFilterOwner
{
    /// <summary>The filter in use; the window may put another in its place (a saved filter chosen, or the filter cleared).</summary>
    DescriptorFilterState Filter { get; set; }

    /// <summary>The filter changed: it is kept, the viewer's filter button shows it, and the tree is filtered again a moment later.</summary>
    void FilterChanged();

    /// <summary>The properties the descriptors being viewed have, for the Property list, with "(Name)" and "(Type)" first.</summary>
    Task<List<string>> FilterProperties();

    /// <summary>How many descriptors the filter shows, as "Showing [12] of 40 descriptors": the bold number between the two parts.</summary>
    (string Before, string Bold, string After) FilterCount();
}
