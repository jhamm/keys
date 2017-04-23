using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace keys
{
    public static class MouseHook
    {
        public static event EventHandler MouseAction = delegate { };

        public static void Start()
        {
            _hookID = SetHook(_proc);
        }

        public static void Stop()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private static LowLevelMouseProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using (System.Diagnostics.Process curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (System.Diagnostics.ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_MOUSE_LL, proc,
                  GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(
          int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && MouseMessages.WM_LBUTTONDOWN == (MouseMessages)wParam)
            {
                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                MouseAction(null, new EventArgs());
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private const int WH_MOUSE_LL = 14;

        private enum MouseMessages
        {
            WM_LBUTTONDOWN = 0x0201,
            WM_LBUTTONUP = 0x0202,
            WM_MOUSEMOVE = 0x0200,
            WM_MOUSEWHEEL = 0x020A,
            WM_RBUTTONDOWN = 0x0204,
            WM_RBUTTONUP = 0x0205
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
          LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }

    public partial class Form1 : Form
    {
        [DllImport("gdi32.dll")]
        public static extern bool GetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);
        [DllImport("gdi32.dll")]
        public static extern bool SetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct RAMP
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public UInt16[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public UInt16[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public UInt16[] Blue;
        }

        private static bool initialized = false;
        private static IntPtr hdc;

        private short bri = 125;

        public short Bri { get => bri; set => SetBrightness(value); }

        private static void InitializeClass()
        {
            if (initialized)
                return;

            //Get the hardware device context of the screen, we can do
            //this by getting the graphics object of null (IntPtr.Zero)
            //then getting the HDC and converting that to an Int32.
            hdc = Graphics.FromHwnd(IntPtr.Zero).GetHdc();

            initialized = true;
        }

        public static bool SetBrightness(short brightness)
        {
            if (brightness > 255)
                brightness = 255;

            if (brightness < 0)
                brightness = 0;

            var ramp = new RAMP
            {
                Red = new ushort[256],
                Blue = new ushort[256],
                Green = new ushort[256],
            };

            for (var i = 0; i < 256; i++)
            {
                var arrayVal = Math.Min(i * (brightness + 128), ushort.MaxValue);

                ramp.Red[i] = ramp.Blue[i] = ramp.Green[i] = (ushort)arrayVal;
            }

            //For some reason, this always returns false?
            bool retVal = SetDeviceGammaRamp(hdc, ref ramp);

            //Memory allocated through stackalloc is automatically free'd
            //by the CLR.

            return retVal;
        }

        public static bool GetBrightness()
        {
            InitializeClass();

            var ramp = new RAMP();

            //For some reason, this always returns false?
            bool retVal = GetDeviceGammaRamp(hdc, ref ramp);

            //Memory allocated through stackalloc is automatically free'd
            //by the CLR.

            return retVal;
        }

        Thread myThread;
        private void OnApplicationExit(object sender, System.Windows.ExitEventArgs e)
        {
            if (myThread.IsAlive)
            {
                myThread.Abort();
            }
            SetBrightness(130);
        }

        [DllImport("Shell32")]
        public static extern int ExtractIconEx(
            string sFile,
            int iIndex,
            out IntPtr piLargeVersion,
            out IntPtr piSmallVersion,
            int amountIcons);

        public Icon GetExecutableIcon()
        {
            ExtractIconEx(Application.ExecutablePath, 0, out IntPtr large, out IntPtr small, 1);
            return Icon.FromHandle(small);
        }

        public Form1()
        {
            InitializeComponent();
            this.myThread = new Thread(new ThreadStart(myStartingMethod));
            MouseHook.Start();
            MouseHook.MouseAction += new EventHandler(Event);
            InitializeClass();
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.Visible = false;
            this.FormClosed += new FormClosedEventHandler(Form1_FormClosed);
            this.Icon = this.GetExecutableIcon();
            NotifyIcon icon = new NotifyIcon();
            icon.Icon = this.Icon;
            icon.Visible = true;
            icon.Click += (sender, e) => this.Close();
        }

        protected void Form1_FormClosed(object sender, EventArgs e)
        {
            if (myThread.IsAlive)
            {
                myThread.Abort();
            }
            SetBrightness(130);
        }

        private void myStartingMethod()
        {
            SetBrightness(0);
            Thread.Sleep(6000);
            SetBrightness(135);
        }

        private void Event(object sender, EventArgs e) {
            Console.WriteLine("Left mouse click!");
            if (myThread.IsAlive)
            {
                myThread.Abort();
            }
            myThread = new Thread(new ThreadStart(myStartingMethod));
            myThread.Start();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
        }
    }
}
