using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;               // For Brush, Brushes, Colors, BrushConverter
using System.Windows.Media.Effects;       // For DropShadowEffect
using System.Windows.Threading;          // For DispatcherTimer
using System.Windows.Shapes; // For Ellipse

namespace UserModule
{
    /// <summary>
    /// Interaction logic for Header.xaml
    /// </summary>
    public partial class Header : UserControl
    {
        private Button? _selectedButton;
        // Local references to the internet status controls (resolved after InitializeComponent)
        private Ellipse? _internetStatusDot;
        private TextBlock? _internetStatusText;

        public Header()
        {
            InitializeComponent();

            // Resolve named controls (safe approach if generated fields are not available)
            _internetStatusDot = FindName("InternetStatusDot") as Ellipse;
            _internetStatusText = FindName("InternetStatusText") as TextBlock;
            
            LoadContent(new Dashboard());

            // Initially select Dashboard button
            _selectedButton = DashboardButton;
            SetSelectedButton(_selectedButton);

            // Start internet monitor
            InitializeInternetStatusMonitor();
        }

        // Internet status monitoring
        private DispatcherTimer? internetCheckTimer;
        private int consecutiveFailures = 0;

        private void InitializeInternetStatusMonitor()
        {
            // Check immediately on load
            _ = CheckInternetStatusAsync();

            // Set up timer to check every 5 seconds
            internetCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            internetCheckTimer.Tick += async (s, e) => await CheckInternetStatusAsync();
            internetCheckTimer.Start();
        }

        private async System.Threading.Tasks.Task CheckInternetStatusAsync()
        {
            try
            {
                bool isConnected = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();

                if (!isConnected)
                {
                    consecutiveFailures = 3;
                    UpdateInternetStatus(InternetStatus.NoConnection);
                    return;
                }

                bool hasInternet = await PingServerAsync("8.8.8.8", 3000);

                if (hasInternet)
                {
                    consecutiveFailures = 0;
                    UpdateInternetStatus(InternetStatus.Good);
                }
                else
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= 3)
                        UpdateInternetStatus(InternetStatus.NoConnection);
                    else
                        UpdateInternetStatus(InternetStatus.Unstable);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                consecutiveFailures++;
                if (consecutiveFailures >= 2)
                    UpdateInternetStatus(InternetStatus.NoConnection);
                else
                    UpdateInternetStatus(InternetStatus.Unstable);
            }
        }

