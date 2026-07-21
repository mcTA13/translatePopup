using System.Runtime.InteropServices;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

namespace TranslatePopup.Interop;

internal static class ClipboardHelper
{
    private const int MaxRetries = 5;
    private const int RetryDelayMs = 40;

    public static uint GetSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();

    /// <summary>Captures the current clipboard contents (all formats) so they can be restored later.</summary>
    public static async Task<DataObject?> TrySnapshotAsync()
    {
        for (var i = 0; i < MaxRetries; i++)
        {
            try
            {
                var current = Clipboard.GetDataObject();
                if (current is null)
                {
                    return null;
                }

                var snapshot = new DataObject();
                foreach (var format in current.GetFormats())
                {
                    try
                    {
                        snapshot.SetData(format, current.GetData(format));
                    }
                    catch
                    {
                        // Some formats cannot be round-tripped; skip them, best effort.
                    }
                }

                return snapshot;
            }
            catch (COMException)
            {
                await Task.Delay(RetryDelayMs).ConfigureAwait(true);
            }
        }

        return null;
    }

    public static async Task RestoreAsync(DataObject? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        for (var i = 0; i < MaxRetries; i++)
        {
            try
            {
                Clipboard.SetDataObject(snapshot, true);
                return;
            }
            catch (COMException)
            {
                await Task.Delay(RetryDelayMs).ConfigureAwait(true);
            }
        }
    }

    public static async Task<string?> TryGetTextAsync()
    {
        for (var i = 0; i < MaxRetries; i++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                await Task.Delay(RetryDelayMs).ConfigureAwait(true);
            }
        }

        return null;
    }

    /// <summary>Polls the clipboard sequence number until it changes from <paramref name="before"/> or the timeout elapses.</summary>
    public static async Task<bool> WaitForChangeAsync(uint before, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (GetSequenceNumber() != before)
            {
                return true;
            }

            await Task.Delay(25).ConfigureAwait(true);
        }

        return false;
    }
}
