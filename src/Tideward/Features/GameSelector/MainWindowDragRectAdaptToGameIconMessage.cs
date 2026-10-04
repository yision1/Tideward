namespace Tideward.Features.GameSelector;

internal class MainWindowDragRectAdaptToGameIconMessage
{

    public bool IgnoreDpiChanged { get; set; }

    public MainWindowDragRectAdaptToGameIconMessage(bool ignoreDpiChanged = false)
    {
        IgnoreDpiChanged = ignoreDpiChanged;
    }

}