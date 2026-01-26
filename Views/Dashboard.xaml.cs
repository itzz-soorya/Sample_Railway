using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using UserModule.Models;

namespace UserModule
{
    public partial class Dashboard : UserControl
    {
        public ObservableCollection<Booking1> Bookings { get; set; } = new ObservableCollection<Booking1>();
        private List<Booking1> allBookings = new List<Booking1>(); // Store all bookings for filtering

        private Dictionary<string, int> bookingTypeCounts = new Dictionary<string, int>();
        private Dictionary<string, TextBlock> typeTextBlocks = new Dictionary<string, TextBlock>();
        private string currentFilter = "All"; // Track current filter state
        private DispatcherTimer? refreshTimer;

        public Dashboard()
        {
            InitializeComponent();
            DataContext = this;
            BookingDataGrid.ItemsSource = Bookings;

            // Start timer to refresh converters every second
            refreshTimer = new DispatcherTimer();
            refreshTimer.Interval = TimeSpan.FromSeconds(1);
            refreshTimer.Tick += (s, e) => RefreshDataGridView();
            refreshTimer.Start();

            try
            {
                LoadBookings();
                DateTextBlock.Text = DateTime.Now.ToString("MMMM d, yyyy");

                // Get username from LocalStorage instead of hardcoded "User"
                string? username = LocalStorage.GetItem("username");
                if (string.IsNullOrEmpty(username))
                {
                    username = "User"; // Fallback if not found
                }
                UpdateGreeting(username);

                // No longer needed - we have fixed badges in XAML
                // InitializeBookingTypeCounts();
                UpdateCountsFromBookings();

                // Fetch worker balance on load
                _ = FetchWorkerBalanceAsync();

                // Internet status monitoring is handled by Header control
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show("An error occurred while loading the dashboard.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshDataGridView()
        {
            // Force DataGrid to refresh bindings/triggers
            if (BookingDataGrid != null && BookingDataGrid.ItemsSource is INotifyCollectionChanged)
            {
                BookingDataGrid.InvalidateVisual();
            }
        }

        public void LoadBookings()
        {
            try
            {
                Bookings.Clear();
                allBookings.Clear(); // Clear stored bookings
                var localBookings = OfflineBookingStorage.GetBasicBookings();

                if (localBookings != null && localBookings.Any())
                {
                    // Get current worker ID
                    string? currentWorkerId = LocalStorage.GetItem("workerId");
                    
                    // Filter to only show ACTIVE bookings created in the last 2 weeks
                    DateTime twoWeeksAgo = DateTime.Now.AddDays(-14);
                    
                    allBookings = localBookings
                        .Where(b => b.created_at.HasValue && b.created_at.Value >= twoWeeksAgo
                                    && b.status?.ToLower() == "active"
                                    && (b.worker_id == currentWorkerId || true))
                        .OrderByDescending(b => b.created_at)
                        .ToList();
                    
                    Logger.Log($"Loaded {allBookings.Count} bookings created in the last 2 weeks (since {twoWeeksAgo:yyyy-MM-dd})");
                    
                    // Apply current filter
                    ApplyFilter();

                    // Update totals (count and amount)
                    UpdateTotalsFromBookings();
                }
                else
                {
                    Logger.Log("No bookings found in local database.");

                    // Ensure totals show zero
                    UpdateTotalsFromBookings();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show("Failed to load bookings from local database. Please try again later", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Update total bookings count and worker balance UI
        private void UpdateTotalsFromBookings()
        {
            try
            {
                if (TotalBookingsTextBox == null || TotalAmountTextBox == null)
                    return;

                // Get the last balance reset timestamp
                string? lastResetStr = LocalStorage.GetItem("lastBalanceResetTime");
                DateTime? lastResetTime = null;
                
                if (!string.IsNullOrEmpty(lastResetStr) && DateTime.TryParse(lastResetStr, out DateTime parsedDate))
                {
                    lastResetTime = parsedDate;
                }

                // Count only bookings after the last reset
                int totalBookings = 0;
                if (allBookings != null && allBookings.Any())
                {
                    if (lastResetTime.HasValue)
                    {
                        // Count bookings created after the last reset
                        totalBookings = allBookings.Count(b => b.created_at.HasValue && b.created_at.Value > lastResetTime.Value);
                    }
                    else
                    {
                        // No reset yet, count all bookings
                        totalBookings = allBookings.Count;
                    }
                }

                TotalBookingsTextBox.Text = $"Total Bookings: {totalBookings}";
                
                // Worker balance is fetched separately via API
                // TotalAmountTextBox will be updated by FetchWorkerBalanceAsync()
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        // Fetch worker balance from API
        private async Task FetchWorkerBalanceAsync()
        {
            try
            {
                if (TotalAmountTextBox == null)
                    return;

                string? workerId = LocalStorage.GetItem("workerId");
                string? adminId = LocalStorage.GetItem("adminId");

                if (string.IsNullOrEmpty(workerId) || string.IsNullOrEmpty(adminId))
                {
                    TotalAmountTextBox.Text = "Worker Balance: ₹0.00";
                    return;
                }

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                
                string apiUrl = $"https://railway-api-worker.artechnology.pro/api/Settings/worker-balance/{workerId}/{adminId}";
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var balanceData = JsonSerializer.Deserialize<JsonElement>(jsonString);
                    
                    if (balanceData.TryGetProperty("balance", out JsonElement balanceElement))
                    {
                        decimal balance = balanceElement.GetDecimal();
                        TotalAmountTextBox.Text = $"Worker Balance: ₹{balance:F2}";
                        
                        // If balance is 0, reset the booking count by storing current timestamp
                        if (balance == 0)
                        {
                            LocalStorage.SetItem("lastBalanceResetTime", DateTime.Now.ToString("o"));
                            // Update the booking count display immediately
                            UpdateTotalsFromBookings();
                        }
                    }
                    else
                    {
                        TotalAmountTextBox.Text = "Worker Balance: ₹0.00";
                        LocalStorage.SetItem("lastBalanceResetTime", DateTime.Now.ToString("o"));
                        UpdateTotalsFromBookings();
                    }
                }
                else
                {
                    TotalAmountTextBox.Text = "Worker Balance: ₹0.00";
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                TotalAmountTextBox.Text = "Worker Balance: ₹0.00";
            }
        }

        private void ApplyFilter()
        {
            Bookings.Clear();
            
            IEnumerable<Booking1> filteredBookings = allBookings;

            if (currentFilter == "Sitting")
            {
                filteredBookings = allBookings.Where(b => b.booking_type?.ToLower() == "sitting");
            }
            else if (currentFilter == "Sleeper")
            {
                filteredBookings = allBookings.Where(b => b.booking_type?.ToLower() == "sleeper");
            }
            // "All" shows everything, no filter needed

            foreach (var booking in filteredBookings)
            {
                Bookings.Add(booking);
            }

            BookingDataGrid.ItemsSource = Bookings;
        }

        private void FilterAll_Click(object sender, RoutedEventArgs e)
        {
            currentFilter = "All";
            UpdateFilterButtons();
            ApplyFilter();
            Logger.Log("Filter: All bookings");
        }

        private void FilterSleeper_Click(object sender, RoutedEventArgs e)
        {
            currentFilter = "Sleeper";
            UpdateFilterButtons();
            ApplyFilter();
            Logger.Log("Filter: Sleeper bookings only");
        }

        private void FilterSitting_Click(object sender, RoutedEventArgs e)
        {
            currentFilter = "Sitting";
            UpdateFilterButtons();
            ApplyFilter();
            Logger.Log("Filter: Sitting bookings only");
        }

        private void UpdateFilterButtons()
        {
            // Reset all buttons to inactive state
            btnAllFilter.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));
            btnAllFilter.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
            
            btnSittingFilter.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));
            btnSittingFilter.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
            
            btnSleeperFilter.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));
            btnSleeperFilter.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));

            // Set active button
            if (currentFilter == "All")
            {
                btnAllFilter.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#232323"));
                btnAllFilter.Foreground = Brushes.White;
            }
            else if (currentFilter == "Sitting")
            {
                btnSittingFilter.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3"));
                btnSittingFilter.Foreground = Brushes.White;
            }
            else if (currentFilter == "Sleeper")
            {
                btnSleeperFilter.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9C27B0"));
                btnSleeperFilter.Foreground = Brushes.White;
            }
        }

        private void AddBookingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var parentWindow = Window.GetWindow(this);
                if (parentWindow is MainWindow mainWin && mainWin.MainContent.Content is Header header)
                {
                    header.OpenNewBooking();
                    Logger.Log("New booking page opened.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private void BookingDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var dataGrid = sender as DataGrid;
                var selectedItem = dataGrid?.SelectedItem as Booking1;
                
                // Clear selection immediately to prevent visual glitches
                if (dataGrid != null)
                {
                    dataGrid.SelectedItem = null;
                    dataGrid.SelectedIndex = -1;
                }

                // Commented out the message box functionality
                /*
                if (selectedItem != null)
                {
                    // Only allow sync for active bookings
                    if (selectedItem.status?.ToLower() != "active")
                    {
                        MessageBox.Show(
                            "Only active bookings can be synced to the network.",
                            "Cannot Sync",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    // Check if already synced
                    if (selectedItem.IsSynced == 1)
                    {
                        MessageBox.Show(
                            $"Booking Details:\n\n" +
                            $"Booking ID: {selectedItem.booking_id}\n" +
                            $"Name: {selectedItem.guest_name}\n" +
                            $"Phone: {selectedItem.phone_number}\n" +
                            $"Type: {selectedItem.booking_type}\n" +
                            $"In Time: {selectedItem.in_time}\n" +
                            $"Out Time: {selectedItem.out_time}\n" +
                            $"Status: {selectedItem.status}\n\n" +
                            $"✓ This booking is already synced to the network.",
                            "Already Synced",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    // Booking is active and not synced - ask to sync
                    string details =
                        $"Booking ID: {selectedItem.booking_id}\n" +
                        $"Name: {selectedItem.guest_name}\n" +
                        $"Phone: {selectedItem.phone_number}\n" +
                        $"Type: {selectedItem.booking_type}\n" +
                        $"In Time: {selectedItem.in_time}\n" +
                        $"Out Time: {selectedItem.out_time}\n" +
                        $"Status: {selectedItem.status}\n\n" +
                        $"Do you want to add this booking to the network?";

                    // Show MessageBox with Yes/No buttons
                    var result = MessageBox.Show(
                        details, 
                        "Add to Network?", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // User clicked "Yes" - add to network
                        AddBookingToNetwork(selectedItem);
                    }

                    Logger.Log($"Viewed booking details for {selectedItem.booking_id}");
                }
                */
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // MessageBox.Show("Error displaying booking details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddBookingToNetwork(Booking1 booking)
        {
            try
            {
                // First, check if internet is available
                bool isOnline = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                
                if (!isOnline)
                {
                    MessageBox.Show(
                        "No internet connection available.\n\nPlease connect to the internet and try again.",
                        "No Internet",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Log the sync attempt
                Logger.Log($"Attempting to sync booking {booking.booking_id} to server...");

                // Call the API to sync the booking
                bool success = await OfflineBookingStorage.SyncSingleBookingToServer(booking);

                if (success)
                {
                    // MessageBox.Show(
                    //     "Booking successfully added to the network!",
                    //     "Success",
                    //     MessageBoxButton.OK,
                    //     MessageBoxImage.Information);

                    // Reload bookings to reflect updated sync status
                    LoadBookings();
                }
                else
                {
                    // Sync failed - server issue
                    // MessageBox.Show(
                    //     "Server is temporarily unavailable.\n\nPlease try again later.",
                    //     "Server Error",
                    //     MessageBoxButton.OK,
                    //     MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // MessageBox.Show(
                //     $"An error occurred while syncing:\n\n{ex.Message}",
                //     "Error",
                //     MessageBoxButton.OK,
                //     MessageBoxImage.Error);
            }
        }

        public void UpdateGreeting(string username)
        {
            try
            {
                if (GreetingTextBlock != null)
                    GreetingTextBlock.Text = $"{GetGreeting()}, {username}";
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private string GetGreeting()
        {
            var hour = DateTime.Now.Hour;
            if (hour < 12)
                return "Good morning";
            else if (hour < 17)
                return "Good afternoon";
            else
                return "Good evening";
        }

        public void UpdateCountsFromBookings()
        {
            try
            {
                // Reset all counts
                bookingTypeCounts.Clear();
                foreach (var key in typeTextBlocks.Keys)
                {
                    bookingTypeCounts[key] = 0;
                }

                int activeCount = 0;
                int completedCount = 0;

                // Count bookings by status and type from ALL bookings (not filtered)
                foreach (var booking in allBookings)
                {
                    if (booking.status?.ToLower() == "active")
                    {
                        activeCount++;
                        
                        // Count by type (only for active bookings) - case-insensitive comparison
                        string? bookingType = booking.booking_type?.Trim();
                        if (!string.IsNullOrEmpty(bookingType))
                        {
                            // Normalize "Sleeper" to "Sleeping" to match settings
                            if (bookingType.Equals("Sleeper", StringComparison.OrdinalIgnoreCase))
                            {
                                bookingType = "Sleeping";
                            }
                            
                            // Find matching key in dictionary (case-insensitive)
                            var matchingKey = bookingTypeCounts.Keys
                                .FirstOrDefault(k => k.Equals(bookingType, StringComparison.OrdinalIgnoreCase));
                            
                            if (matchingKey != null)
                            {
                                bookingTypeCounts[matchingKey]++;
                            }
                            else
                            {
                                Logger.Log($"Warning: Booking type '{bookingType}' not found in counts dictionary. Available types: {string.Join(", ", bookingTypeCounts.Keys)}");
                            }
                        }
                    }
                    else if (booking.status?.ToLower() == "completed")
                    {
                        completedCount++;
                    }
                }

                // Update status counts
                ActiveTextBlock.Text = $"Active: {activeCount}";
                
                // Update booking type counts (Sitting and Sleeper)
                int sittingCount = allBookings.Count(b => b.booking_type?.ToLower() == "sitting");
                int sleeperCount = allBookings.Count(b => b.booking_type?.ToLower() == "sleeper");
                SittingTextBlock.Text = $"Sitting: {sittingCount}";
                SleeperTextBlock.Text = $"Sleeper: {sleeperCount}";

                Logger.Log($"Counts updated - Active: {activeCount}, Sitting: {sittingCount}, Sleeper: {sleeperCount}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private void InitializeBookingTypeCounts()
        {
            try
            {
                // Get booking types from database settings
                var bookingTypes = OfflineBookingStorage.GetBookingTypes();
                
                if (bookingTypes == null || bookingTypes.Count == 0)
                {
                    Logger.Log("No booking types found in database settings.");
                    return;
                }

                // Define colors for each type
                var colors = new[]
                {
                    new { Background = "#EAF3FF", Foreground = "#3A72C5" },  // Blue
                    new { Background = "#F5E8FF", Foreground = "#C53ABF" },  // Purple
                    new { Background = "#FFF8E1", Foreground = "#F9A825" },  // Yellow
                    new { Background = "#E8F5E9", Foreground = "#66BB6A" }   // Green
                };

                int colorIndex = 0;

                foreach (var bookingType in bookingTypes)
                {
                    if (string.IsNullOrEmpty(bookingType.Type)) continue;

                    string typeName = bookingType.Type.Trim();
                    bookingTypeCounts[typeName] = 0;

                    // Create UI element for this type
                    var border = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[colorIndex % colors.Length].Background)),
                        Padding = new Thickness(6, 4, 6, 4),
                        CornerRadius = new CornerRadius(16),
                        Margin = new Thickness(0, 0, 6, 0)
                    };

                    var stackPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var textBlock = new TextBlock
                    {
                        Text = $"{typeName}: 0",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[colorIndex % colors.Length].Foreground)),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold
                    };

                    typeTextBlocks[typeName] = textBlock;
                    stackPanel.Children.Add(textBlock);
                    border.Child = stackPanel;
                    CountsPanel.Children.Add(border);

                    colorIndex++;
                }

                Logger.Log($"Initialized {bookingTypeCounts.Count} booking type counters.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Log("Scan button clicked - attempting to create SimpleScanControl");
                
                SimpleScanControl scanControl;
                try
                {
                    scanControl = new SimpleScanControl();
                    Logger.Log("SimpleScanControl created successfully");
                }
                catch (Exception createEx)
                {
                    Logger.LogError(createEx);
                    MessageBox.Show(
                        $"Error creating scan control", 
                        "Control Creation Error", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
                    return;
                }
                
                // Handle close event
                scanControl.CloseRequested += (s, ev) =>
                {
                    ContentGrid.Children.Clear();
                    ContentGrid.Visibility = Visibility.Collapsed;
                    
                    // Reload bookings to reflect any completed bookings
                    LoadBookings();
                    UpdateCountsFromBookings();
                    
                    Logger.Log("Scan control closed - Dashboard refreshed");
                };
                
                // Handle billing completed event (removed for simple test)
                
                ContentGrid.Children.Clear();
                ContentGrid.Children.Add(scanControl);
                ContentGrid.Visibility = Visibility.Visible;
                Logger.Log("Scan billing control opened and displayed.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // MessageBox.Show(
                //     $"Error opening scan control:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}", 
                //     "Error", 
                //     MessageBoxButton.OK, 
                //     MessageBoxImage.Error);
            }
        }

        // Method to view all database contents - for debugging
        public void ViewDatabaseContents()
        {
            try
            {
                OfflineBookingStorage.ShowAllBookingsData();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // MessageBox.Show($"Error viewing database: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void RefreshBookings()
        {
            try
            {
                BookingDataGrid.ItemsSource = null;
                BookingDataGrid.ItemsSource = Bookings;
                Logger.Log("Bookings refreshed."); // ✅ Added Logger
            }
            catch (Exception ex)
            {
                Logger.LogError(ex); // ✅ Added Logger
            }
        }

        /// <summary>
        /// Reloads all bookings from database and updates the display
        /// </summary>
        public void ReloadData()
        {
            try
            {
                LoadBookings();
                UpdateCountsFromBookings();
                Logger.Log("Dashboard data reloaded from database.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // MessageBox.Show("Error reloading dashboard data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string query = SearchTextBox.Text.Trim().ToLower();

                if (string.IsNullOrEmpty(query))
                {
                    BookingDataGrid.ItemsSource = Bookings;
                    return;
                }

                var filtered = Bookings.Where(b =>
                    (!string.IsNullOrEmpty(b.guest_name) && b.guest_name.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(b.phone_number) && b.phone_number.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(b.booking_id) && b.booking_id.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(b.booking_type) && b.booking_type.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(b.status) && b.status.ToLower().Contains(query))
                ).ToList();

                BookingDataGrid.ItemsSource = filtered;
                Logger.Log($"Search performed: {query}"); // ✅ Added Logger
            }
            catch (Exception ex)
            {
                Logger.LogError(ex); // ✅ Added Logger
                // MessageBox.Show($"Search error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button during sync
                RefreshButton.IsEnabled = false;
                RefreshButton.Opacity = 0.5;

                // Get current worker ID
                string? workerId = LocalStorage.GetItem("workerId");
                if (string.IsNullOrEmpty(workerId))
                {
                    MessageBox.Show("Worker ID not found. Please log in again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Sync with server
                string result = await OfflineBookingStorage.SyncCompletedBookingsFromServerAsync(workerId);

                // Sync pending worker balance updates
                int balancesSynced = await OfflineBookingStorage.SyncWorkerBalancesAsync();
                if (balancesSynced > 0)
                {
                    Logger.Log($"{balancesSynced} worker balance(s) synced to server");
                }

                // Show result
                MessageBox.Show(result, "Sync Status", MessageBoxButton.OK, 
                    result.Contains("✅") ? MessageBoxImage.Information : MessageBoxImage.Warning);

                // Reload bookings if sync was successful
                if (result.Contains("✅"))
                {
                    LoadBookings();
                    UpdateCountsFromBookings();
                    await FetchWorkerBalanceAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Sync error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable button
                RefreshButton.IsEnabled = true;
                RefreshButton.Opacity = 1.0;
            }
        }
    }

    // Converter to show only last 6 digits of booking ID
    public class BookingIdConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return string.Empty;

            string? bookingId = value.ToString();
            
            // Return last 6 digits if the ID is longer than 6 characters
            if (bookingId != null && bookingId.Length > 6)
            {
                return bookingId.Substring(bookingId.Length - 6);
            }
            
            return bookingId ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter to format booking type with room number for Sleeper bookings
    /// </summary>
    public class BookingTypeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return string.Empty;

            string? bookingType = values[0] as string;
            string? roomNumber = values[1] as string;

            if (string.IsNullOrWhiteSpace(bookingType))
                return string.Empty;

            // If Sleeper and room number exists, format as "Sleeper(num)"
            if (bookingType.Equals("Sleeper", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(roomNumber))
            {
                return $"{bookingType}({roomNumber})";
            }

            return bookingType;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter to check if booking has 5 minutes or less remaining
    /// </summary>
    public class RemainingTimeChecker : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan outTime)
            {
                // Get current time
                TimeSpan now = DateTime.Now.TimeOfDay;
                
                // Calculate remaining time
                TimeSpan remaining = outTime - now;
                
                // If remaining time is 5 minutes or less (and positive), return true
                if (remaining.TotalSeconds > 0 && remaining.TotalMinutes <= 5)
                {
                    return true;
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter to check if row should be highlighted (active booking where end time has passed)
    /// </summary>
    public class CriticalRowBackgroundConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length < 4)
                    return false;

                // booking_date comes as STRING from SQLite TEXT field
                if (!DateTime.TryParse(values[0]?.ToString(), out DateTime bookingDate))
                    return false;

                if (!TimeSpan.TryParse(values[1]?.ToString(), out TimeSpan inTime))
                    return false;

                if (!TimeSpan.TryParse(values[2]?.ToString(), out TimeSpan outTime))
                    return false;

                string status = values[3]?.ToString() ?? "";

                // Only ACTIVE bookings
                if (!status.Equals("active", StringComparison.OrdinalIgnoreCase))
                    return false;

                DateTime now = DateTime.Now;

                // Calculate real end datetime
                DateTime endDateTime = bookingDate.Date.Add(outTime);

                // Overnight booking
                if (outTime < inTime)
                    endDateTime = endDateTime.AddDays(1);

                // 🔴 RED only if expired (>= ensures exact time match)
                return now >= endDateTime;
            }
            catch
            {
                return false;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter to check if text should be white (active booking where end time has passed)
    /// </summary>
    public class CriticalRowForegroundConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length < 4)
                    return false;

                if (!DateTime.TryParse(values[0]?.ToString(), out DateTime bookingDate))
                    return false;

                if (!TimeSpan.TryParse(values[1]?.ToString(), out TimeSpan inTime))
                    return false;

                if (!TimeSpan.TryParse(values[2]?.ToString(), out TimeSpan outTime))
                    return false;

                string status = values[3]?.ToString() ?? "";

                if (!status.Equals("active", StringComparison.OrdinalIgnoreCase))
                    return false;

                DateTime now = DateTime.Now;

                DateTime endDateTime = bookingDate.Date.Add(outTime);

                if (outTime < inTime)
                    endDateTime = endDateTime.AddDays(1);

                return now >= endDateTime;
            }
            catch
            {
                return false;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
