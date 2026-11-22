using gca.Classes;
using gca.Script;
using System.Diagnostics;

namespace gca
{
    public partial class Autobot
    {

        /// <summary>
        /// not background mode
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void LeftClick(int x, int y)
        {
            RequireForegroundInput();
            x += DisplayInfo.X;
            y += DisplayInfo.Y;
            WinAPI.SetCursorPos(x, y);
            WinAPI.mouse_event(WinAPI.MOUSEEVENTF_LEFTDOWN, (uint)x, (uint)y, 0, UIntPtr.Zero);
            WinAPI.mouse_event(WinAPI.MOUSEEVENTF_LEFTUP, (uint)x, (uint)y, 0, UIntPtr.Zero);
        }

        /// <summary>
        /// not background mode
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void RightClick(int x, int y)
        {
            RequireForegroundInput();
            x += DisplayInfo.X;
            y += DisplayInfo.Y;
            WinAPI.SetCursorPos(x, y);
            WinAPI.mouse_event(WinAPI.MOUSEEVENTF_RIGHTDOWN, (uint)x, (uint)y, 0, UIntPtr.Zero);
            WinAPI.mouse_event(WinAPI.MOUSEEVENTF_RIGHTUP, (uint)x, (uint)y, 0, UIntPtr.Zero);
        }

        public void LeftClickBackground(IntPtr hWnd, int x, int y)
        {
            BackgroundAction(input => input.Click(x, y));
        }

        public void RightClickBackground(IntPtr hWnd, int x, int y)
        {
            BackgroundAction(input => input.Click(x, y, right: true));
        }

        /// <summary>
        /// not background mode
        /// </summary>
        /// <param name="amount"></param>
        public void ScrollWheel(int amount)
        {
            RequireForegroundInput();
            WinAPI.mouse_event(WinAPI.MOUSEEVENTF_WHEEL, 0, 0, (uint)amount, UIntPtr.Zero);
        }

        /// <summary>
        /// background mode
        /// </summary>
        /// <param name="hWnd"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="delta"></param>
        public void WheelBackground(IntPtr hWnd, int x, int y, int delta)
        {
            BackgroundAction(input => input.Wheel(x, y, delta));
        }

        public void MoveBackground(IntPtr hWnd, int x, int y)
        {
            BackgroundAction(input => input.Move(x, y));
        }

        public IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
        }

        public IntPtr MakeWParam(int x, int y) => MakeLParam(x, y);

        private void SendKey(byte keyCode)
        {
            if (backgroundMode) { BackgroundAction(input => input.Key(keyCode)); return; }
            WinAPI.keybd_event(keyCode, 0, WinAPI.KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            Thread.Sleep(50);
            WinAPI.keybd_event(keyCode, 0, WinAPI.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public void ClickHomeButton()
        {
            ClickButton("home");
        }

        public void ClickBackButton()
        {
            ClickButton("back");
        }

        public void ClickButton(string buttonName)
        {
            if (backgroundMode)
            {
                C();
                if (DateTime.UtcNow - lastValidCapture > TimeSpan.FromMilliseconds(500)) G();
                backgroundTarget!.Validate();
                RunLdConsole("action", "--index", ldInstanceIndex.ToString(), "--key", "call.keyboard", "--value", buttonName);
                frameHealth.InputSent(frameClock.Elapsed);
            }
            else RunLdConsole("action", "--name", windowName, "--key", "call.keyboard", "--value", buttonName);
        }

    }
}
