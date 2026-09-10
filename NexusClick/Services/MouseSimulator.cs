using System;
using System.Runtime.InteropServices;

namespace NexusClick.Services
{
    public static class MouseSimulator
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        public static void Click(int buttonType, bool doubleClick, int? x = null, int? y = null)
        {
            if (x.HasValue && y.HasValue) SetCursorPos(x.Value, y.Value);

            uint downFlag = MOUSEEVENTF_LEFTDOWN;
            uint upFlag = MOUSEEVENTF_LEFTUP;

            if (buttonType == 1) { downFlag = MOUSEEVENTF_RIGHTDOWN; upFlag = MOUSEEVENTF_RIGHTUP; }
            else if (buttonType == 2) { downFlag = MOUSEEVENTF_MIDDLEDOWN; upFlag = MOUSEEVENTF_MIDDLEUP; }

            SendClick(downFlag, upFlag);
            if (doubleClick)
            {
                System.Threading.Thread.Sleep(30); // Jeda micro antar klik
                SendClick(downFlag, upFlag);
            }
        }

        private static void SendClick(uint down, uint up)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = down;
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = up;

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
