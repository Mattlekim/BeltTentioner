using BeltAPI;
using BeltTentionerWPF.Services;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BeltTentionerWPF.ViewModels
{
    public class TestingViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;

        private enum TestMode { None, Surge, Sway, Heave }
        private TestMode _currentMode = TestMode.None;

        private float _sweepValue = -2f;
        private bool _sweepUp = true;

        private const float SurgeMin = -2f, SurgeMax = 7f;
        private const float SwayMin = -5f, SwayMax = 5f;
        private const float HeaveMin = -2f, HeaveMax = 3f;
        private const float StepSize = 0.20f;

        private readonly DispatcherTimer _timer;

        // Graph motor history
        private const int HistorySize = 300;
        private readonly Queue<float> _leftHistory = new();
        private readonly Queue<float> _rightHistory = new();
        private float _lastPitch, _lastRoll;

        // Live preview smoothed
        private float _smoothSurge, _smoothSway, _smoothHeave;

        // Status
        private string _status = "Idle";
        public string Status { get => _status; set => SetField(ref _status, value); }

        // Active buttons
        private bool _surgeActive, _swayActive, _heaveActive;
        public bool SurgeActive { get => _surgeActive; set => SetField(ref _surgeActive, value); }
        public bool SwayActive { get => _swayActive; set => SetField(ref _swayActive, value); }
        public bool HeaveActive { get => _heaveActive; set => SetField(ref _heaveActive, value); }

        // Graph visibility toggles
        public bool ShowSurge { get => _showSurge; set { SetField(ref _showSurge, value); DrawCurveGraph(); } }
        private bool _showSurge = true;
        public bool ShowSway { get => _showSway; set { SetField(ref _showSway, value); DrawCurveGraph(); } }
        private bool _showSway = true;
        public bool ShowHeave { get => _showHeave; set { SetField(ref _showHeave, value); DrawCurveGraph(); } }
        private bool _showHeave = true;
        public bool ShowLivePreview { get => _showLive; set { SetField(ref _showLive, value); DrawCurveGraph(); } }
        private bool _showLive = true;

        // Rotation labels
        public string PitchText { get => _pitchText; set => SetField(ref _pitchText, value); }
        private string _pitchText = "Pitch: 0.0°";
        public string RollText { get => _rollText; set => SetField(ref _rollText, value); }
        private string _rollText = "Roll: 0.0°";

        // Graph bitmaps (WriteableBitmap for WPF)
        public WriteableBitmap? CurveGraphBitmap { get => _curveGraph; set => SetField(ref _curveGraph, value); }
        private WriteableBitmap? _curveGraph;
        public WriteableBitmap? MotorGraphBitmap { get => _motorGraph; set => SetField(ref _motorGraph, value); }
        private WriteableBitmap? _motorGraph;

        // Canvas dimensions (set by view on size changed)
        public int CurveGraphWidth { get => _cgW; set { _cgW = value; DrawCurveGraph(); } }
        private int _cgW = 600;
        public int CurveGraphHeight { get => _cgH; set { _cgH = value; DrawCurveGraph(); } }
        private int _cgH = 250;
        public int MotorGraphWidth { get => _mgW; set { _mgW = value; DrawMotorGraph(); } }
        private int _mgW = 600;
        public int MotorGraphHeight { get => _mgH; set { _mgH = value; DrawMotorGraph(); } }
        private int _mgH = 200;

        // Commands
        public ICommand SurgeCommand { get; }
        public ICommand SwayCommand { get; }
        public ICommand HeaveCommand { get; }
        public ICommand StopCommand { get; }

        public TestingViewModel(MainViewModel main)
        {
            _main = main;
            SurgeCommand = new RelayCommand(() => StartMode(TestMode.Surge));
            SwayCommand = new RelayCommand(() => StartMode(TestMode.Sway));
            HeaveCommand = new RelayCommand(() => StartMode(TestMode.Heave));
            StopCommand = new RelayCommand(StopTest);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _timer.Tick += OnTick;
        }

        private void StartMode(TestMode mode)
        {
            _currentMode = mode;
            _sweepUp = true;
            _sweepValue = GetMin(mode);
            SurgeActive = mode == TestMode.Surge;
            SwayActive = mode == TestMode.Sway;
            HeaveActive = mode == TestMode.Heave;
            _timer.Start();
        }

        private void StopTest()
        {
            _timer.Stop();
            _currentMode = TestMode.None;
            SurgeActive = SwayActive = HeaveActive = false;
            _main.StopForces();
            _main.UpdateForces(0, 0, 0, Rotation.Zero);
            Status = "Idle";
        }

        private void OnTick(object? sender, EventArgs e)
        {
            float min = GetMin(_currentMode), max = GetMax(_currentMode);
            if (_sweepUp) { _sweepValue += StepSize; if (_sweepValue >= max) { _sweepValue = max; _sweepUp = false; } }
            else { _sweepValue -= StepSize; if (_sweepValue <= min) { _sweepValue = min; _sweepUp = true; } }

            float surge = 0, sway = 0, heave = 0;
            switch (_currentMode)
            {
                case TestMode.Surge: surge = _sweepValue; break;
                case TestMode.Sway: sway = _sweepValue; break;
                case TestMode.Heave: heave = _sweepValue; break;
            }

            _main.UpdateForces(surge, sway, heave, Rotation.Zero);
            Status = $"{_currentMode}: {_sweepValue:F2}  [{min:F0}?{max:F0}]";
        }

        public void UpdateLivePreview(float surge, float sway, float heave)
        {
            _smoothSurge = _smoothSurge * .9f + surge * .1f;
            _smoothSway = _smoothSway * .9f + sway * .1f;
            _smoothHeave = _smoothHeave * .9f + heave * .1f;
            DrawCurveGraph();
        }

        public void UpdateMotorOutput(float left, float right, Rotation rotation)
        {
            if (_leftHistory.Count >= HistorySize) _leftHistory.Dequeue();
            if (_rightHistory.Count >= HistorySize) _rightHistory.Dequeue();
            _leftHistory.Enqueue(left);
            _rightHistory.Enqueue(right);
            _lastPitch = rotation.Pitch;
            _lastRoll = rotation.Roll;
            PitchText = $"Pitch: {(_lastPitch * 180f / MathF.PI):F1}°";
            RollText = $"Roll:  {(_lastRoll * 180f / MathF.PI):F1}°";
            DrawMotorGraph();
        }

        // ?? Graph rendering ???????????????????????????????????????????????????
        private static readonly Color BgCol = Color.FromRgb(18, 18, 30);
        private static readonly Color GridCol = Color.FromRgb(38, 38, 58);
        private static readonly Color AxisCol = Color.FromRgb(70, 70, 100);
        private static readonly Color LabelCol = Color.FromRgb(160, 160, 190);
        private static readonly Color SurgeCol = Color.FromRgb(100, 160, 255);
        private static readonly Color SwayCol = Color.FromRgb(80, 200, 120);
        private static readonly Color HeaveCol = Color.FromRgb(255, 165, 60);
        private static readonly Color MaxLineCol = Color.FromRgb(220, 60, 60);
        private static readonly Color LeftMotorCol = Color.FromRgb(100, 200, 255);
        private static readonly Color RightMotorCol = Color.FromRgb(255, 140, 80);

        public void DrawCurveGraph()
        {
            var device = _main.BeltDevice;
            if (!device.IsConnected) return;
            var settings = CarSettingsService.Instance.CurrentSettings;
            if (settings == null) return;

            int w = Math.Max(_cgW, 10), h = Math.Max(_cgH, 10);
            int lp = 44, rp = 28, tp = 12, bp = 28;
            int gw = w - lp - rp, gh = h - tp - bp;
            if (gw <= 0 || gh <= 0) return;

            float minV = device.DeviceMotorSettings.LeftMinimumAngle;
            float maxV = device.DeviceMotorSettings.LeftMaximumAngle;
            if (minV > maxV) (minV, maxV) = (maxV, minV);
            float motorRange = maxV - minV; if (motorRange == 0) motorRange = 1;

            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            bmp.Lock();
            unsafe
            {
                int* buf = (int*)bmp.BackBuffer;
                int stride = bmp.BackBufferStride / 4;

                void FillAll(Color c)
                {
                    int col = ToInt(c);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            buf[y * stride + x] = col;
                }

                void DrawPx(int x, int y, Color c)
                {
                    if (x < 0 || x >= w || y < 0 || y >= h) return;
                    buf[y * stride + x] = ToInt(c);
                }

                void HLine(int x0, int x1, int y, Color c)
                { for (int x = x0; x <= x1; x++) DrawPx(x, y, c); }

                void VLine(int x, int y0, int y1, Color c)
                { for (int y = y0; y <= y1; y++) DrawPx(x, y, c); }

                FillAll(BgCol);

                // Grid
                for (int i = 0; i <= 4; i++)
                {
                    int y = tp + i * (gh - 1) / 4;
                    HLine(lp, lp + gw - 1, y, GridCol);
                }
                for (int gVal = -2; gVal <= 7; gVal++)
                {
                    int x = lp + (int)((gVal + 2f) / 9f * (gw - 1));
                    if (x >= lp && x < lp + gw) VLine(x, tp, tp + gh - 1, GridCol);
                }
                // Axes
                VLine(lp, tp, tp + gh - 1, AxisCol);
                HLine(lp, lp + gw - 1, tp + gh - 1, AxisCol);

                int MapY(float v)
                {
                    float c = Math.Clamp(v, minV, maxV);
                    return tp + (int)((1f - (c - minV) / motorRange) * (gh - 1));
                }
                int MapX(float f) => lp + (int)((f + 2f) / 9f * (gw - 1));
                float XToInput(int px) => (float)(px - lp) / (gw - 1) * 9f - 2f;

                // Max power line
                float maxOut = minV + motorRange * (settings.MaxPower / 100f);
                int yMax = MapY(maxOut);
                HLine(lp, lp + gw - 1, yMax, MaxLineCol);

                // Surge
                if (_showSurge)
                    DrawCurveLine(buf, stride, w, h, lp, gw, SurgeCol, px =>
                    {
                        float inp = XToInput(lp + px);
                        if (inp < 0 || inp > CarSettings.SurgeGForceScale) return null;
                        bool inv = settings.InvertSurge; settings.InvertSurge = false;
                        var mo = device.SetupMotorsForData(inp, 0, 0, settings, Rotation.Zero);
                        float raw = mo.CalculateDataForGraph(device, settings, false);
                        float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(raw + settings.RestingPoint / 100f, 0, settings).Item1;
                        settings.InvertSurge = inv;
                        return MapY(yv);
                    });

                // Sway
                if (_showSway)
                    DrawCurveLine(buf, stride, w, h, lp, gw, SwayCol, px =>
                    {
                        float inp = XToInput(lp + px);
                        if (inp < 0 || inp > CarSettings.SwayGForceScale) return null;
                        bool inv = settings.InvertSway; settings.InvertSway = false;
                        var mo = device.SetupMotorsForData(0, inp, 0, settings, Rotation.Zero);
                        mo.CalculateDataForGraph(device, settings, false);
                        float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(mo.RightSwayOutput + settings.RestingPoint / 100f, 0, settings).Item1;
                        settings.InvertSway = inv;
                        return MapY(yv);
                    });

                // Heave
                if (_showHeave)
                    DrawCurveLine(buf, stride, w, h, lp, gw, HeaveCol, px =>
                    {
                        float inp = XToInput(lp + px);
                        if (inp < -CarSettings.HeaveGForceScale || inp > CarSettings.HeaveGForceScale) return null;
                        bool inv = settings.InvertHeave; settings.InvertHeave = false;
                        var mo = device.DeviceMotorSettings.Setup(0, 0, inp, settings, Rotation.Zero);
                        float raw = mo.CalculateDataForGraph(device, settings, false);
                        float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(raw + settings.RestingPoint / 100f, 0, settings).Item1;
                        settings.InvertHeave = inv;
                        return MapY(yv);
                    });

                // Live dots
                if (_showLive)
                {
                    if (_showSurge) DrawDot(buf, stride, w, h, MapX(Math.Max(_smoothSurge, 0)), MapY(GetLiveMotorY(device, settings, _smoothSurge, 0, 0)), SurgeCol);
                    if (_showSway)  DrawDot(buf, stride, w, h, MapX(Math.Max(_smoothSway, 0)),  MapY(GetLiveMotorY(device, settings, 0, _smoothSway, 0)),  SwayCol);
                    if (_showHeave) DrawDot(buf, stride, w, h, MapX(_smoothHeave),               MapY(GetLiveMotorY(device, settings, 0, 0, _smoothHeave)), HeaveCol);
                }
            }
            bmp.AddDirtyRect(new Int32Rect(0, 0, w, h));
            bmp.Unlock();
            CurveGraphBitmap = bmp;
        }

        private float GetLiveMotorY(BeltSerialDevice device, CarSettings settings, float surge, float sway, float heave)
        {
            var mo = device.SetupMotorsForData(surge, sway, heave, settings, Rotation.Zero);
            float raw = mo.CalculateDataForGraph(device, settings, false);
            return device.DeviceMotorSettings.ClampToMaxMotorPower(raw + settings.RestingPoint / 100f, 0, settings).Item1;
        }

        public void DrawMotorGraph()
        {
            var device = _main.BeltDevice;
            if (!device.IsConnected) return;

            int w = Math.Max(_mgW, 10), h = Math.Max(_mgH, 10);
            int lp = 48, rp = 12, tp = 18, bp = 22;
            int gw = w - lp - rp, gh = h - tp - bp;
            if (gw <= 0 || gh <= 0) return;

            float minV = device.DeviceMotorSettings.LeftMinimumAngle;
            float maxV = device.DeviceMotorSettings.LeftMaximumAngle;
            if (minV > maxV) (minV, maxV) = (maxV, minV);
            float range = maxV - minV; if (range == 0) range = 1;

            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            bmp.Lock();
            unsafe
            {
                int* buf = (int*)bmp.BackBuffer;
                int stride = bmp.BackBufferStride / 4;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        buf[y * stride + x] = ToInt(BgCol);

                void DrawPx(int x, int y, Color c)
                { if (x < 0 || x >= w || y < 0 || y >= h) return; buf[y * stride + x] = ToInt(c); }

                int MapY(float v) => tp + (int)((1f - (Math.Clamp(v, minV, maxV) - minV) / range) * (gh - 1));

                // Grid
                for (int i = 0; i <= 4; i++)
                {
                    int y = tp + i * (gh - 1) / 4;
                    for (int x = lp; x < lp + gw; x++) DrawPx(x, y, GridCol);
                }
                // Axes
                for (int y = tp; y < tp + gh; y++) DrawPx(lp, y, AxisCol);
                for (int x = lp; x < lp + gw; x++) DrawPx(x, tp + gh - 1, AxisCol);

                void DrawHistory(Queue<float> queue, Func<float, float> unInvert, Color color)
                {
                    var arr = queue.ToArray();
                    if (arr.Length < 2) return;
                    for (int i = 1; i < arr.Length; i++)
                    {
                        int x0 = lp + (int)((float)(i - 1) / (HistorySize - 1) * (gw - 1));
                        int y0 = MapY(unInvert(arr[i - 1]));
                        int x1 = lp + (int)((float)i / (HistorySize - 1) * (gw - 1));
                        int y1 = MapY(unInvert(arr[i]));
                        // Simple line via Bresenham
                        Bresenham(buf, stride, w, h, x0, y0, x1, y1, color);
                    }
                }

                float UnInvL(float v) => device.DeviceMotorSettings.LeftInverted ? device.DeviceMotorSettings.LeftMaximumAngle - v : v;
                float UnInvR(float v) => device.DeviceMotorSettings.RightInverted ? device.DeviceMotorSettings.RightMaximumAngle - v : v;

                DrawHistory(_leftHistory, UnInvL, LeftMotorCol);
                DrawHistory(_rightHistory, UnInvR, RightMotorCol);
            }
            bmp.AddDirtyRect(new Int32Rect(0, 0, w, h));
            bmp.Unlock();
            MotorGraphBitmap = bmp;
        }

        // ?? Pixel helpers ?????????????????????????????????????????????????????
        private static unsafe void DrawCurveLine(int* buf, int stride, int w, int h,
            int lp, int gw, Color color, Func<int, int?> mapY)
        {
            int? prevY = null;
            for (int px = 0; px < gw; px++)
            {
                int? y = mapY(px);
                if (y == null) { prevY = null; continue; }
                int cx = lp + px;
                if (prevY.HasValue)
                    Bresenham(buf, stride, w, h, cx - 1, prevY.Value, cx, y.Value, color);
                else
                    SetPx(buf, stride, w, h, cx, y.Value, color);
                prevY = y;
            }
        }

        private static unsafe void DrawDot(int* buf, int stride, int w, int h, int cx, int cy, Color color)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                    if (dx * dx + dy * dy <= 16)
                    {
                        int a = 255 - (int)(Math.Sqrt(dx * dx + dy * dy) / 4f * 100);
                        if (a > 40) SetPx(buf, stride, w, h, cx + dx, cy + dy,
                            Color.FromArgb((byte)a, color.R, color.G, color.B));
                    }
        }

        private static unsafe void Bresenham(int* buf, int stride, int w, int h, int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                SetPx(buf, stride, w, h, x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private static unsafe void SetPx(int* buf, int stride, int w, int h, int x, int y, Color c)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            buf[y * stride + x] = ToInt(c);
        }

        private static int ToInt(Color c) => (c.R << 16) | (c.G << 8) | c.B;

        private static float GetMin(TestMode m) => m switch { TestMode.Surge => SurgeMin, TestMode.Sway => SwayMin, TestMode.Heave => HeaveMin, _ => 0f };
        private static float GetMax(TestMode m) => m switch { TestMode.Surge => SurgeMax, TestMode.Sway => SwayMax, TestMode.Heave => HeaveMax, _ => 0f };
    }
}