        private async System.Threading.Tasks.Task<bool> PingServerAsync(string host, int timeout)
        {
            try
            {
                using (var ping = new System.Net.NetworkInformation.Ping())
                {
                    var reply = await ping.SendPingAsync(host, timeout);
                    return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        private void UpdateInternetStatus(InternetStatus status)
        {
            // Use local resolved references
            if (_internetStatusDot == null || _internetStatusText == null)
                return;

            switch (status)
            {
                case InternetStatus.Good:
                    _internetStatusDot.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    _internetStatusText.Text = "Online";
                    _internetStatusDot.ToolTip = "Internet connection is stable";
                    break;
                case InternetStatus.Unstable:
                    _internetStatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
                    _internetStatusText.Text = "Unstable";
                    _internetStatusDot.ToolTip = "Internet connection is weak or unstable";
                    break;
                case InternetStatus.NoConnection:
                    _internetStatusDot.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                    _internetStatusText.Text = "Offline";
                    _internetStatusDot.ToolTip = "No internet connection";
                    break;
            }
        }

        private enum InternetStatus
        {
            Good,
            Unstable,
            NoConnection
        }

        public void SetLoggedInUser(string username)
        {
            // Capitalize first letter of username
            string capitalizedUsername = !string.IsNullOrEmpty(username) 
                ? char.ToUpper(username[0]) + username.Substring(1).ToLower() 
                : username;

            // Get time-based greeting
            string greeting = GetTimeBasedGreeting();

            // Update username text
            UserNameTextBlock.Text = capitalizedUsername;

            // Create popup container
            var popupBorder = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 20, 0),
                Opacity = 0,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 15,
                    ShadowDepth = 3,
                    Opacity = 0.25
                }
            };

            // Text inside popup
            var popupText = new TextBlock
            {
                Text = $"{greeting}, {capitalizedUsername}! Welcome Back. Have a good day!!",
                Foreground = (Brush?)new BrushConverter().ConvertFromString("#28C76F") ?? new SolidColorBrush(Color.FromRgb(40, 199, 111)),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            };

            popupBorder.Child = popupText;

            // Add to MainContentGrid
            MainContentGrid.Children.Add(popupBorder);

            // Animate slide-in from top-right
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400)));
            popupBorder.BeginAnimation(OpacityProperty, anim);

            // Auto remove after 3 seconds
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(300)));
                fadeOut.Completed += (s2, e2) => MainContentGrid.Children.Remove(popupBorder);
                popupBorder.BeginAnimation(OpacityProperty, fadeOut);
                timer.Stop();
            };
            timer.Start();
        }
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show confirmation dialog
                var result = MessageBox.Show(
                    "Are you sure you want to logout?", 
                    "Confirm Logout", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Clear stored credentials from LocalStorage
                    LocalStorage.RemoveItem("username");
                    LocalStorage.RemoveItem("password");
                    LocalStorage.RemoveItem("workerId");
                    LocalStorage.RemoveItem("rememberMe");
                    
                    // Log the logout action
                    Logger.Log($"User {UserNameTextBlock.Text} logged out successfully");

                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    if (mainWindow != null)
                    {
                        // Create a fresh Login UserControl
                        var loginControl = new Login();

                        // Handle LoginSuccess to reload Header + Dashboard
                        loginControl.LoginSuccess += username =>
                        {
                            // Create new Header inside the lambda
                            var header = new Header();

                            // Set the logged-in user
                            header.SetLoggedInUser(username);

                            // Load Dashboard inside Header's MainContentHost
                            header.MainContentHost.Content = new Dashboard();

                            // Set MainContent of MainWindow to the new Header
                            mainWindow.MainContent.Content = header;
                        };

                        // Replace current MainContent (Header + Dashboard) with Login
                        mainWindow.MainContent.Content = loginControl;
                        
                        // Show logout success message
                        MessageBox.Show(
                            "You have been logged out successfully!", 
                            "Logout Successful", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // MessageBox.Show(
                //     "An error occurred during logout. Please try again.", 
                //     "Logout Error", 
                //     MessageBoxButton.OK, 
                //     MessageBoxImage.Error);
            }
        }




        // Ex
        public void LoadContent(UserControl control)
        {
            MainContentHost.Content = control;
        }

        private void SetSelectedButton(Button button)
        {
            // Prevent unnecessary restyle
            if (_selectedButton == button)
                return;

            // Reset previous selected button style (except Submit)
            if (_selectedButton != null && _selectedButton != SubmitBookingButton)
                _selectedButton.Style = (Style)FindResource("HeaderButtonStyle");

            // Update current button
            _selectedButton = button;

            // Always keep Submit button in selected style
            if (button == SubmitBookingButton)
            {
                SubmitBookingButton.Style = (Style)FindResource("HeaderButtonSelectedStyle");
            }
            else
            {
                _selectedButton.Style = (Style)FindResource("HeaderButtonSelectedStyle");
            }
        }

        private void Dashboard_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedButton(DashboardButton);
            SetSubmitButtonVisibility(false); // Hide Submit button
            LoadContent(new Dashboard());
        }

        private void NewBooking_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedButton(NewBookingButton);
            SetSubmitButtonVisibility(false); // Hide Submit button
            var dashboard = MainContentGrid.Children.OfType<Dashboard>().FirstOrDefault() ?? new Dashboard();
            LoadContent(new NewBooking(dashboard));
        }

        private void HeaderScanButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedButton(ScanHeaderButton);
            SetSubmitButtonVisibility(false); // Hide Submit button
            
            // Delegate to any open Dashboard inside MainContentHost or MainWindow
            try
            {
                // First try Header's own MainContentHost
                var dashboard = MainContentHost.Content as Dashboard;
                if (dashboard != null)
                {
                    // Trigger dashboard's scan action by invoking its ScanButton_Click handler via reflection-like call
                    dashboard.Dispatcher.Invoke(() => {
                        // Create SimpleScanControl as Dashboard does
                        var scanControl = new SimpleScanControl();
                        scanControl.CloseRequested += (s, ev) =>
                        {
                            dashboard.ContentGrid.Children.Clear();
                            dashboard.ContentGrid.Visibility = Visibility.Collapsed;
                            dashboard.LoadBookings();
                            dashboard.UpdateCountsFromBookings();
                        };
                        dashboard.ContentGrid.Children.Clear();
                        dashboard.ContentGrid.Children.Add(scanControl);
                        dashboard.ContentGrid.Visibility = Visibility.Visible;
                    });
                    return;
                }

                // Otherwise try MainWindow's MainContent which usually contains Header -> MainContentHost
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow?.MainContent.Content is Header header && header.MainContentHost.Content is Dashboard dash)
                {
                    dash.Dispatcher.Invoke(() => {
                        var scanControl = new SimpleScanControl();
                        scanControl.CloseRequested += (s, ev) =>
                        {
                            dash.ContentGrid.Children.Clear();
                            dash.ContentGrid.Visibility = Visibility.Collapsed;
                            dash.LoadBookings();
                            dash.UpdateCountsFromBookings();
                        };
                        dash.ContentGrid.Children.Clear();
                        dash.ContentGrid.Children.Add(scanControl);
                        dash.ContentGrid.Visibility = Visibility.Visible;
                    });
                    return;
                }

                // As fallback, load a new Dashboard into Header and open scan
                var newDash = new Dashboard();
                LoadContent(newDash);
                newDash.Dispatcher.Invoke(() => {
                    var scanControl = new SimpleScanControl();
                    scanControl.CloseRequested += (s, ev) =>
                    {
                        newDash.ContentGrid.Children.Clear();
                        newDash.ContentGrid.Visibility = Visibility.Collapsed;
                        newDash.LoadBookings();
                        newDash.UpdateCountsFromBookings();
                    };
                    newDash.ContentGrid.Children.Clear();
                    newDash.ContentGrid.Children.Add(scanControl);
                    newDash.ContentGrid.Visibility = Visibility.Visible;
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show("Error opening scan control.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenNewBooking()
        {
            SetSelectedButton(NewBookingButton);
            SetSubmitButtonVisibility(false); // Hide Submit button
            var dashboard = MainContentGrid.Children.OfType<Dashboard>().FirstOrDefault() ?? new Dashboard();
            LoadContent(new NewBooking(dashboard));
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            // Submit button should always appear selected
            SubmitBookingButton.Style = (Style)FindResource("HeaderButtonSelectedStyle");
            SetSubmitButtonVisibility(true); // Show Submit button
            SetSelectedButton(SubmitBookingButton);
            LoadContent(new Submit());
        }

        public void UpdateUsername(string username)
        {
            UserNameTextBlock.Text = username;
        }

        // Controls Submit button visibility in Header
        public void SetSubmitButtonVisibility(bool isVisible)
        {
            SubmitBookingButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Report_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedButton(ReportButton);
            SetSubmitButtonVisibility(false); // Hide Submit button
            LoadContent(new Report());
        }

        /// <summary>
        /// Gets a greeting message based on the current time
        /// </summary>
        private string GetTimeBasedGreeting()
        {
            int hour = DateTime.Now.Hour;

            if (hour >= 5 && hour < 12)
                return "Good Morning";
            else if (hour >= 12 && hour < 17)
                return "Good Afternoon";
            else if (hour >= 17 && hour < 21)
                return "Good Evening";
            else
                return "Good Night";
        }
    }
}
