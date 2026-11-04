using EasyNdf.App.Controls;

namespace EasyNdf.App.Pages;

/// <summary>
/// While a file loads in the Editor (opened, its export shown, its changes only, back to its source), the loading card
/// covers the app (see <see cref="LoadingCover"/>): what loads, from where or with which rules, the bar and the step under
/// way.
/// </summary>
public partial class EditorPage
{
    /// <summary>A text this long (in characters) holds the screen while it is shown: the card shows at once.</summary>
    private const int BigText = 2_000_000;

    private LoadingCover? loadingCover;

    private LoadingCover Loading => loadingCover ??= new LoadingCover(this);

    private int StartLoading(string title, string where, params string[] steps) => Loading.Start(title, where, steps);

    private void ShowLoadingStep(int load, int step) => Loading.Step(load, step);

    /// <summary>A step that holds the screen while a text of this length is laid out.</summary>
    private Task ShowStepThatHolds(int load, int step, int textLength) => Loading.StepThatHolds(load, step, textLength >= BigText);

    private void EndLoading(int load) => Loading.End(load);
}
