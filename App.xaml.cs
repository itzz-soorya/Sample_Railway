using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace UserModule
{
    public partial class App : Application
    {
        private bool _isSyncInProgress;
        private System.Timers.Timer? _syncTimer;
        private bool _lastKnownInternetStatus = false;
        private static Mutex? _mutex = null;

        // Windows API to attach console
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        public App()
        {
            // Attach console window for debugging
            //if (GetConsoleWindow() == IntPtr.Zero)
            //{
            //    AllocConsole();
            //}
            
            //Console.WriteLine("====================================");
            //Console.WriteLine("    RAILAX - Console Logging Active");
            //Console.WriteLine("====================================\n");
            
            // Global exception handlers to prevent crashes
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Log($"Unhandled UI Exception: {e.Exception.Message}\n{e.Exception.StackTrace}");
            MessageBox.Show($"An error occurred: {e.Exception.Message}\n\nThe application will continue running.", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            Logger.Log($"Unhandled Domain Exception: {exception?.Message}\n{exception?.StackTrace}");
            MessageBox.Show($"A critical error occurred: {exception?.Message}\n\nPlease restart the application.", 
                "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Log($"Unhandled Task Exception: {e.Exception.Message}\n{e.Exception.StackTrace}");
            e.SetObserved();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Single instance check
            const string appName = "RailaxBookingApp";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // Application is already running - silently exit
                Current.Shutdown();
                return;
            }

            try
            {
                base.OnStartup(e);
                Logger.Log("Application starting...");
                NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
                await CheckInitialNetworkStatusAsync();
                
                // Start periodic sync check every 10 seconds to continuously monitor internet
                StartPeriodicSyncCheck();
                Logger.Log("Application started successfully");
            }
            catch (Exception ex)
            {
                Logger.Log($"Startup error: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Failed to start application: {ex.Message}", "Startup Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _syncTimer?.Stop();
            _syncTimer?.Dispose();
            base.OnExit(e);
        }

        private void StartPeriodicSyncCheck()
        {
            _syncTimer = new System.Timers.Timer(10000); // Check every 10 seconds
            _syncTimer.Elapsed += async (sender, e) =>
            {
                // Continuously check actual internet connectivity, not just network adapter status
                bool hasInternet = await CheckActualInternetConnectivityAsync();
                
                if (hasInternet && !_isSyncInProgress)
                {
                    // Check if we have offline data to sync
                    int pendingCount = await OfflineBookingStorage.GetPendingSyncCountAsync();
                    if (pendingCount > 0)
                    {
                        Logger.Log($"Periodic check: Found {pendingCount} pending bookings. Starting sync...");
                        await UploadOfflineData(showMessages: false);
                    }
                }
                
                // Track internet status changes
                if (hasInternet && !_lastKnownInternetStatus)
                {
                    // Internet just became available
                    Logger.Log("Internet connection detected by periodic check. Triggering sync...");
                    _lastKnownInternetStatus = true;
                    await UploadOfflineData(showMessages: false);
                }
                else if (!hasInternet && _lastKnownInternetStatus)
                {
                    // Internet just became unavailable
                    Logger.Log("Internet connection lost detected by periodic check.");
                    _lastKnownInternetStatus = false;
                }
            };
            _syncTimer.AutoReset = true;
            _syncTimer.Start();
        }

        private async Task<bool> CheckActualInternetConnectivityAsync()
        {
            // First check if network adapter is available
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return false;
            }

            // Then verify actual internet connectivity by pinging Google DNS
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 3000); // 3 second timeout
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                // If ping fails, try alternative check
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync("1.1.1.1", 3000); // Cloudflare DNS
                    return reply.Status == IPStatus.Success;
                }
                catch
                {
                    return false;
                }
            }
        }

    private async void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (!e.IsAvailable)
            {
                // ShowMessage("📴 No internet connection.\nData will be stored locally and synced when connection is restored.", "Network Lost", MessageBoxImage.Warning);
                return;
            }

            // Show message when connection restored and sync
            // ShowMessage("✅ Internet connection restored.\nSyncing offline data...", "Network Connected", MessageBoxImage.Information);
            await HandleNetworkAvailableAsync(showMessages: true);
        }

        private async Task<bool> CheckPingAsync(string host)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 2000);

                // Ping < 200ms → good internet
                return reply.Status == IPStatus.Success && reply.RoundtripTime < 200;
            }
            catch
            {
                return false;
            }
        }

        private async Task UploadOfflineData(bool showMessages = true)
        {
            if (_isSyncInProgress)
            {
                return;
            }

            _isSyncInProgress = true;

            await Task.Delay(500);
            try
            {
                // Sync new offline bookings (IsSynced = 0)
                int newSynced = await OfflineBookingStorage.SyncAllOfflineBookingsAsync(showMessages: showMessages);

                // Sync updated bookings (IsSynced = 2) - completed/payment updates
                int updatedSynced = await OfflineBookingStorage.SyncUpdatedBookingsAsync(showMessages: showMessages);
                
                if (showMessages && (newSynced > 0 || updatedSynced > 0))
                {
                    // ShowMessage($"✅ Offline data synced successfully!\n\nNew bookings: {newSynced}\nUpdated bookings: {updatedSynced}", 
                    //     "Sync Complete", MessageBoxImage.Information);
                }
            }
            catch (Exception)
            {
                if (showMessages)
                {
                    // ShowMessage($"Sync encountered an issue:\n{ex.Message}", "Sync Issue", MessageBoxImage.Warning);
                }
            }
            finally
            {
                _isSyncInProgress = false;
            }
        }

        private void ShowMessage(string text, string title, MessageBoxImage icon)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(text, title, MessageBoxButton.OK, icon);
            });
        }

        private async Task CheckInitialNetworkStatusAsync()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                // Don't show message on startup if offline - just silently use offline mode
                return;
            }

            // Silently sync in background on startup
            await HandleNetworkAvailableAsync(showMessages: false);
        }

        private async Task HandleNetworkAvailableAsync(bool showMessages = true)
        {
            // Only sync if we have any offline data to sync
            await UploadOfflineData(showMessages);
        }
    }
}
