using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BeltAPI;

namespace belttentiontest
{
    internal partial class TestingForm : Form
    {
        private readonly System.Windows.Forms.Timer _updateTimer;

        private enum TestMode { None, Surge, Sway, Heave }
        private TestMode _currentMode = TestMode.None;

        private float _sweepValue = -2f;
        private bool _sweepUp = true;

        private const float SurgeMin = -2f, SurgeMax = 7f;
        private const float SwayMin  = -5f, SwayMax  = 5f;
        private const float HeaveMin = -2f, HeaveMax = 3f;

        private const float StepSize = 0.20f;

        // Live-preview smoothed inputs
        private float _lastLongForceInput = 0f;
        private float _lastLatForceInput  = 0f;
        private float _lastVertForceInput = 0f;

        private const int MotorHistorySize = 300;
        private readonly Queue<float> _leftMotorHistory  = new Queue<float>();
        private readonly Queue<float> _rightMotorHistory = new Queue<float>();
        private float _lastPitch = 0f;
        private float _lastRoll  = 0f;
        private float _lastYaw   = 0f;

        public TestingForm()
        {
            InitializeComponent();

            _updateTimer = new System.Windows.Forms.Timer { Interval = 60 };
            _updateTimer.Tick += OnTimerTick;

            FormClosed += (_, __) => StopTest();

            _pictureBoxGraph.SizeChanged += (_, __) => DrawCurveGraph();
        }

        private void PictureBoxMotorGraph_SizeChanged(object? sender, EventArgs e) => DrawMotorGraph();

        private void BtnSurge_Click(object? sender, EventArgs e) => StartMode(TestMode.Surge);
        private void BtnSway_Click(object? sender, EventArgs e)  => StartMode(TestMode.Sway);
        private void BtnHeave_Click(object? sender, EventArgs e) => StartMode(TestMode.Heave);
        private void BtnStop_Click(object? sender, EventArgs e)  => StopTest();

        private void StartMode(TestMode mode)
        {
            _currentMode = mode;
            _sweepUp = true;
            _sweepValue = GetMin(mode);
            HighlightActiveButton();
            _updateTimer.Start();
        }

        private void StopTest()
        {
            _updateTimer.Stop();
            _currentMode = TestMode.None;
            Form1.Instance.UpdateBeltTensionerForces(0f, 0f, 0f, Rotation.Zero);
            _lblStatus.Text = "Idle";
            HighlightActiveButton();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            float min = GetMin(_currentMode);
            float max = GetMax(_currentMode);

            if (_sweepUp)
            {
                _sweepValue += StepSize;
                if (_sweepValue >= max) { _sweepValue = max; _sweepUp = false; }
            }
            else
            {
                _sweepValue -= StepSize;
                if (_sweepValue <= min) { _sweepValue = min; _sweepUp = true; }
            }

            float surge = 0f, sway = 0f, heave = 0f;
            switch (_currentMode)
            {
                case TestMode.Surge: surge = _sweepValue; break;
                case TestMode.Sway:  sway  = _sweepValue; break;
                case TestMode.Heave: heave = _sweepValue; break;
            }

            Form1.Instance.UpdateBeltTensionerForces(surge, sway, heave, Rotation.Zero);
            _lblStatus.Text = $"{_currentMode}: {_sweepValue:F2}  [{min:F0} ? {max:F0}]";
        }

        // Called by Form1.UpdateBeltTentionFeedback each tick when live preview is active
        public void UpdateLivePreview(float surge, float sway, float heave)
        {
            _lastLongForceInput = _lastLongForceInput * .9f + surge * .1f;
            _lastLatForceInput  = _lastLatForceInput  * .9f + sway  * .1f;
            _lastVertForceInput = _lastVertForceInput * .9f + heave * .1f;
            DrawCurveGraph();
        }

