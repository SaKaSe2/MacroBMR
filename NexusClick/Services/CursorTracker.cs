using System;
using System.Runtime.InteropServices;

namespace NexusClick.Services
{
    public static class CursorTracker
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public static (int x, int y) GetPosition()
        {
            GetCursorPos(out POINT point);
            return (point.X, point.Y);
        }
    }
}
