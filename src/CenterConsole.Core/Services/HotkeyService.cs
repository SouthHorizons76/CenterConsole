using CenterConsole.Core.Interop;
using CenterConsole.Core.Models;

namespace CenterConsole.Core.Services;

public interface IHotkeyService
{
    /// <summary>Must be called once with a real window handle before any Register/Unregister call:
    /// RegisterHotKey requires a window owned by the calling thread to deliver WM_HOTKEY to.</summary>
    void AttachToWindow(IntPtr windowHandle);

    /// <summary>Registers (or replaces) the global hotkey for a logical action name (e.g. "MicMute").
    /// Returns false if the binding is unassigned or the combo is already claimed by another app.</summary>
    bool TryRegister(string actionKey, HotkeyBinding binding);

    void Unregister(string actionKey);
    void UnregisterAll();

    /// <summary>Feed every message from the host window's WndProc/HwndSource hook here. Returns true
    /// if the message was a WM_HOTKEY this service handled (caller should mark it handled).</summary>
    bool ProcessWindowMessage(int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Raised with the logical action name (e.g. "MicMute") when its hotkey is pressed.</summary>
    event EventHandler<string>? HotkeyPressed;
}

public sealed class HotkeyService : IHotkeyService
{
    private readonly Dictionary<string, int> _actionToId = new();
    private readonly Dictionary<int, string> _idToAction = new();
    private IntPtr _hwnd = IntPtr.Zero;
    private int _nextId = 1;

    public event EventHandler<string>? HotkeyPressed;

    public void AttachToWindow(IntPtr windowHandle)
    {
        _hwnd = windowHandle;
    }

    public bool TryRegister(string actionKey, HotkeyBinding binding)
    {
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"{nameof(AttachToWindow)} must be called before registering hotkeys.");

        if (!binding.IsAssigned)
            return false;

        Unregister(actionKey);

        int id = _nextId++;
        if (!User32Hotkey.RegisterHotKey(_hwnd, id, (uint)binding.Modifiers, binding.VirtualKey))
            return false; // combo already claimed by this or another application

        _actionToId[actionKey] = id;
        _idToAction[id] = actionKey;
        return true;
    }

    public void Unregister(string actionKey)
    {
        if (!_actionToId.Remove(actionKey, out int id))
            return;

        _idToAction.Remove(id);
        User32Hotkey.UnregisterHotKey(_hwnd, id);
    }

    public void UnregisterAll()
    {
        foreach (int id in _idToAction.Keys.ToList())
            User32Hotkey.UnregisterHotKey(_hwnd, id);

        _actionToId.Clear();
        _idToAction.Clear();
    }

    public bool ProcessWindowMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != User32Hotkey.WM_HOTKEY)
            return false;

        int id = wParam.ToInt32();
        if (!_idToAction.TryGetValue(id, out string? actionKey))
            return false;

        HotkeyPressed?.Invoke(this, actionKey);
        return true;
    }
}