        // Called by Form1.UpdateBeltTentionFeedback each tick with actual motor outputs and rotation
        public void UpdateMotorOutput(float left, float right, BeltAPI.Rotation rotation)
        {
            if (_leftMotorHistory.Count >= MotorHistorySize)  _leftMotorHistory.Dequeue();
            if (_rightMotorHistory.Count >= MotorHistorySize) _rightMotorHistory.Dequeue();
            _leftMotorHistory.Enqueue(left);
            _rightMotorHistory.Enqueue(right);
            _lastPitch = rotation.Pitch;
            _lastRoll  = rotation.Roll;
            _lastYaw   = rotation.Yaw;
            DrawMotorGraph();
            UpdateRotationLabels();
        }

        private void UpdateRotationLabels()
        {
            if (_lblPitch.InvokeRequired)
            {
                _lblPitch.BeginInvoke(new Action(UpdateRotationLabels));
                return;
            }
            _lblPitch.Text = $"Pitch: {(_lastPitch * 180f / MathF.PI):F1}°";
            _lblRoll.Text  = $"Roll:  {(_lastRoll  * 180f / MathF.PI):F1}°";
        
        }

        public void DrawMotorGraph()
        {
            if (_pictureBoxMotorGraph == null || _pictureBoxMotorGraph.Width <= 0 || _pictureBoxMotorGraph.Height <= 0)
                return;

            var device = Form1.Instance.BeltTentionerDevice;
            if (device == null) return;

            int width  = _pictureBoxMotorGraph.Width;
            int height = _pictureBoxMotorGraph.Height;

            var bgColor    = Color.FromArgb(18,  18,  30);
            var gridColor  = Color.FromArgb(38,  38,  58);
            var axisColor  = Color.FromArgb(70,  70, 100);
            var labelColor = Color.FromArgb(160, 160, 190);
            var leftColor  = Color.FromArgb(100, 200, 255);
            var rightColor = Color.FromArgb(255, 140, 80);

            int leftPad   = 48;
            int rightPad  = 12;
            int topPad    = 18;
            int bottomPad = 22;
            int graphW    = width  - leftPad - rightPad;
            int graphH    = height - topPad  - bottomPad;
            if (graphW <= 0 || graphH <= 0) return;

            float minV = device.DeviceMotorSettings.LeftMinimumAngle;
            float maxV = device.DeviceMotorSettings.LeftMaximumAngle;
            if (minV > maxV) (minV, maxV) = (maxV, minV);
            float range = maxV - minV;
            if (range == 0) range = 1;

            int MapY(float v)
            {
                float clamped = Math.Clamp(v, minV, maxV);
                return topPad + (int)((1f - (clamped - minV) / range) * (graphH - 1));
            }

            var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(bgColor);

            var labelFont  = new Font("Segoe UI", 7.5f);
            var labelBrush = new SolidBrush(labelColor);

            // Grid
            using (var gridPen = new Pen(gridColor, 1))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = topPad + i * (graphH - 1) / 4;
                    g.DrawLine(gridPen, leftPad, y, leftPad + graphW - 1, y);
                    float val = maxV - i * range / 4f;
                    var lbl = val.ToString("F0");
                    var sz  = g.MeasureString(lbl, labelFont);
                    g.DrawString(lbl, labelFont, labelBrush, leftPad - sz.Width - 2, y - sz.Height / 2);
                }
            }

            // Axes
            using var axisPen = new Pen(axisColor, 1);
            g.DrawLine(axisPen, leftPad, topPad, leftPad, topPad + graphH - 1);
            g.DrawLine(axisPen, leftPad, topPad + graphH - 1, leftPad + graphW - 1, topPad + graphH - 1);

