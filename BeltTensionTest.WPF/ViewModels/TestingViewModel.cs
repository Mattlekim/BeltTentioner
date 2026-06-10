using BeltAPI;
using BeltTensionTest.WPF.Helpers;
using BeltTensionTest.WPF.Services;
using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Threading;

namespace BeltTensionTest.WPF.ViewModels
{
    public class TestingViewModel : ViewModelBase, IDisposable
    {
        private readonly MainViewModel _main;
        private readonly DispatcherTimer _timer;

        private enum TestMode { None, Surge, Sway, Heave }
        private TestMode _mode = TestMode.None;

        private float _sweep;
        private bool  _sweepUp = true;

        private const float SurgeMin = -2f, SurgeMax = 7f;
        private const float SwayMin  = -5f, SwayMax  = 5f;
        private const float HeaveMin = -2f, HeaveMax = 3f;
        private const float StepSize = 0.20f;

        // Motor output history (300 samples)
        public const int HistorySize = 300;
        private readonly Queue<float> _leftHistory  = new();
        private readonly Queue<float> _rightHistory = new();

        // Smoothed live inputs (for graph markers)
        private float _lastSurge, _lastSway, _lastHeave;

        private string _statusText = "Idle";
        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        private bool _surgeActive;
        public bool SurgeActive { get => _surgeActive; set => SetField(ref _surgeActive, value); }
        private bool _swayActive;
        public bool SwayActive  { get => _swayActive;  set => SetField(ref _swayActive,  value); }
        private bool _heaveActive;
        public bool HeaveActive { get => _heaveActive; set => SetField(ref _heaveActive, value); }

        private bool _showBraking = true;
        public bool ShowBraking { get => _showBraking; set { if (SetField(ref _showBraking, value)) GraphNeedsRedraw?.Invoke(); } }
        private bool _showCornering = true;
        public bool ShowCornering { get => _showCornering; set { if (SetField(ref _showCornering, value)) GraphNeedsRedraw?.Invoke(); } }
        private bool _showVertical = true;
        public bool ShowVertical { get => _showVertical; set { if (SetField(ref _showVertical, value)) GraphNeedsRedraw?.Invoke(); } }
        private bool _livePreview = true;
        public bool LivePreview { get => _livePreview; set => SetField(ref _livePreview, value); }

        private string _pitchText = "Pitch: 0.0°";
        public string PitchText { get => _pitchText; set => SetField(ref _pitchText, value); }
        private string _rollText = "Roll: 0.0°";
        public string RollText  { get => _rollText;  set => SetField(ref _rollText,  value); }

        public event Action? GraphNeedsRedraw;
        public event Action? MotorGraphNeedsRedraw;

        public ICommand StartSurgeCommand { get; }
        public ICommand StartSwayCommand  { get; }
        public ICommand StartHeaveCommand { get; }
        public ICommand StopCommand       { get; }

        public TestingViewModel(MainViewModel main)
        {
            _main = main;

            StartSurgeCommand = new RelayCommand(() => StartMode(TestMode.Surge));
            StartSwayCommand  = new RelayCommand(() => StartMode(TestMode.Sway));
            StartHeaveCommand = new RelayCommand(() => StartMode(TestMode.Heave));
            StopCommand       = new RelayCommand(Stop);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _timer.Tick += OnTick;
        }

        private void StartMode(TestMode mode)
        {
            _mode    = mode;
            _sweepUp = true;
            _sweep   = GetMin(mode);
            UpdateHighlights();
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _mode = TestMode.None;
            _main.StopBeltTensionerForces();
            StatusText = "Idle";
            UpdateHighlights();
        }

        private void OnTick(object? s, EventArgs e)
        {
            float min = GetMin(_mode), max = GetMax(_mode);
            if (_sweepUp) { _sweep += StepSize; if (_sweep >= max) { _sweep = max; _sweepUp = false; } }
            else          { _sweep -= StepSize; if (_sweep <= min) { _sweep = min; _sweepUp = true;  } }

            float surge = 0, sway = 0, heave = 0;
            switch (_mode)
            {
                case TestMode.Surge: surge = _sweep; break;
                case TestMode.Sway:  sway  = _sweep; break;
                case TestMode.Heave: heave = _sweep; break;
            }
            _main.UpdateBeltTensionerForces(surge, sway, heave, Rotation.Zero);
            StatusText = $"{_mode}: {_sweep:F2}  [{min:F0}?{max:F0}]";
        }

        public void UpdateLivePreview(float surge, float sway, float heave)
        {
            _lastSurge = _lastSurge * .9f + surge * .1f;
            _lastSway  = _lastSway  * .9f + sway  * .1f;
            _lastHeave = _lastHeave * .9f + heave  * .1f;
            GraphNeedsRedraw?.Invoke();
        }

        public void UpdateMotorOutput(float left, float right, Rotation rotation)
        {
            if (_leftHistory.Count  >= HistorySize) _leftHistory.Dequeue();
            if (_rightHistory.Count >= HistorySize) _rightHistory.Dequeue();
            _leftHistory.Enqueue(left);
            _rightHistory.Enqueue(right);
            PitchText = $"Pitch: {(rotation.Pitch * 180f / MathF.PI):F1}°";
            RollText  = $"Roll:  {(rotation.Roll  * 180f / MathF.PI):F1}°";
            MotorGraphNeedsRedraw?.Invoke();
        }

        public (float surge, float sway, float heave) GetSmoothedInputs() => (_lastSurge, _lastSway, _lastHeave);
        public (Queue<float> left, Queue<float> right) GetMotorHistory()  => (_leftHistory, _rightHistory);

        private void UpdateHighlights()
        {
            SurgeActive = _mode == TestMode.Surge;
            SwayActive  = _mode == TestMode.Sway;
            HeaveActive = _mode == TestMode.Heave;
        }

        private static float GetMin(TestMode m) => m switch { TestMode.Surge => SurgeMin, TestMode.Sway => SwayMin,  TestMode.Heave => HeaveMin, _ => 0 };
        private static float GetMax(TestMode m) => m switch { TestMode.Surge => SurgeMax, TestMode.Sway => SwayMax,  TestMode.Heave => HeaveMax, _ => 0 };

        public void Dispose() { Stop(); }
    }
}
