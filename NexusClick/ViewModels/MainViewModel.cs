using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NexusClick.Services;

namespace NexusClick.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private bool _isRunning;
        private int _hours = 0, _minutes = 0, _seconds = 0, _milliseconds = 100;
        private int _mouseButtonIndex = 0; // 0=Left, 1=Right, 2=Middle
        private int _clickTypeIndex = 0; // 0=Single, 1=Double
        private bool _useRandomization = true;
        private bool _followCursor = true;
        private bool _fixedPosition = false;
        private int _fixedX = 0, _fixedY = 0;
        
        private CancellationTokenSource? _cts;
        private HotkeyService? _hotkeyService;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public MainViewModel()
        {
            ToggleStartCommand = new RelayCommand(ToggleStart);
            PickLocationCommand = new RelayCommand(PickLocation);
            
            // Setup Hotkey setelah Window terbuka
            System.Windows.Application.Current.MainWindow.Loaded += (s, e) =>
            {
                _hotkeyService = new HotkeyService();
                _hotkeyService.OnHotkeyPressed += ToggleStart;
                _hotkeyService.Register(System.Windows.Application.Current.MainWindow);
            };
        }

        // --- PROPERTIES BINDING ---
        public bool IsRunning 
        { 
            get => _isRunning; 
            set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(ActionButtonText)); } 
        }
        
        public string StatusText => IsRunning ? "RUNNING" : "STOPPED";
        public string StatusColor => IsRunning ? "#E53935" : "#0A2540";
        public string ActionButtonText => IsRunning ? "STOP (F8)" : "START (F8)";
        public string HotkeyText => "Global Hotkey: F8";

        public int Hours { get => _hours; set { _hours = value; OnPropertyChanged(); } }
        public int Minutes { get => _minutes; set { _minutes = value; OnPropertyChanged(); } }
        public int Seconds { get => _seconds; set { _seconds = value; OnPropertyChanged(); } }
        public int Milliseconds { get => _milliseconds; set { _milliseconds = value; OnPropertyChanged(); } }
        
        public int MouseButtonIndex { get => _mouseButtonIndex; set { _mouseButtonIndex = value; OnPropertyChanged(); } }
        public int ClickTypeIndex { get => _clickTypeIndex; set { _clickTypeIndex = value; OnPropertyChanged(); } }
        
        public bool UseRandomization { get => _useRandomization; set { _useRandomization = value; OnPropertyChanged(); } }
        public bool FollowCursor { get => _followCursor; set { _followCursor = value; OnPropertyChanged(); } }
        public bool FixedPosition { get => _fixedPosition; set { _fixedPosition = value; OnPropertyChanged(); } }
        public int FixedX { get => _fixedX; set { _fixedX = value; OnPropertyChanged(); } }
        public int FixedY { get => _fixedY; set { _fixedY = value; OnPropertyChanged(); } }

        public ICommand ToggleStartCommand { get; }
        public ICommand PickLocationCommand { get; }

        private void ToggleStart()
        {
            if (IsRunning) StopClicking();
            else StartClicking();
        }

        private async void PickLocation()
        {
            // Tambahkan delay sedikit agar user punya waktu menggerakkan kursor
            await Task.Delay(2000);
            var pos = CursorTracker.GetPosition();
            FixedX = pos.x;
            FixedY = pos.y;
        }

        private async void StartClicking()
        {
            int baseInterval = (Hours * 3600000) + (Minutes * 60000) + (Seconds * 1000) + Milliseconds;
            if (baseInterval <= 0) baseInterval = 10; // Failsafe

            IsRunning = true;
            
            // Sembunyikan window agar tidak mengklik tombol START-nya sendiri
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Minimized;
            });

            // Beri jeda sedikit agar user bisa menyadari bahwa makro mulai berjalan
            await Task.Delay(500);

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            
            var rnd = new Random();

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    int? targetX = FixedPosition ? FixedX : null;
                    int? targetY = FixedPosition ? FixedY : null;

                    MouseSimulator.Click(MouseButtonIndex, ClickTypeIndex == 1, targetX, targetY);

                    int actualInterval = baseInterval;
                    if (UseRandomization) 
                    {
                        // Add humanization (+/- 10 to 40ms)
                        int jitter = rnd.Next(10, 40);
                        actualInterval += (rnd.Next(0, 2) == 0 ? jitter : -jitter);
                        if (actualInterval < 1) actualInterval = 1;
                    }

                    await Task.Delay(actualInterval, token).ConfigureAwait(false);
                }
            }, token);
        }

        private void StopClicking()
        {
            IsRunning = false;
            _cts?.Cancel();

            // Munculkan kembali window saat berhenti
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null && mainWindow.WindowState == System.Windows.WindowState.Minimized)
                {
                    mainWindow.WindowState = System.Windows.WindowState.Normal;
                    mainWindow.Activate();
                }
            });
        }
    }
}
