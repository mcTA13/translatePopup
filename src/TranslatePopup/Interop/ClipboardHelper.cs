using System.Runtime.InteropServices;
using System.Windows.Threading;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

namespace TranslatePopup.Interop;

internal static class ClipboardHelper
{
    private const int MaxRetries = 5;
    private const int RetryDelayMs = 40;

    // OLE clipboard access (especially enumerating/copying every format on the clipboard) can
    // block for a surprising stretch of time. Doing that on the UI thread - which also owns the
    // global mouse hook - would freeze all mouse input system-wide for as long as it takes, which
    // is exactly why dragging felt like it briefly hung. Running it on its own STA thread instead
    // keeps the hook thread free the whole time.
    private static readonly Dispatcher WorkerDispatcher = CreateWorkerDispatcher();

    private static Dispatcher CreateWorkerDispatcher()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "ClipboardWorker",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        ready.Wait();
        return dispatcher!;
    }

    public static uint GetSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();

    /// <summary>Captures the current clipboard contents (all formats) so they can be restored later.</summary>
    public static Task<DataObject?> TrySnapshotAsync() => WorkerDispatcher.InvokeAsync(() =>
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
                Thread.Sleep(RetryDelayMs);
            }
        }

        return null;
    }).Task;

    public static Task RestoreAsync(DataObject? snapshot) => WorkerDispatcher.InvokeAsync(() =>
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
                Thread.Sleep(RetryDelayMs);
            }
        }
    }).Task;

    public static Task<string?> TryGetTextAsync() => WorkerDispatcher.InvokeAsync(() =>
    {
        for (var i = 0; i < MaxRetries; i++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }

        return (string?)null;
    }).Task;

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
