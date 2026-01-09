using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UserModule.Components;

namespace UserModule
{
    public partial class Login : UserControl
    {
        public event Action<string> LoginSuccess = delegate { };

        public Login()
        {
            InitializeComponent();

            this.KeyDown += Login_KeyDown;
            this.Focusable = true;
            this.Focus();
            // Username events
            txtUsername.GotFocus += TxtUsername_GotFocus;
            txtUsername.LostFocus += TxtUsername_LostFocus;
            txtUsername.TextChanged += TxtUsername_TextChanged;

            // Password events
            txtPassword.GotFocus += TxtPassword_GotFocus;
            txtPassword.LostFocus += TxtPassword_LostFocus;
            txtPassword.PasswordChanged += TxtPassword_PasswordChanged;

            txtPasswordVisible.GotFocus += TxtPasswordVisible_GotFocus;
            txtPasswordVisible.LostFocus += TxtPasswordVisible_LostFocus;
            txtPasswordVisible.TextChanged += TxtPasswordVisible_TextChanged;
            Loaded += Login_Loaded;
            SizeChanged += Parent_SizeChanged;
        }

        private void TxtUsername_GotFocus(object sender, RoutedEventArgs e) => usernamePlaceholder.Visibility = Visibility.Collapsed;
        private void TxtUsername_LostFocus(object sender, RoutedEventArgs e) => usernamePlaceholder.Visibility = string.IsNullOrEmpty(txtUsername.Text) ? Visibility.Visible : Visibility.Collapsed;
        private void TxtUsername_TextChanged(object sender, TextChangedEventArgs e) => usernamePlaceholder.Visibility = string.IsNullOrEmpty(txtUsername.Text) ? Visibility.Visible : Visibility.Collapsed;

