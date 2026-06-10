using BeltAPI;
using BeltTensionTest.WPF.Services;
using BeltTensionTest.WPF.ViewModels;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ApiRotation = BeltAPI.Rotation;

namespace BeltTensionTest.WPF.Views
{
    public partial class TestingWindow : Window
    {
        private readonly TestingViewModel _vm;
        private readonly MainViewModel    _main;

        public TestingWindow(MainViewModel main)
        {
            _main = main;
            _vm   = new TestingViewModel(main);
            DataContext = _vm;
            InitializeComponent();

            _vm.GraphNeedsRedraw      += DrawCurveGraph;
            _vm.MotorGraphNeedsRedraw += DrawMotorGraph;

            SizeChanged += (_, _) => { DrawCurveGraph(); DrawMotorGraph(); };
            Closed      += (_, _) => { _vm.Dispose(); };
        }

        // ?? Curve Graph ??????????????????????????????????????????????????????
        private void DrawCurveGraph()
        {
            var device   = _main.Device;
            var settings = CarSettingsService.Instance.CurrentSettings;
            if (device == null || settings == null) return;

            int w = (int)CurveGraphImage.ActualWidth;
            int h = (int)CurveGraphImage.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var bgColor    = Color.FromArgb(18, 18, 30);
            var gridColor  = Color.FromArgb(38, 38, 58);
            var axisColor  = Color.FromArgb(70, 70, 100);
            var labelColor = Color.FromArgb(160, 160, 190);
            var surgeColor = Color.FromArgb(100, 160, 255);
            var swayColor  = Color.FromArgb(80,  200, 120);
            var heaveColor = Color.FromArgb(255, 165, 60);
            var maxColor   = Color.FromArgb(220, 60,  60);
            var restColor  = Color.FromArgb(60,  180, 180);

            int lp = 44, rp = 28, tp = 12, bp = 28;
            int gw = w - lp - rp, gh = h - tp - bp;
            if (gw <= 0 || gh <= 0) return;

            float minV = device.DeviceMotorSettings.LeftMinimumAngle;
            float maxV = device.DeviceMotorSettings.LeftMaximumAngle;
            if (minV > maxV) (minV, maxV) = (maxV, minV);
            float mr = maxV - minV; if (mr == 0) mr = 1;

            int MapY(float v) => tp + (int)((1f - (Math.Clamp(v, minV, maxV) - minV) / mr) * (gh - 1));
            float MapXToInput(int px) => (float)(px - lp) / (gw - 1) * 9f - 2f;
            int MapInputToX(float f) => lp + (int)((f + 2f) / 9f * (gw - 1));

            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(bgColor);

            var lf = new Font("Segoe UI", 7.5f);
            var lb = new SolidBrush(labelColor);

            using (var gp = new Pen(gridColor, 1))
            {
                for (int i = 0; i <= 4; i++)
                    g.DrawLine(gp, lp, tp + i * (gh - 1) / 4, lp + gw - 1, tp + i * (gh - 1) / 4);
                for (int gVal = -2; gVal <= 7; gVal++)
                {
                    int x = MapInputToX(gVal);
                    if (x >= lp && x <= lp + gw - 1)
                        g.DrawLine(gp, x, tp, x, tp + gh - 1);
                }
            }
            using var ap = new Pen(axisColor, 1);
            g.DrawLine(ap, lp, tp, lp, tp + gh - 1);
            g.DrawLine(ap, lp, tp + gh - 1, lp + gw - 1, tp + gh - 1);

            for (int gVal = -2; gVal <= 7; gVal++)
            {
                int x = MapInputToX(gVal);
                if (x < lp || x > lp + gw - 1) continue;
                var sz = g.MeasureString(gVal.ToString(), lf);
                g.DrawString(gVal.ToString(), lf, lb, x - sz.Width / 2, tp + gh + 4);
            }

            // Y axis label
            g.TranslateTransform(10, tp + gh / 2f);
            g.RotateTransform(-90);
            var yLbl = "Motor Output";
            var ySz  = g.MeasureString(yLbl, lf);
            g.DrawString(yLbl, lf, lb, -ySz.Width / 2, -ySz.Height / 2);
            g.ResetTransform();
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Max power line
            float maxOut = minV + mr * (settings.MaxPower / 100f);
            int   yMax   = MapY(maxOut);
            using (var mp = new Pen(maxColor, 1) { DashStyle = DashStyle.Dash })
                g.DrawLine(mp, lp, yMax, lp + gw - 1, yMax);

            // Resting point line
            float restVal = device.DeviceMotorSettings.ClampToMaxMotorPower(settings.RestingPoint / 100f, 0, settings).Item1;
            int   yRest   = MapY(restVal);
            using (var rp2 = new Pen(restColor, 1) { DashStyle = DashStyle.Dot })
                g.DrawLine(rp2, lp, yRest, lp + gw - 1, yRest);

            int baseline = tp + gh - 1;

            void DrawCurve(Color col, Func<int, System.Drawing.Point?> sample)
            {
                var pts = new List<System.Drawing.Point>();
                for (int px = 0; px < gw; px++)
                {
                    var pt = sample(px);
                    if (pt.HasValue) pts.Add(pt.Value);
                }
                if (pts.Count < 2) return;
                var poly = new System.Drawing.Point[pts.Count + 2];
                poly[0] = new System.Drawing.Point(pts[0].X, baseline);
                for (int i = 0; i < pts.Count; i++) poly[i + 1] = pts[i];
                poly[^1] = new System.Drawing.Point(pts[^1].X, baseline);
                using var fb = new SolidBrush(Color.FromArgb(35, col));
                g.FillPolygon(fb, poly);
                using var glow = new Pen(Color.FromArgb(50, col), 5);
                g.DrawLines(glow, pts.ToArray());
                using var pen = new Pen(col, 2);
                g.DrawLines(pen, pts.ToArray());
            }

            if (_vm.ShowBraking)
                DrawCurve(surgeColor, px =>
                {
                    float inp = MapXToInput(lp + px);
                    if (inp < -CarSettings.SurgeGForceScale || inp > CarSettings.SurgeGForceScale) return null;
                    bool inv = settings.InvertSurge; settings.InvertSurge = false;
                    var mo  = device.SetupMotorsForData(Math.Max(inp, 0), 0, 0, settings, ApiRotation.Zero);
                    float raw = mo.CalculateDataForGraph(device, settings, false);
                    float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(raw + settings.RestingPoint / 100f, 0, settings).Item1;
                    settings.InvertSurge = inv;
                    return new System.Drawing.Point(lp + px, MapY(yv));
                });

            if (_vm.ShowCornering)
                DrawCurve(swayColor, px =>
                {
                    float inp = MapXToInput(lp + px);
                    if (inp < 0 || inp > CarSettings.SwayGForceScale) return null;
                    bool inv = settings.InvertSway; settings.InvertSway = false;
                    var mo  = device.SetupMotorsForData(0, inp, 0, settings, ApiRotation.Zero);
                    mo.CalculateDataForGraph(device, settings, false);
                    float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(
                        mo.RightSwayOutput + settings.RestingPoint / 100f, 0, settings).Item1;
                    settings.InvertSway = inv;
                    return new System.Drawing.Point(lp + px, MapY(yv));
                });

            if (_vm.ShowVertical)
                DrawCurve(heaveColor, px =>
                {
                    float inp = MapXToInput(lp + px);
                    if (inp < -CarSettings.HeaveGForceScale || inp > CarSettings.HeaveGForceScale) return null;
                    bool inv = settings.InvertHeave; settings.InvertHeave = false;
                    var mo  = device.DeviceMotorSettings.Setup(0, 0, inp, CarSettingsService.Instance.CurrentSettings, ApiRotation.Zero);
                    float raw = mo.CalculateDataForGraph(device, settings, false);
                    float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(raw + settings.RestingPoint / 100f, 0, settings).Item1;
                    settings.InvertHeave = inv;
                    return new System.Drawing.Point(lp + px, MapY(yv));
                });

            // Live preview dots
            if (_vm.LivePreview)
            {
                var (ls, lsw, lh) = _vm.GetSmoothedInputs();
                void Dot(float inp, Color c)
                {
                    int mx = MapInputToX(inp);
                    if (mx < lp || mx > lp + gw) return;
                    var mo  = device.SetupMotorsForData(inp, 0, 0, settings, ApiRotation.Zero);
                    float raw = mo.CalculateDataForGraph(device, settings, false);
                    float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(raw + settings.RestingPoint / 100f, 0, settings).Item1;
                    int my = MapY(yv);
                    g.FillEllipse(new SolidBrush(Color.FromArgb(60, c)), mx - 8, my - 8, 16, 16);
                    g.FillEllipse(new SolidBrush(c), mx - 4, my - 4, 8, 8);
                }
                if (_vm.ShowBraking)   Dot(Math.Max(ls, 0), surgeColor);
                if (_vm.ShowCornering) Dot(Math.Max(lsw, 0), swayColor);
                if (_vm.ShowVertical)  Dot(lh, heaveColor);
            }

            CurveGraphImage.Source = BitmapToImageSource(bmp);
            bmp.Dispose();
        }

