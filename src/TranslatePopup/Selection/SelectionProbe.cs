using System.Windows.Automation;

namespace TranslatePopup.Selection;

/// <summary>
/// Checks, via UI Automation, whether the control at a given screen point actually has a
/// non-empty text selection right now - without touching the clipboard or sending any input.
/// Lets the caller skip synthesizing Ctrl+C for gestures that only *look* like a text selection
/// (a stray drag across a game's rendered canvas, clicking around in a terminal with nothing
/// highlighted, dragging a desktop icon, etc.), where the control either doesn't expose
/// TextPattern at all or reports an empty selection. This matters because Ctrl+C is not a no-op
/// everywhere: in a console/terminal with no selection it sends a break/interrupt to the running
/// process instead of copying anything.
/// </summary>
internal static class SelectionProbe
{
    public static bool HasNonEmptyTextSelection(int rawX, int rawY)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(rawX, rawY));
            if (element is null)
            {
                return false;
            }

            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj))
            {
                return false;
            }

            var ranges = ((TextPattern)patternObj).GetSelection();
            foreach (var range in ranges)
            {
                if (!string.IsNullOrWhiteSpace(range.GetText(-1)))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // Any COM/UIA failure (unresponsive app, unsupported control, cross-process hiccup)
            // is treated the same as "no selection" - erring towards not sending Ctrl+C.
            return false;
        }
    }
}