        // ===== Password Placeholder =====
        private void TxtPassword_GotFocus(object sender, RoutedEventArgs e) => pwdPlaceholder.Visibility = Visibility.Collapsed;
        private void TxtPassword_LostFocus(object sender, RoutedEventArgs e)
        {
            if (txtPasswordVisible.Visibility == Visibility.Visible)
                pwdPlaceholder.Visibility = string.IsNullOrEmpty(txtPasswordVisible.Text) ? Visibility.Visible : Visibility.Collapsed;
            else
                pwdPlaceholder.Visibility = string.IsNullOrEmpty(txtPassword.Password) ? Visibility.Visible : Visibility.Collapsed;
        }
        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (txtPassword.Visibility == Visibility.Visible)
                pwdPlaceholder.Visibility = string.IsNullOrEmpty(txtPassword.Password) ? Visibility.Visible : Visibility.Collapsed;
        }
        private void TxtPasswordVisible_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtPasswordVisible.Visibility == Visibility.Visible)
                pwdPlaceholder.Visibility = string.IsNullOrEmpty(txtPasswordVisible.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
        private void TxtPasswordVisible_GotFocus(object sender, RoutedEventArgs e) => pwdPlaceholder.Visibility = Visibility.Collapsed;
        private void TxtPasswordVisible_LostFocus(object sender, RoutedEventArgs e) => pwdPlaceholder.Visibility = string.IsNullOrEmpty(txtPasswordVisible.Text) ? Visibility.Visible : Visibility.Collapsed;

        // ===== Show/Hide Password =====
        private void ShowPassword_Click(object sender, MouseButtonEventArgs e)
        {
            txtPasswordVisible.Text = txtPassword.Password;
            txtPassword.Visibility = Visibility.Collapsed;
            txtPasswordVisible.Visibility = Visibility.Visible;
            pwdPlaceholder.Visibility = string.IsNullOrEmpty(txtPasswordVisible.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HidePassword_Click(object sender, MouseButtonEventArgs e)
        {
            txtPassword.Password = txtPasswordVisible.Text;
            txtPassword.Visibility = Visibility.Visible;
            txtPasswordVisible.Visibility = Visibility.Collapsed;
            pwdPlaceholder.Visibility = string.IsNullOrEmpty(txtPassword.Password) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Login_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string username = txtUsername.Text.Trim();
                string password = txtPasswordVisible.Visibility == Visibility.Visible
                                    ? txtPasswordVisible.Text
                                    : txtPassword.Password;

                // Case 1: Username empty → focus username box
                if (string.IsNullOrEmpty(username))
                {
                    txtUsername.Focus();
                    e.Handled = true;
                    return;
                }

                // Case 2: Password empty → focus password box
                if (string.IsNullOrEmpty(password))
                {
                    if (txtPasswordVisible.Visibility == Visibility.Visible)
                        txtPasswordVisible.Focus();
                    else
                        txtPassword.Focus();

                    e.Handled = true;
                    return;
                }

                // Case 3: Both filled → trigger login
                Login_Click(btnLogin, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void TxtUsername_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (!string.IsNullOrWhiteSpace(txtUsername.Text))
                {
                    if (txtPasswordVisible.Visibility == Visibility.Visible)
                        txtPasswordVisible.Focus();
                    else
                        txtPassword.Focus();
                }
            }
        }


        // Login Button
        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPasswordVisible.Visibility == Visibility.Visible
                                ? txtPasswordVisible.Text
                                : txtPassword.Password;

            // Console log: Input data
            //Console.WriteLine("=== LOGIN ATTEMPT ===");
            //Console.WriteLine($"Username: {username}");
            //Console.WriteLine($"Password: {new string('*', password.Length)} (hidden for security)");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both username and password.",
                                "Missing Fields", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                MessageBox.Show("No internet connection. Please check your network and try again.",
                                "Network Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LoaderOverlay.Visibility = Visibility.Visible;

            var loginData = new
            {
                username = username,
                password = password
            };

            string json = JsonConvert.SerializeObject(loginData);
            
            // Console log: Request payload
            //Console.WriteLine("\n=== LOGIN REQUEST ===");
            //Console.WriteLine($"API Endpoint: https://railway-worker-backend.artechnology.pro/api/Login/check");
            //Console.WriteLine($"Request Payload: {json}");
            
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    client.Timeout = TimeSpan.FromSeconds(6);

                    HttpResponseMessage response = await client.PostAsync("https://railway-worker-backend.artechnology.pro/api/Login/check", content);
                    
                    // Console log: Response status
                    //Console.WriteLine("\n=== API RESPONSE ===");
                    //Console.WriteLine($"Status Code: {response.StatusCode}");
                    //Console.WriteLine($"Success: {response.IsSuccessStatusCode}");
                    
                    // MessageBox.Show($"Login attempt made. Status: {response.StatusCode}", "Login", MessageBoxButton.OK, MessageBoxImage.Information);
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        
                        // Console log: Raw response
                        //Console.WriteLine($"Response Body: {responseBody}");
                        
                        // Show the raw response for debugging
                        // MessageBox.Show($"Response received:\n\n{responseBody}", 
                                        // "Server Response", MessageBoxButton.OK, MessageBoxImage.Information);
                        
                        dynamic? result = JsonConvert.DeserializeObject(responseBody);
                        string? workerId = result?.worker_id;
                        string? adminId = result?.admin_id;

                        // Console log: Parsed data
                        //Console.WriteLine("\n=== PARSED DATA ===");
                        //Console.WriteLine($"Worker ID: {workerId ?? "NULL"}");
                        //Console.WriteLine($"Admin ID: {adminId ?? "NULL"}");

                        // Show parsed values
                        // MessageBox.Show($"Worker ID: {workerId ?? "NULL"}\nAdmin ID: {adminId ?? "NULL"}", 
                                        // "Parsed Values", MessageBoxButton.OK, MessageBoxImage.Information);

                        if (string.IsNullOrEmpty(workerId) || string.IsNullOrEmpty(adminId))
                        {
                            MessageBox.Show("Something went wrong while processing your login. Please try again later.",
                                            "Login Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        // Save login credentials
                        LocalStorage.SetItem("workerId", workerId, TimeSpan.FromHours(8));
                        LocalStorage.SetItem("adminId", adminId, TimeSpan.FromHours(8));
                        LocalStorage.SetItem("username", username, TimeSpan.FromHours(8));

                        // Console log: Saved data
                        //Console.WriteLine("\n=== SAVED TO LOCAL STORAGE ===");
                        //Console.WriteLine($"workerId: {workerId} (Expiry: 8 hours)");
                        //Console.WriteLine($"adminId: {adminId} (Expiry: 8 hours)");
                        //Console.WriteLine($"username: {username} (Expiry: 8 hours)");

                        // Fetch and save worker settings and booking types to local database
                        await OfflineBookingStorage.FetchAndSaveWorkerSettingsAsync(adminId);
                        
                        // Fetch and save printer details for receipt printing
                        await OfflineBookingStorage.FetchAndSavePrinterDetailsAsync(adminId);
                        
                        // Fetch and save hourly pricing tiers (Type2 details)
                        var hourlyPricingTiers = await OfflineBookingStorage.FetchType2DetailsAsync(adminId);
                        OfflineBookingStorage.SaveHourlyPricingTiers(adminId, hourlyPricingTiers);

                        //Console.WriteLine("\n=== LOGIN SUCCESS ===");
                        //Console.WriteLine("Worker settings, printer details, and hourly pricing fetched and saved.");
                        //Console.WriteLine("====================================\n");

                        LoginSuccess?.Invoke(username);
                    }
                    else
                    {
                        MessageBox.Show("Invalid username or password.",
                                        "Login Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (TaskCanceledException ex)
                {
                    Logger.LogError(ex);
                    MessageBox.Show("The connection seems slow. Please check your internet and try again.",
                                    "Slow Network", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (HttpRequestException ex)
                {
                    Logger.LogError(ex);
                    MessageBox.Show("Unable to reach the server. Please check your network connection and try again.",
                                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex);
                    MessageBox.Show("An unexpected issue occurred. Please try again later.",
                                    "Something Went Wrong", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    LoaderOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }



        private void Login_Loaded(object sender, RoutedEventArgs e)
        {
            // Stretch to fill parent container
            if (this.Parent is FrameworkElement parent)
            {
                this.HorizontalAlignment = HorizontalAlignment.Stretch;
                this.VerticalAlignment = VerticalAlignment.Stretch;

                this.Width = parent.ActualWidth;
                this.Height = parent.ActualHeight;
                parent.SizeChanged += Parent_SizeChanged;
            }
        }

        private void Parent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.Width = e.NewSize.Width;
            this.Height = e.NewSize.Height;
        }

        private void UpdateCardSize()
        {
            double rightColumnWidth = RootGrid.ColumnDefinitions.Count > 1 ? RootGrid.ColumnDefinitions[1].ActualWidth : RootGrid.ActualWidth;
            double containerHeight = RootGrid.ActualHeight;

            if (double.IsNaN(rightColumnWidth) || rightColumnWidth <= 0) rightColumnWidth = this.ActualWidth;
            if (double.IsNaN(containerHeight) || containerHeight <= 0) containerHeight = this.ActualHeight;

            double desiredWidth = rightColumnWidth * 0.6;
            double newWidth = Math.Max(CardBorder.MinWidth, Math.Min(CardBorder.MaxWidth, desiredWidth));
            CardBorder.Width = newWidth;
        }

        // Loding the login page and checking for saved session
        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.Parent is FrameworkElement parent)
            {
                this.HorizontalAlignment = HorizontalAlignment.Stretch;
                this.VerticalAlignment = VerticalAlignment.Stretch;

                this.Width = parent.ActualWidth;
                this.Height = parent.ActualHeight;

                parent.SizeChanged += Parent_SizeChanged;
            }

            string? savedWorkerId = LocalStorage.GetItem("workerId");
            string? savedAdminId = LocalStorage.GetItem("adminId");
            string? savedUsername = LocalStorage.GetItem("username");

            // Console log: Auto-login check
            //Console.WriteLine("\n=== AUTO-LOGIN CHECK (On Load) ===");
            //Console.WriteLine($"Saved Worker ID: {savedWorkerId ?? "NOT FOUND"}");
            //Console.WriteLine($"Saved Admin ID: {savedAdminId ?? "NOT FOUND"}");
            //Console.WriteLine($"Saved Username: {savedUsername ?? "NOT FOUND"}");

            if (!string.IsNullOrEmpty(savedWorkerId))
            {
                //Console.WriteLine("\n=== AUTO-LOGIN TRIGGERED ===");
                //Console.WriteLine("Valid session found in LocalStorage.");
                
                // Check if settings exist and are still valid (not expired)
                var settings = OfflineBookingStorage.GetSettings();
                
                // If settings are expired or missing, refetch them
                if (settings == null && !string.IsNullOrEmpty(savedAdminId))
                {
                    //Console.WriteLine("Settings not found or expired. Fetching fresh data...");
                    LoaderOverlay.Visibility = Visibility.Visible;
                    await OfflineBookingStorage.FetchAndSaveWorkerSettingsAsync(savedAdminId);
                    await OfflineBookingStorage.FetchAndSavePrinterDetailsAsync(savedAdminId);
                    var hourlyPricingTiers = await OfflineBookingStorage.FetchType2DetailsAsync(savedAdminId);
                    OfflineBookingStorage.SaveHourlyPricingTiers(savedAdminId, hourlyPricingTiers);
                    LoaderOverlay.Visibility = Visibility.Collapsed;
                    //Console.WriteLine("Fresh settings, printer details, and hourly pricing fetched and saved.");
                }
                else
                {
                    //Console.WriteLine("Using existing settings from storage.");
                }

                //Console.WriteLine("Auto-login successful!");
                //Console.WriteLine("====================================\n");

                LoginSuccess?.Invoke(savedUsername ?? string.Empty);
            }
            else
            {
                //Console.WriteLine("No saved session found. Manual login required.");
                //Console.WriteLine("====================================\n");
            }
        }
    }
}
