using Gma.System.MouseKeyHook;
using System;
using System.Configuration;
using System.Drawing;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;

namespace keys
{
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

        private IKeyboardMouseEvents GlobalHook;
        private bool initialized = false;
        private IntPtr hdc;
        private Graphics graphics;
        private IDisposable MouseDisposable;
        private IDisposable ModeDisposable;

        private short bri = 125;

        public short Bri { get => bri; set => SetBrightness(value); }

        private void InitializeClass()
        {
            if (initialized)
                return;

            //Get the hardware device context of the screen, we can do
            //this by getting the graphics object of null (IntPtr.Zero)
            //then getting the HDC and converting that to an Int32.
            graphics = Graphics.FromHwnd(IntPtr.Zero);
            hdc = graphics.GetHdc();

            initialized = true;
        }

        public void SetBrightness(short brightness)
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
        }

        public bool GetBrightness()
        {
            InitializeClass();

            var ramp = new RAMP();

            //For some reason, this always returns false?
            bool retVal = GetDeviceGammaRamp(hdc, ref ramp);

            //Memory allocated through stackalloc is automatically free'd
            //by the CLR.

            return retVal;
        }

        public Form1()
        {
            InitializeComponent();
            Application.ApplicationExit += (sender, e) => Form1_FormClosed(sender, e);
            InitializeClass();
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.Visible = false;
            this.FormClosed += new FormClosedEventHandler(Form1_FormClosed);
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            var icon = new NotifyIcon
            {
                Icon = this.Icon,
                Visible = true,
            };
            icon.Click += (sender, e) => this.Close();
        }

        protected void Form1_FormClosed(object sender, EventArgs e)
        {
            // abort the rx sequence
            MouseDisposable?.Dispose();
            ModeDisposable?.Dispose();
            SetBrightness(130);
            GlobalHook?.Dispose();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            GlobalHook = Hook.GlobalEvents();
            IObservable<EventPattern<MouseEventArgs>> obs = Observable.FromEventPattern<MouseEventHandler, MouseEventArgs>(
                addHandler: h => GlobalHook.MouseDown += h,
                removeHandler: h => GlobalHook.MouseDown -= h)
                .SubscribeOn(Dispatcher.CurrentDispatcher);

            short brightness = 0;
            var rampUp = Observable.Interval(TimeSpan.FromSeconds(GetSetting("rampUpInterval", .06)))
                .Select(x =>
                {
                    brightness += 10;
                    SetBrightness(brightness);
                    return brightness;
                })
                .TakeWhile(x => x < 131);

            var solid = Observable.Interval(TimeSpan.FromSeconds(GetSetting("solidInterval", 6)))
                .Do(x => SetBrightness(130))
                .Take(1);

            var mode = ConfigurationManager.AppSettings["mode"] ?? "solid";

            IDisposable onsubscribe()
            {
                switch (mode.ToLower())
                {
                    case "rampup":
                        brightness = 0;
                        return rampUp.Subscribe();
                    case "solid":
                    default:
                        SetBrightness(0);
                        return solid.Subscribe();
                }
            }

            MouseDisposable = obs.Subscribe(
                onNext =>
                {
                    // Dispose the old mode observable if one exists. Solid mode has odd behavior if omit this.
                    ModeDisposable?.Dispose();
                    Console.WriteLine("Left mouse click!");
                    ModeDisposable = onsubscribe();
                });
        }

        private double GetSetting(string name, double @default)
        {
            if (double.TryParse(ConfigurationManager.AppSettings[name], out var res))
                return res;
            return @default;
        }
    }
}
