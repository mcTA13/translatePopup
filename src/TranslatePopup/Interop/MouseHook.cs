using System.Runtime.InteropServices;
using Point = System.Windows.Point;

namespace TranslatePopup.Interop;

internal readonly record struct MouseHookEvent(int RawX, int RawY, Point DipPoint, uint Time);

/// <summary>Thin wrapper around a WH_MOUSE_LL global hook. Carries no selection-detection logic of its own.</summary>
internal sealed class MouseHook : IDisposable
{
    // Field, not local: SetWindowsHookEx does not root the delegate, so the GC could otherwise collect it.
    private readonly LowLevelMouseProc _proc;
    private nint _hookId;

    public event Action<MouseHookEvent, bool>? LeftButtonDown;
    public event Action<MouseHookEvent>? LeftButtonUp;

    public MouseHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var hMod = NativeMethods.GetModuleHandle(curModule?.ModuleName);
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var evt = new MouseHookEvent(data.pt.X, data.pt.Y, DpiHelper.PhysicalToDip(data.pt.X, data.pt.Y), data.time);

            switch ((int)wParam)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                    var shiftPressed = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                    LeftButtonDown?.Invoke(evt, shiftPressed);
                    break;
                case NativeMethods.WM_LBUTTONUP:
                    LeftButtonUp?.Invoke(evt);
                    break;
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = nint.Zero;
        }
    }
}