        // ?? Motor Graph ??????????????????????????????????????????????????????
        private void DrawMotorGraph()
        {
            var device = _main.Device;
            if (device == null) return;

            int w = (int)MotorGraphImage.ActualWidth;
            int h = (int)MotorGraphImage.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var bgColor    = Color.FromArgb(18, 18, 30);
            var gridColor  = Color.FromArgb(38, 38, 58);
            var axisColor  = Color.FromArgb(70, 70, 100);
            var labelColor = Color.FromArgb(160, 160, 190);
            var leftColor  = Color.FromArgb(100, 200, 255);
            var rightColor = Color.FromArgb(255, 140, 80);

            int lp = 48, rp = 12, tp = 18, bp = 22;
            int gw = w - lp - rp, gh = h - tp - bp;
            if (gw <= 0 || gh <= 0) return;

            float minV = device.DeviceMotorSettings.LeftMinimumAngle;
            float maxV = device.DeviceMotorSettings.LeftMaximumAngle;
            if (minV > maxV) (minV, maxV) = (maxV, minV);
            float mr = maxV - minV; if (mr == 0) mr = 1;

            int MapY(float v) => tp + (int)((1f - (Math.Clamp(v, minV, maxV) - minV) / mr) * (gh - 1));

            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(bgColor);

            var lf = new Font("Segoe UI", 7.5f);
            var lb = new SolidBrush(labelColor);

            using (var gp = new Pen(gridColor, 1))
                for (int i = 0; i <= 4; i++)
                    g.DrawLine(gp, lp, tp + i * (gh - 1) / 4, lp + gw - 1, tp + i * (gh - 1) / 4);

            for (int i = 0; i <= 4; i++)
            {
                int y = tp + i * (gh - 1) / 4;
                float val = maxV - i * mr / 4f;
                var sz = g.MeasureString(val.ToString("F0"), lf);
                g.DrawString(val.ToString("F0"), lf, lb, lp - sz.Width - 2, y - sz.Height / 2);
            }

            using var ap = new Pen(axisColor, 1);
            g.DrawLine(ap, lp, tp, lp, tp + gh - 1);
            g.DrawLine(ap, lp, tp + gh - 1, lp + gw - 1, tp + gh - 1);

            var (lh, rh) = _vm.GetMotorHistory();

            void DrawHistory(Queue<float> history, Func<float, float> unInvert, Color color)
            {
                var arr = history.ToArray();
                if (arr.Length < 2) return;
                var pts = new System.Drawing.Point[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                    pts[i] = new System.Drawing.Point(lp + (int)((float)i / (TestingViewModel.HistorySize - 1) * (gw - 1)), MapY(unInvert(arr[i])));
                using var glow = new Pen(Color.FromArgb(50, color), 5);
                g.DrawLines(glow, pts);
                using var pen = new Pen(color, 2);
                g.DrawLines(pen, pts);
            }

            float UnInvertL(float v) => device.DeviceMotorSettings.LeftInverted  ? device.DeviceMotorSettings.LeftMaximumAngle  - v : v;
            float UnInvertR(float v) => device.DeviceMotorSettings.RightInverted ? device.DeviceMotorSettings.RightMaximumAngle - v : v;

            DrawHistory(lh, UnInvertL, leftColor);
            DrawHistory(rh, UnInvertR, rightColor);

            // Legend
            var lf2 = new Font("Segoe UI", 8f, System.Drawing.FontStyle.Bold);
            g.FillRectangle(new SolidBrush(leftColor),  lp + gw - 120, tp + 4, 12, 12);
            g.DrawString("Left",  lf2, new SolidBrush(leftColor),  lp + gw - 105, tp + 3);
            g.FillRectangle(new SolidBrush(rightColor), lp + gw - 60,  tp + 4, 12, 12);
            g.DrawString("Right", lf2, new SolidBrush(rightColor), lp + gw - 45,  tp + 3);

            MotorGraphImage.Source = BitmapToImageSource(bmp);
            bmp.Dispose();
        }

        // Convert GDI bitmap to WPF ImageSource
        private static BitmapSource BitmapToImageSource(Bitmap bmp)
        {
            var handle = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    handle, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(handle);
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
