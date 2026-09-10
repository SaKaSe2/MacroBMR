using System.Windows;

namespace MacroBMR
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static System.Threading.Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "MacroBMRAppMutex";
            bool createdNew;

            _mutex = new System.Threading.Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // App is already running!
                MessageBox.Show("Aplikasi Macro sedang berjalan (merekam atau memutar makro) di latar belakang!\n\nKarena aplikasi disembunyikan secara penuh, silakan tekan tombol F12 pada keyboard Anda untuk menghentikan makro dan memunculkannya kembali.", "Aplikasi Sudah Berjalan", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
                return;
            }

            this.DispatcherUnhandledException += (sender, args) =>
            {
                System.IO.File.AppendAllText("crash_log.txt", $"[{System.DateTime.Now}] Error: {args.Exception}\n");
                MessageBox.Show($"Terjadi kesalahan: {args.Exception.Message}", "Error Handled", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);
        }
    }
}