            // Title
            g.TranslateTransform(10, topPad + graphH / 2);
            g.RotateTransform(-90);
            var yLbl  = "Motor Output";
            var ySize = g.MeasureString(yLbl, labelFont);
            g.DrawString(yLbl, labelFont, labelBrush, -ySize.Width / 2, -ySize.Height / 2);
            g.ResetTransform();
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Un-invert a stored serial value back into logical output space for display
            float UnInvertLeft(float v)  => device.DeviceMotorSettings.LeftInverted  ? device.DeviceMotorSettings.LeftMaximumAngle  - v : v;
            float UnInvertRight(float v) => device.DeviceMotorSettings.RightInverted ? device.DeviceMotorSettings.RightMaximumAngle - v : v;

            // Draw history lines
            void DrawHistory(Queue<float> history, Func<float, float> unInvert, Color color)
            {
                var arr = history.ToArray();
                if (arr.Length < 2) return;
                var pts = new System.Drawing.Point[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    int x = leftPad + (int)((float)i / (MotorHistorySize - 1) * (graphW - 1));
                    pts[i] = new System.Drawing.Point(x, MapY(unInvert(arr[i])));
                }
                using var glow = new Pen(Color.FromArgb(50, color), 5);
                g.DrawLines(glow, pts);
                using var pen = new Pen(color, 2);
                g.DrawLines(pen, pts);
            }

            DrawHistory(_leftMotorHistory,  UnInvertLeft,  leftColor);
            DrawHistory(_rightMotorHistory, UnInvertRight, rightColor);

            // Legend
            var legendFont = new Font("Segoe UI", 8f, FontStyle.Bold);
            g.FillRectangle(new SolidBrush(leftColor),  leftPad + graphW - 120, topPad + 4, 12, 12);
            g.DrawString("Left",  legendFont, new SolidBrush(leftColor),  leftPad + graphW - 105, topPad + 3);
            g.FillRectangle(new SolidBrush(rightColor), leftPad + graphW - 60,  topPad + 4, 12, 12);
            g.DrawString("Right", legendFont, new SolidBrush(rightColor), leftPad + graphW - 45,  topPad + 3);

            // X-axis label
            var xLbl   = "← History";
            var xLblSz = g.MeasureString(xLbl, labelFont);
            g.DrawString(xLbl, labelFont, labelBrush, leftPad + (graphW - xLblSz.Width) / 2, topPad + graphH + 4);

