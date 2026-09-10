using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NexusClick.Services
{
    public class HotkeyService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _handle;
        private HwndSource? _source;
        private const int HOTKEY_ID = 9000;

        public event Action? OnHotkeyPressed;

        public void Register(System.Windows.Window window)
        {
            _handle = new WindowInteropHelper(window).Handle;
            _source = HwndSource.FromHwnd(_handle);
            _source.AddHook(HwndHook);

            // Register F8 (Virtual Key Code 0x77)
            RegisterHotKey(_handle, HOTKEY_ID, 0, 0x77); 
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                OnHotkeyPressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterHotKey(_handle, HOTKEY_ID);
            _source?.RemoveHook(HwndHook);
        }
    }
}