            var old = _pictureBoxMotorGraph.Image;
            if (_pictureBoxMotorGraph.InvokeRequired)
                _pictureBoxMotorGraph.BeginInvoke(new Action(() => { _pictureBoxMotorGraph.Image = bmp; old?.Dispose(); }));
            else
            { _pictureBoxMotorGraph.Image = bmp; old?.Dispose(); }
        }

        private void _panelGraphControls_Paint(object? sender, PaintEventArgs e)
        {
            var panel = sender as System.Windows.Forms.Panel;
            using var pen = new Pen(Color.FromArgb(70, 70, 100), 1);
            e.Graphics.DrawLine(pen, 0, 0, panel?.Width ?? 0, 0);
        }

        public void DrawCurveGraph()
        {
            if (_pictureBoxGraph == null || _pictureBoxGraph.Width <= 0 || _pictureBoxGraph.Height <= 0)
                return;

            var device = Form1.Instance.BeltTentionerDevice;
            if (device == null) return;

            CarSettings? carSettings = CarSettingsDatabase.Instance.CurrentSettings;
            if (carSettings == null) return;

            // ── palette ───────────────────────────────────────────────────
            var bgColor      = Color.FromArgb(18,  18,  30);
            var gridColor    = Color.FromArgb(38,  38,  58);
            var axisColor    = Color.FromArgb(70,  70, 100);
            var labelColor   = Color.FromArgb(160, 160, 190);
            var surgeColor   = Color.FromArgb(100, 160, 255);
            var swayColor    = Color.FromArgb( 80, 200, 120);
            var heaveColor   = Color.FromArgb(255, 165,  60);
            var maxLineColor = Color.FromArgb(220,  60,  60);
            var restColor    = Color.FromArgb( 60, 180, 180);

            int width       = _pictureBoxGraph.Width;
            int height      = _pictureBoxGraph.Height;
            int leftPad     = 44;   // room for Y-axis label
            int rightPad    = 28;
            int topPad      = 12;
            int bottomPad   = 28;   // room for X-axis labels
            int graphWidth  = width  - leftPad - rightPad;
            int graphHeight = height - topPad  - bottomPad;

            var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(bgColor);

            float minV      = device.DeviceMotorSettings.LeftMinimumAngle;
            float maxV      = device.DeviceMotorSettings.LeftMaximumAngle;
            if (minV > maxV) (minV, maxV) = (maxV, minV);
            float motorRange = maxV - minV;
            if (motorRange == 0) motorRange = 1;

            // Map a motor-output value → pixel Y inside the graph area
            int MapY(float v)
            {
                float clamped = Math.Clamp(v, minV, maxV);
                return topPad + (int)((1f - (clamped - minV) / motorRange) * (graphHeight - 1));
            }
            // Map a pixel X → G-force input value
            float MapXToInput(int px) => (float)(px - leftPad) / (graphWidth - 1) * 9f - 2f;
            // Map a G-force input → pixel X
            int MapInputToX(float f) => leftPad + (int)((f + 2f) / 9f * (graphWidth - 1));

            var labelFont = new Font("Segoe UI", 7.5f);
            var labelBrush = new SolidBrush(labelColor);

            // ── grid lines ────────────────────────────────────────────────
            using (var gridPen = new Pen(gridColor, 1))
            {
                // Horizontal grid (5 lines)
                for (int i = 0; i <= 4; i++)
                {
                    int y = topPad + i * (graphHeight - 1) / 4;
                    g.DrawLine(gridPen, leftPad, y, leftPad + graphWidth - 1, y);
                }
                // Vertical grid at each G integer
                for (int gVal = -2; gVal <= 7; gVal++)
                {
                    int x = MapInputToX(gVal);
                    if (x < leftPad || x > leftPad + graphWidth - 1) continue;
                    g.DrawLine(gridPen, x, topPad, x, topPad + graphHeight - 1);
                }
            }

            // ── axes ──────────────────────────────────────────────────────
            using var axisPen = new Pen(axisColor, 1);
            g.DrawLine(axisPen, leftPad, topPad, leftPad, topPad + graphHeight - 1);                          // Y axis
            g.DrawLine(axisPen, leftPad, topPad + graphHeight - 1, leftPad + graphWidth - 1, topPad + graphHeight - 1); // X axis

            // ── X-axis tick labels ────────────────────────────────────────
            for (int gVal = -2; gVal <= 7; gVal++)
            {
                int    x     = MapInputToX(gVal);
                if (x < leftPad || x > leftPad + graphWidth - 1) continue;
                string lbl   = gVal.ToString();
                var    sz    = g.MeasureString(lbl, labelFont);
                g.DrawString(lbl, labelFont, labelBrush, x - sz.Width / 2, topPad + graphHeight + 4);
            }

            // ── Y-axis label (rotated) ────────────────────────────────────
            g.TranslateTransform(10, topPad + graphHeight / 2);
            g.RotateTransform(-90);
            var yLbl  = "Motor Output";
            var ySize = g.MeasureString(yLbl, labelFont);
            g.DrawString(yLbl, labelFont, labelBrush, -ySize.Width / 2, -ySize.Height / 2);
            g.ResetTransform();
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // ── max-power line ────────────────────────────────────────────
            float maxOutputVal = minV + motorRange * (carSettings.MaxPower / 100f);
            int   yMax         = MapY(maxOutputVal);
            using (var maxPen = new Pen(maxLineColor, 1) { DashStyle = DashStyle.Dash })
                g.DrawLine(maxPen, leftPad, yMax, leftPad + graphWidth - 1, yMax);
            var maxLblSz = g.MeasureString("Max", labelFont);
            g.DrawString("Max", labelFont, new SolidBrush(maxLineColor), leftPad + graphWidth - maxLblSz.Width - 2, yMax - maxLblSz.Height);

            // ── resting-point line ────────────────────────────────────────
            float restingVal = device.DeviceMotorSettings.ClampToMaxMotorPower(carSettings.RestingPoint / 100f, 0, carSettings).Item1;
            int   yRest      = MapY(restingVal);
            using (var restPen = new Pen(restColor, 1) { DashStyle = DashStyle.Dot })
                g.DrawLine(restPen, leftPad, yRest, leftPad + graphWidth - 1, yRest);

            // Helper: build points array and a matching fill polygon for a curve
            static (System.Drawing.Point[] pts, System.Drawing.Point[] poly) BuildCurve(
                List<System.Drawing.Point> pts, int baseY, int lx)
            {
                if (pts.Count < 2) return (pts.ToArray(), System.Array.Empty<System.Drawing.Point>());
                var poly = new System.Drawing.Point[pts.Count + 2];
                poly[0] = new System.Drawing.Point(pts[0].X, baseY);
                for (int i = 0; i < pts.Count; i++) poly[i + 1] = pts[i];
                poly[^1] = new System.Drawing.Point(pts[^1].X, baseY);
                return (pts.ToArray(), poly);
            }

            int baselineY = topPad + graphHeight - 1;

            // ── Surge curve ───────────────────────────────────────────────
            if (_cbShowBraking.Checked)
            {
                var pts = new List<System.Drawing.Point>();
                for (int px = 0; px < graphWidth; px++)
                {
                    float inp = MapXToInput(leftPad + px);
                    if (inp < -CarSettings.SurgeGForceScale || inp > CarSettings.SurgeGForceScale) continue;
                    bool inv = carSettings.InvertSurge; carSettings.InvertSurge = false;
                    var  mo  = device.SetupMotorsForData(Math.Max(inp, 0), 0, 0, carSettings, Rotation.Zero);
                    float raw = mo.CalculateDataForGraph(device, carSettings, false);
                    float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(raw + carSettings.RestingPoint / 100f, 0, carSettings).Item1;
                    carSettings.InvertSurge = inv;
                    pts.Add(new System.Drawing.Point(leftPad + px, MapY(yv)));
                }
                if (pts.Count >= 2)
                {
                    var (line, fill) = BuildCurve(pts, baselineY, leftPad);
                    using var fillBrush = new SolidBrush(Color.FromArgb(35, surgeColor));
                    g.FillPolygon(fillBrush, fill);
                    // glow pass
                    using var glowPen = new Pen(Color.FromArgb(50, surgeColor), 5);
                    g.DrawLines(glowPen, line);
                    using var curvePen = new Pen(surgeColor, 2);
                    g.DrawLines(curvePen, line);
                }
            }

            // ── Sway curve ────────────────────────────────────────────────
            if (_cbShowCorn.Checked)
            {
                var pts = new List<System.Drawing.Point>();
                for (int px = 0; px < graphWidth; px++)
                {
                    float inp = MapXToInput(leftPad + px);
                    if (inp < 0 || inp > CarSettings.SwayGForceScale) continue;
                    bool inv = carSettings.InvertSway; carSettings.InvertSway = false;
                    var  mo  = device.SetupMotorsForData(0, inp, 0, carSettings, Rotation.Zero);
                    mo.CalculateDataForGraph(device, carSettings, false);
                    float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(
                        mo.RightSwayOutput + carSettings.RestingPoint / 100f, 0, carSettings).Item1;
                    carSettings.InvertSway = inv;
                    pts.Add(new System.Drawing.Point(leftPad + px, MapY(yv)));
                }
                if (pts.Count >= 2)
                {
                    var (line, fill) = BuildCurve(pts, baselineY, leftPad);
                    using var fillBrush = new SolidBrush(Color.FromArgb(35, swayColor));
                    g.FillPolygon(fillBrush, fill);
                    using var glowPen = new Pen(Color.FromArgb(50, swayColor), 5);
                    g.DrawLines(glowPen, line);
                    using var curvePen = new Pen(swayColor, 2);
                    g.DrawLines(curvePen, line);
                }
            }

            // ── Heave curve ───────────────────────────────────────────────
            if (_cbShowVer.Checked)
            {
                var pts = new List<System.Drawing.Point>();
                for (int px = 0; px < graphWidth; px++)
                {
                    float inp = MapXToInput(leftPad + px);
                    if (inp < -CarSettings.HeaveGForceScale || inp > CarSettings.HeaveGForceScale) continue;
                    bool inv = carSettings.InvertHeave; carSettings.InvertHeave = false;
                    var  mo  = device.DeviceMotorSettings.Setup(0, 0, inp, CarSettingsDatabase.Instance.CurrentSettings, Rotation.Zero);
                    float raw = mo.CalculateDataForGraph(device, carSettings, false);
                    float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(raw + carSettings.RestingPoint / 100f, 0, carSettings).Item1;
                    carSettings.InvertHeave = inv;
                    pts.Add(new System.Drawing.Point(leftPad + px, MapY(yv)));
                }
                if (pts.Count >= 2)
                {
                    var (line, fill) = BuildCurve(pts, baselineY, leftPad);
                    using var fillBrush = new SolidBrush(Color.FromArgb(35, heaveColor));
                    g.FillPolygon(fillBrush, fill);
                    using var glowPen = new Pen(Color.FromArgb(50, heaveColor), 5);
                    g.DrawLines(glowPen, line);
                    using var curvePen = new Pen(heaveColor, 2);
                    g.DrawLines(curvePen, line);
                }
            }

            // ── live-preview markers + combined bar ───────────────────────
            if (_cbLivePreview.Checked)
            {
                if (_cbShowBraking.Checked)
                {
                    float inp = Math.Max(_lastLongForceInput, 0);
                    bool  inv = carSettings.InvertSurge; carSettings.InvertSurge = false;
                    var   mo  = device.SetupMotorsForData(inp, 0, 0, carSettings, Rotation.Zero);
                    float raw = mo.CalculateDataForGraph(device, carSettings, false);
                    float yv  = device.DeviceMotorSettings.ClampToMaxMotorPower(raw + carSettings.RestingPoint / 100f, 0, carSettings).Item1;
                    carSettings.InvertSurge = inv;
                    int mx = MapInputToX(inp), my = MapY(yv);
                    using var glow = new SolidBrush(Color.FromArgb(60, surgeColor));
                    g.FillEllipse(glow, mx - 8, my - 8, 16, 16);
                    g.FillEllipse(new SolidBrush(surgeColor), mx - 4, my - 4, 8, 8);
                }
                if (_cbShowCorn.Checked)
                {
                    float inp = Math.Max(_lastLatForceInput, 0);
                    bool  inv = carSettings.InvertSway; carSettings.InvertSway = false;
                    var   mo  = device.SetupMotorsForData(0, inp, 0, carSettings, Rotation.Zero);
                    mo.CalculateDataForGraph(device, carSettings, false);
                    float yv = device.DeviceMotorSettings.ClampToMaxMotorPower(
                        mo.RightSwayOutput + carSettings.RestingPoint / 100f, 0, carSettings).Item1;
                    carSettings.InvertSway = inv;
                    int mx = MapInputToX(inp), my = MapY(yv);
                    using var glow = new SolidBrush(Color.FromArgb(60, swayColor));
                    g.FillEllipse(glow, mx - 8, my - 8, 16, 16);
                    g.FillEllipse(new SolidBrush(swayColor), mx - 4, my - 4, 8, 8);
                }
                if (_cbShowVer.Checked)
                {
                    float inp = _lastVertForceInput;
                    if (Form1.ApplicatoinSettings.UseIracing)
                        inp--;

                    bool  inv = carSettings.InvertHeave; carSettings.InvertHeave = false;
                    var   mo  = device.SetupMotorsForData(0, 0, inp, carSettings, Rotation.Zero);
                    float raw = mo.CalculateDataForGraph(device, carSettings, false);
                    float yv  = device.DeviceMotorSettings.ClampToMaxMotorPower(raw + carSettings.RestingPoint / 100f, 0, carSettings).Item1;
                    carSettings.InvertHeave = inv;
                    int mx = MapInputToX(inp), my = MapY(yv);
                    using var glow = new SolidBrush(Color.FromArgb(60, heaveColor));
                    g.FillEllipse(glow, mx - 8, my - 8, 16, 16);
                    g.FillEllipse(new SolidBrush(heaveColor), mx - 4, my - 4, 8, 8);
                }

                // Combined output bar (right side)
                var   combined      = device.DeviceMotorSettings.Setup(_lastLongForceInput, _lastLatForceInput, _lastVertForceInput, CarSettingsDatabase.Instance.CurrentSettings, Rotation.Zero);
                float combinedValue = Math.Abs(combined.CalculateDataToSerail(device, CarSettingsDatabase.Instance.CurrentSettings));
                float barMaxVal     = minV + motorRange * (CarSettingsDatabase.Instance.CurrentSettings.MaxPower / 100f);
                float barPercent    = Math.Clamp((combinedValue - minV) / (barMaxVal - minV), 0f, 1f);
                int   barMaxHeight  = (int)((barMaxVal - minV) / motorRange * (graphHeight - 1));
                int   barH          = (int)(barPercent * barMaxHeight);
                int   barW          = 14;
                int   barX          = leftPad + graphWidth + 6;
                int   barTop        = topPad + graphHeight - 1 - barH;
                using var barBrush  = new LinearGradientBrush(
                    new Rectangle(barX, topPad, barW, graphHeight),
                    Color.FromArgb(80, 200, 80), Color.FromArgb(220, 60, 60),
                    LinearGradientMode.Vertical);
                g.FillRectangle(barBrush, barX, barTop, barW, barH);
                using var barBorder = new Pen(axisColor, 1);
                g.DrawRectangle(barBorder, barX, topPad + graphHeight - 1 - barMaxHeight, barW, barMaxHeight);
            }

            var old = _pictureBoxGraph.Image;
            _pictureBoxGraph.Image = bmp;
            old?.Dispose();
        }

        private void _cbShowBraking_CheckedChanged(object? sender, EventArgs e) => DrawCurveGraph();
        private void _cbShowCorn_CheckedChanged(object? sender, EventArgs e)    => DrawCurveGraph();
        private void _cbShowVer_CheckedChanged(object? sender, EventArgs e)     => DrawCurveGraph();

        private static float GetMin(TestMode mode) => mode switch
        {
            TestMode.Surge => SurgeMin,
            TestMode.Sway  => SwayMin,
            TestMode.Heave => HeaveMin,
            _              => 0f,
        };

        private static float GetMax(TestMode mode) => mode switch
        {
            TestMode.Surge => SurgeMax,
            TestMode.Sway  => SwayMax,
            TestMode.Heave => HeaveMax,
            _              => 0f,
        };

        private void HighlightActiveButton()
        {
            _btnSurge.FlatAppearance.BorderSize = _currentMode == TestMode.Surge ? 3 : 0;
            _btnSway.FlatAppearance.BorderSize  = _currentMode == TestMode.Sway  ? 3 : 0;
            _btnHeave.FlatAppearance.BorderSize = _currentMode == TestMode.Heave ? 3 : 0;
            _btnStop.FlatAppearance.BorderSize  = 0;

            _btnSurge.FlatAppearance.BorderColor = Color.White;
            _btnSway.FlatAppearance.BorderColor  = Color.White;
            _btnHeave.FlatAppearance.BorderColor = Color.White;
        }
    }
}

