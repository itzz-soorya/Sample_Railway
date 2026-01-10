using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using UserModule.Models;

namespace UserModule
{
    public partial class NewBooking : UserControl
    {
        // Regex patterns for input validation
        private static readonly Regex _regex = new Regex("^[a-zA-Z ]+$");
        private static readonly Regex _regexNumeric = new Regex("^[0-9]+$");
        private static readonly Regex _regexAlphanumeric = new Regex("^[A-Za-z0-9]+$");

        // Constants
        private const int PHONE_NUMBER_LENGTH = 10;
        private const int AADHAR_NUMBER_LENGTH = 12;
        private const int PAN_NUMBER_LENGTH = 10;
        private const int PNR_NUMBER_LENGTH = 10;

        // Color brushes for UI (reusable to avoid creating new instances repeatedly)
        private static readonly SolidColorBrush PlaceholderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#999999"));
        private static readonly SolidColorBrush ContentBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#333333"));

        private readonly Dashboard? _dashboardInstance;
        private readonly DispatcherTimer dateTimeTimer = new DispatcherTimer();
        private int idNumberEnterCount = 0; // Track Enter key presses on ID number field
        private DispatcherTimer enterResetTimer = new DispatcherTimer(); // Timer to reset Enter count
        private int printButtonEnterCount = 0; // Track Enter key presses on print button
        private DispatcherTimer printEnterResetTimer = new DispatcherTimer(); // Timer to reset print enter count

        // ===== Parameterless constructor for XAML =====
        public NewBooking()
        {
            InitializeComponent();

            // Load seating types on page load
            this.Loaded += NewBooking_Loaded;

            // Add keyboard shortcut for direct print (Ctrl+Shift+P)
            this.PreviewKeyDown += NewBooking_PreviewKeyDown;

            // Initialize Enter reset timer
            enterResetTimer.Interval = TimeSpan.FromSeconds(2); // Reset after 2 seconds of inactivity
            enterResetTimer.Tick += (s, e) =>
            {
                idNumberEnterCount = 0;
                enterResetTimer.Stop();
            };

            // Initialize Print button enter reset timer
            printEnterResetTimer.Interval = TimeSpan.FromSeconds(2); // Reset after 2 seconds of inactivity
            printEnterResetTimer.Tick += (s, e) =>
            {
                printButtonEnterCount = 0;
                printEnterResetTimer.Stop();
            };

            txtFirstName.TextChanged += (s, e) => errCustomer.Visibility = Visibility.Collapsed;
            txtPhone.TextChanged += (s, e) => errPhone.Visibility = Visibility.Collapsed;
            txtPersons.TextChanged += (s, e) => errPersons.Visibility = Visibility.Collapsed;
            txtIdNumber.TextChanged += (s, e) => errIdNumber.Visibility = Visibility.Collapsed;

            // Reset Enter count when ID number changes
            txtIdNumber.TextChanged += (s, e) => 
            {
                idNumberEnterCount = 0;
                enterResetTimer.Stop();
            };

            // Change TextBox foreground color based on content
            txtFirstName.TextChanged += TextBox_ForegroundColorChanged;
            txtPhone.TextChanged += TextBox_ForegroundColorChanged;
            txtPersons.TextChanged += TextBox_ForegroundColorChanged;
            txtIdNumber.TextChanged += TextBox_ForegroundColorChanged;

            txtSeats.SelectionChanged += (s, e) => errSeats.Visibility = Visibility.Collapsed;
            txtHours.SelectionChanged += (s, e) => errHours.Visibility = Visibility.Collapsed;
            cmbIdType.SelectionChanged += (s, e) => errIdType.Visibility = Visibility.Collapsed;

            // Handle seat type change to populate hours dynamically
            txtSeats.SelectionChanged += TxtSeats_SelectionChanged;

            // Update total amount automatically when user changes inputs
            txtSeats.SelectionChanged += (s, e) => UpdateAmount();
            txtHours.SelectionChanged += (s, e) => UpdateAmount();
            txtPersons.TextChanged += (s, e) => UpdateAmount();

            txtPhone.TextChanged += (s, e) =>
            {
                if (txtPhone.Text.Length == PHONE_NUMBER_LENGTH)
                {
                    GenerateBillIDFromPhone();
                }
                else
                {
                    txtBookingID.Text = "";
                }
            };

        }

        /// <summary>
        /// Handle page loaded event
        /// </summary>
        private async void NewBooking_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadSeatingTypesAsync();
        }

        /// <summary>
        /// Fetch seating types from API and configure ComboBox
        /// </summary>
        private async Task LoadSeatingTypesAsync()
        {
            try
            {
                // Get admin and worker IDs from LocalStorage
                string adminId = LocalStorage.GetItem("adminId") ?? "";
                string workerId = LocalStorage.GetItem("workerId") ?? "";

                if (string.IsNullOrEmpty(adminId) || string.IsNullOrEmpty(workerId))
                {
                    // Default to showing both options if IDs not available
                    ConfigureSeatingOptions(2);
                    return;
                }

                // Always fetch from API to get latest seating types
                // Fetch from API
                string apiUrl = $"https://railway-api.artechnology.pro/api/worker/get-seating-types/{adminId}/{workerId}";
                using (HttpClient client = new HttpClient())
                {
                    var response = await client.GetAsync(apiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        var jsonObject = JObject.Parse(jsonResponse);
                        int seatingTypes = jsonObject["seating_types"]?.Value<int>() ?? 2;

                        // Save to LocalStorage (no expiry)
                        LocalStorage.SetItem("seating_types", seatingTypes.ToString());

                        // Configure the ComboBox based on the value
                        ConfigureSeatingOptions(seatingTypes);
                    }
                    else
                    {
                        // Default to showing both options on API failure
                        ConfigureSeatingOptions(2);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error and default to showing both options
                System.Diagnostics.Debug.WriteLine($"Error loading seating types: {ex.Message}");
                ConfigureSeatingOptions(2);
            }
        }

        /// <summary>
        /// Configure seat type ComboBox based on seating_types value
        /// 0 = only Sitting
        /// 1 = only Sleeping (Sleeper)
        /// 2 = both Sitting and Sleeping
        /// If only one type is available, it will be auto-selected and the ComboBox will be disabled
        /// </summary>
        private void ConfigureSeatingOptions(int seatingTypes)
        {
            // Clear existing items except placeholder
            txtSeats.Items.Clear();
            
            // Add placeholder
            var placeholder = new ComboBoxItem
            {
                Content = "Select type",
                IsEnabled = false,
                IsSelected = true,
                Style = (Style)FindResource("WhiteComboBoxItemStyle")
            };
            placeholder.Name = "seatsPlaceholder";
            txtSeats.Items.Add(placeholder);

            // Add items based on seating_types
            switch (seatingTypes)
            {
                case 0: // Only Sitting
                    txtSeats.Items.Add(new ComboBoxItem
                    {
                        Content = "Sitting",
                        Style = (Style)FindResource("WhiteComboBoxItemStyle")
                    });
                    break;

                case 1: // Only Sleeping (Sleeper)
                    txtSeats.Items.Add(new ComboBoxItem
                    {
                        Content = "Sleeper",
                        Style = (Style)FindResource("WhiteComboBoxItemStyle")
                    });
                    break;

                case 2: // Both
                default:
                    txtSeats.Items.Add(new ComboBoxItem
                    {
                        Content = "Sitting",
                        Style = (Style)FindResource("WhiteComboBoxItemStyle")
                    });
                    txtSeats.Items.Add(new ComboBoxItem
                    {
                        Content = "Sleeper",
                        Style = (Style)FindResource("WhiteComboBoxItemStyle")
                    });
                    break;
            }

            // Check if only one option is available (seatingTypes 0 or 1)
            if (seatingTypes == 0 || seatingTypes == 1)
            {
                // Only one option - auto-select it and disable the ComboBox
                txtSeats.SelectedIndex = 1; // Select the first (and only) non-placeholder item
                txtSeats.IsEnabled = false; // Disable ComboBox since there's no choice
                errSeats.Visibility = Visibility.Collapsed; // Hide error message
            }
            else
            {
                // Multiple options - allow user to select
                txtSeats.SelectedIndex = 0; // Select placeholder
                txtSeats.IsEnabled = true; // Enable ComboBox for user selection
            }
        }

        /// <summary>
        /// Public method to refresh seating types from API (useful for manual refresh)
        /// </summary>
        public async Task RefreshSeatingTypesAsync(bool forceRefresh = false)
        {
            if (forceRefresh)
            {
                // Clear cache to force API call
                LocalStorage.RemoveItem("seating_types");
            }
            await LoadSeatingTypesAsync();
        }

        /// <summary>
        /// Shows an alert message using MessageBox
        /// </summary>
        private void ShowAlert(string type, string message)
        {
            string title = type switch
            {
                "error" => "Error",
                "warning" => "Warning",
                "success" => "Success",
                "info" => "Information",
                _ => "Information"
            };

            MessageBoxImage icon = type switch
            {
                "error" => MessageBoxImage.Error,
                "warning" => MessageBoxImage.Warning,
                "success" => MessageBoxImage.Information,
                "info" => MessageBoxImage.Information,
                _ => MessageBoxImage.Information
            };

            // MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }

        /// <summary>
        /// Sets the foreground color of a control based on placeholder state
        /// </summary>
        private void SetForegroundColor(Control control, bool isPlaceholder)
        {
            control.Foreground = isPlaceholder ? PlaceholderBrush : ContentBrush;
        }


        private void CustomerName_TextChanged(object sender, TextChangedEventArgs e)
        {
            string firstName = txtFirstName.Text.Trim();

            if (string.IsNullOrEmpty(firstName))
            {
                errCustomer.Visibility = Visibility.Visible; // Show error
                txtCustomer.Text = "";
            }
            else
            {
                errCustomer.Visibility = Visibility.Collapsed;

                // Use only first name, last name is always empty
                txtCustomer.Text = firstName;
            }
        }

        private void TextBox_ForegroundColorChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // If text is empty, show grey (placeholder color)
                SetForegroundColor(textBox, string.IsNullOrEmpty(textBox.Text));
            }
        }

        private void txtSeats_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // If user picks index > 0, collapse the placeholder item so it no longer appears
            if (txtSeats.SelectedIndex > 0)
            {
                var placeholder = GetSeatsPlaceholder();
                if (placeholder != null) placeholder.Visibility = Visibility.Collapsed;
                SetForegroundColor(txtSeats, false);
            }
            else
            {
                var placeholder = GetSeatsPlaceholder();
                if (placeholder != null) placeholder.Visibility = Visibility.Visible;
                SetForegroundColor(txtSeats, true);
            }
        }

        /// <summary>
        /// Get the placeholder ComboBoxItem from txtSeats
        /// </summary>
        private ComboBoxItem? GetSeatsPlaceholder()
        {
            return txtSeats.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Name == "seatsPlaceholder");
        }

        /// <summary>
        /// Handle seat type selection change to populate hours dynamically
        /// </summary>
        private void TxtSeats_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (txtSeats.SelectedItem is ComboBoxItem selectedItem && 
                selectedItem.Name != "seatsPlaceholder")
            {
                string seatType = selectedItem.Content?.ToString()?.ToLower() ?? "";
                
                if (seatType == "sleeper" || seatType == "sleeping")
                {
                    // Load hourly pricing tiers from database for Sleeper
                    PopulateHoursWithPricingTiers();
                    
                    // Also populate overlay combobox with same hours
                    PopulateSummaryHoursWithPricingTiers();
                }
                else
                {
                    // For Sitting, show default hours (1-12)
                    PopulateDefaultHours();
                    
                    // Also populate overlay combobox with same hours
                    PopulateSummaryDefaultHours();
                }

                // Update room number visibility based on seat type
                UpdateRoomNumberVisibility();

                // Reset hours selection when seat type changes
                txtHours.SelectedIndex = 0;
                if (summaryHoursCombo != null)
                {
                    summaryHoursCombo.SelectedIndex = 0;
                }
            }
        }

        /// <summary>
        /// Populate hours dropdown with pricing tiers from database (for Sleeper)
        /// </summary>
        private void PopulateHoursWithPricingTiers()
        {
            try
            {
                // Clear existing items except placeholder
                var placeholder = txtHours.Items[0];
                txtHours.Items.Clear();
                txtHours.Items.Add(placeholder);

                // Get hourly pricing tiers from database
                var pricingTiers = OfflineBookingStorage.GetHourlyPricingTiers();

                if (pricingTiers == null || pricingTiers.Count == 0)
                {
                    //Console.WriteLine("No hourly pricing tiers found in database");
                    // Fallback to default hours
                    PopulateDefaultHours();
                    return;
                }

                //Console.WriteLine($"\n=== POPULATING HOURS WITH PRICING TIERS ===");
                //Console.WriteLine($"Found {pricingTiers.Count} pricing tiers");

                foreach (var tier in pricingTiers)
                {
                    string displayText = $"{tier.MinHours}-{tier.MaxHours} hrs";
                    
                    var item = new ComboBoxItem
                    {
                        Content = displayText,
                        Tag = tier.Amount, // Store the amount in Tag for easy access
                        Style = (Style)FindResource("WhiteComboBoxItemStyle")
                    };

                    txtHours.Items.Add(item);
                    //Console.WriteLine($"Added: {displayText} (Amount: ₹{tier.Amount})");
                }

                //Console.WriteLine("====================================\n");

                // Reset selection to placeholder
                txtHours.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"ERROR: Failed to populate pricing tiers - {ex.Message}");
                Logger.LogError(ex);
                PopulateDefaultHours();
            }
        }

        /// <summary>
        /// Populate hours dropdown with default 1-12 hours (for Sitting)
        /// </summary>
        private void PopulateDefaultHours()
        {
            try
            {
                // Clear existing items except placeholder
                var placeholder = txtHours.Items[0];
                txtHours.Items.Clear();
                txtHours.Items.Add(placeholder);

                // Add default 1-12 hours
                for (int i = 1; i <= 12; i++)
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{i} hr",
                        Style = (Style)FindResource("WhiteComboBoxItemStyle")
                    };
                    txtHours.Items.Add(item);
                }

                // Reset selection to placeholder
                txtHours.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"ERROR: Failed to populate default hours - {ex.Message}");
                Logger.LogError(ex);
            }
        }

        /// <summary>
        /// Populate overlay hours combobox with pricing tiers from database (for Sleeper)
        /// </summary>
        private void PopulateSummaryHoursWithPricingTiers()
        {
            try
            {
                if (summaryHoursCombo == null) return;

                // Clear existing items except placeholder
                var placeholder = summaryHoursCombo.Items[0];
                summaryHoursCombo.Items.Clear();
                summaryHoursCombo.Items.Add(placeholder);

                // Get hourly pricing tiers from database
                var pricingTiers = OfflineBookingStorage.GetHourlyPricingTiers();

                if (pricingTiers == null || pricingTiers.Count == 0)
                {
                    // Fallback to default hours
                    PopulateSummaryDefaultHours();
                    return;
                }

                foreach (var tier in pricingTiers)
                {
                    string displayText = $"{tier.MinHours}-{tier.MaxHours} hrs";
                    
                    var item = new ComboBoxItem
                    {
                        Content = displayText,
                        Tag = tier.Amount, // Store the amount in Tag for easy access
                        Style = (Style)FindResource("WhiteComboBoxItemStyle")
                    };

                    summaryHoursCombo.Items.Add(item);
                }

                // Reset selection to placeholder
                summaryHoursCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        /// <summary>
        /// Populate overlay hours combobox with default 1-12 hours (for Sitting)
        /// </summary>
        private void PopulateSummaryDefaultHours()
        {
            try
            {
                if (summaryHoursCombo == null) return;

                // Clear existing items except placeholder
                var placeholder = summaryHoursCombo.Items[0];
                summaryHoursCombo.Items.Clear();
                summaryHoursCombo.Items.Add(placeholder);

                // Add default 1-12 hours
                for (int i = 1; i <= 12; i++)
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{i} hr",
                        Style = (Style)FindResource("WhiteComboBoxItemStyle")
                    };
                    summaryHoursCombo.Items.Add(item);
                }

                // Reset selection to placeholder
                summaryHoursCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private void txtHours_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (txtHours.SelectedIndex > 0)
            {
                hoursPlaceholder.Visibility = Visibility.Collapsed;
                SetForegroundColor(txtHours, false);
            }
            else
            {
                hoursPlaceholder.Visibility = Visibility.Visible;
                SetForegroundColor(txtHours, true);
            }

            // Sync overlay combobox with form hours selection
            SyncOverlayHoursFromForm();

            // Update overlay when hours change
            UpdateOverlayFromForm();
        }

        private void txtHours_DropDownOpened(object sender, EventArgs e)
        {
            // Scroll to top when dropdown opens
            var comboBox = sender as ComboBox;
            if (comboBox != null && comboBox.Items.Count > 0)
            {
                // Use Dispatcher to ensure the dropdown is fully rendered
                comboBox.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Scroll to the first item (placeholder)
                    comboBox.Items.MoveCurrentToFirst();
                    var firstItem = comboBox.ItemContainerGenerator.ContainerFromIndex(0);
                    if (firstItem != null)
                    {
                        (firstItem as ComboBoxItem)?.BringIntoView();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// Enhanced ID Type keyboard navigation handler with cycling down arrow selection
        /// </summary>
        private void IdType_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                var comboBox = sender as ComboBox;
                if (comboBox == null) return;

                // Check for Shift+Enter to trigger Generate Bill
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    e.Handled = true;
                    GenerateBill_Click(sender, new RoutedEventArgs());
                    return;
                }

                // Handle Down Arrow key for cycling through options
                if (e.Key == Key.Down)
                {
                    e.Handled = true; // Prevent default ComboBox behavior
                    
                    int currentIndex = comboBox.SelectedIndex;
                    int nextIndex;
                    
                    // Cycle through the items: placeholder(0) → Aadhar(1) → PNR Number(2) → PAN ID(3) → back to Aadhar(1)
                    if (currentIndex <= 0) // If placeholder or no selection
                    {
                        nextIndex = 1; // Select Aadhar
                    }
                    else if (currentIndex == 1) // If Aadhar
                    {
                        nextIndex = 2; // Select PNR Number
                    }
                    else if (currentIndex == 2) // If PNR Number
                    {
                        nextIndex = 3; // Select PAN ID
                    }
                    else // If PAN ID or beyond
                    {
                        nextIndex = 1; // Cycle back to Aadhar
                    }
                    
                    // Set the new selection
                    comboBox.SelectedIndex = nextIndex;
                    return;
                }

                // Handle Up Arrow key for cycling in reverse
                if (e.Key == Key.Up)
                {
                    e.Handled = true; // Prevent default ComboBox behavior
                    
                    int currentIndex = comboBox.SelectedIndex;
                    int nextIndex;
                    
                    // Cycle in reverse: PAN ID(3) → PNR Number(2) → Aadhar(1) → PAN ID(3)
                    if (currentIndex <= 1) // If placeholder or Aadhar
                    {
                        nextIndex = 3; // Select PAN ID
                    }
                    else if (currentIndex == 2) // If PNR Number
                    {
                        nextIndex = 1; // Select Aadhar
                    }
                    else // If PAN ID
                    {
                        nextIndex = 2; // Select PNR Number
                    }
                    
                    // Set the new selection
                    comboBox.SelectedIndex = nextIndex;
                    return;
                }

                // Handle Enter key
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    
                    // Validate that we have a valid selection (not placeholder)
                    if (cmbIdType.SelectedIndex <= 0)
                    {
                        // If no valid selection, select Aadhar by default
                        cmbIdType.SelectedIndex = 1;
                    }
                    
                    // The SelectionChanged event will handle moving to the next field
                    // and setting up the ID number field configuration
                    return;
                }

                // Handle Escape key to reset to placeholder
                if (e.Key == Key.Escape)
                {
                    comboBox.SelectedIndex = 0; // Reset to placeholder
                    e.Handled = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in IdType_PreviewKeyDown: {ex.Message}");
            }
        }

        /// <summary>
        /// Enhanced ID Number keyboard navigation handler with double-Enter support
        /// </summary>
        private void IdNumber_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                // Check for Shift+Enter to trigger Generate Bill
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    e.Handled = true;
                    GenerateBill_Click(sender, new RoutedEventArgs());
                    return;
                }

                if (e.Key == Key.Enter)
                {
                    e.Handled = true;

                    // Validate ID number based on selected ID type
                    if (!ValidateIdNumber())
                    {
                        errIdNumber.Visibility = Visibility.Visible;
                        idNumberEnterCount = 0; // Reset count on validation failure
                        return;
                    }
                    else
                    {
                        errIdNumber.Visibility = Visibility.Collapsed;
                    }

                    // Increment Enter count for this field
                    idNumberEnterCount++;
                    
                    // Restart the reset timer
                    enterResetTimer.Stop();
                    enterResetTimer.Start();

                    if (idNumberEnterCount == 1)
                    {
                        // First Enter: Move focus to Generate Bill button
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            GenerateBillButton.Focus();
                        }), System.Windows.Threading.DispatcherPriority.Render);
                    }
                    else if (idNumberEnterCount >= 2)
                    {
                        // Second Enter: Trigger Generate Bill if form is valid
                        if (ValidateForm())
                        {
                            // Reset count and trigger Generate Bill
                            idNumberEnterCount = 0;
                            enterResetTimer.Stop();
                            GenerateBill_Click(sender, new RoutedEventArgs());
                        }
                        else
                        {
                            // Form not valid, just focus the Generate Bill button
                            idNumberEnterCount = 0;
                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                GenerateBillButton.Focus();
                            }), System.Windows.Threading.DispatcherPriority.Render);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in IdNumber_PreviewKeyDown: {ex.Message}");
                idNumberEnterCount = 0; // Reset on error
            }
        }

        /// <summary>
        /// Validation method for ID number based on selected type
        /// </summary>
        private bool ValidateIdNumber()
        {
            try
            {
                if (cmbIdType.SelectedItem == null || cmbIdType.SelectedItem == idPlaceholder)
                    return false;

                string selectedIdType = ((ComboBoxItem)cmbIdType.SelectedItem).Content.ToString() ?? string.Empty;
                string idValue = txtIdNumber.Text.Trim();

                switch (selectedIdType)
                {
                    case "Aadhar":
                        return !string.IsNullOrWhiteSpace(idValue) && idValue.Length == 12 && idValue.All(char.IsDigit);

                    case "PNR Number":
                        return !string.IsNullOrWhiteSpace(idValue) && idValue.Length == 10 && idValue.All(char.IsDigit);

                    case "PAN ID":
                        return !string.IsNullOrWhiteSpace(idValue) && idValue.Length == 10 &&
                               Regex.IsMatch(idValue, @"^[A-Za-z]{5}[0-9]{4}[A-Za-z]{1}$");

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ValidateIdNumber: {ex.Message}");
                return false;
            }
        }

        private void cmbIdType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Reset Enter count when ID type changes
                idNumberEnterCount = 0;
                enterResetTimer.Stop();

                if (cmbIdType.SelectedItem is ComboBoxItem selectedItem && lblIdInput != null)
                {
                    string selectedType = selectedItem.Content?.ToString()?.ToLower() ?? "";

                    // ✅ Always clear ID number when user changes dropdown
                    txtIdNumber.Clear();
                    errIdNumber.Visibility = Visibility.Collapsed;

                    // Remove any previous input restrictions
                    txtIdNumber.PreviewTextInput -= NumbersOnly_PreviewTextInput;
                    txtIdNumber.PreviewTextInput -= Alphanumeric_PreviewTextInput;
                    txtIdNumber.PreviewTextInput -= IdNumber_PreviewTextInput;

                    if (string.IsNullOrWhiteSpace(selectedType) || selectedType == "select id")
                    {
                        lblIdInput.Text = "Enter ID Number";
                        txtIdNumber.MaxLength = 0;
                        txtIdNumber.IsEnabled = false; // Disable input until valid selection
                        SetForegroundColor(cmbIdType, true);
                    }
                    else
                    {
                        txtIdNumber.IsEnabled = true; // Enable input when valid ID type is selected
                        SetForegroundColor(cmbIdType, false);
                        errIdType.Visibility = Visibility.Collapsed;

                        // ✅ Apply input validation and setup based on selected type
                        switch (selectedType)
                        {
                            case "aadhar":
                                txtIdNumber.MaxLength = AADHAR_NUMBER_LENGTH;
                                txtIdNumber.Tag = "Enter 12 digit Aadhar number";
                                lblIdInput.Text = "Enter Aadhar Number";
                                txtIdNumber.PreviewTextInput += IdNumber_PreviewTextInput;
                                break;

                            case "pnr number":
                                txtIdNumber.MaxLength = PNR_NUMBER_LENGTH;
                                txtIdNumber.Tag = "Enter 10 digit PNR number";
                                lblIdInput.Text = "Enter PNR Number";
                                txtIdNumber.PreviewTextInput += IdNumber_PreviewTextInput;
                                break;

                            case "pan id":
                                txtIdNumber.MaxLength = PAN_NUMBER_LENGTH;
                                txtIdNumber.Tag = "Enter PAN ID (ABCDE1234F)";
                                lblIdInput.Text = "Enter PAN ID";
                                txtIdNumber.PreviewTextInput += IdNumber_PreviewTextInput;
                                break;

                            default:
                                txtIdNumber.MaxLength = 0;
                                break;
                        }

                        // ✅ Auto-navigate to ID Number field when valid selection is made
                        // Use dispatcher to ensure the ComboBox has finished processing
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // Close dropdown if it's still open
                            if (cmbIdType.IsDropDownOpen)
                            {
                                cmbIdType.IsDropDownOpen = false;
                            }
                            
                            // Focus on ID Number field
                            txtIdNumber.Focus();
                        }), System.Windows.Threading.DispatcherPriority.Input);
                    }

                    // ✅ Clear the ID number *again* (extra safety)
                    txtIdNumber.Text = string.Empty;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in cmbIdType_SelectionChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Enhanced ID Number input validation based on selected type
        /// </summary>
        private void IdNumber_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (cmbIdType.SelectedItem == null || cmbIdType.SelectedItem == idPlaceholder)
                {
                    e.Handled = true;
                    return;
                }

                string selectedIdType = ((ComboBoxItem)cmbIdType.SelectedItem).Content.ToString() ?? string.Empty;

                switch (selectedIdType)
                {
                    case "Aadhar":
                    case "PNR Number":
                        // Only allow digits
                        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
                        break;

                    case "PAN ID":
                        string currentText = txtIdNumber.Text;
                        int caretIndex = txtIdNumber.CaretIndex;

                        // PAN format: ABCDE1234F (5 letters, 4 digits, 1 letter)
                        if (caretIndex < 5)
                        {
                            // First 5 positions: only letters (accept both upper and lower case)
                            e.Handled = !Regex.IsMatch(e.Text, "^[a-zA-Z]+$");

                            // Convert to uppercase if it's a valid letter
                            if (!e.Handled)
                            {
                                // Cancel the original input and insert uppercase version
                                e.Handled = true;

                                // Insert uppercase text at current position
                                string upperText = e.Text.ToUpper();
                                int currentCaretIndex = txtIdNumber.CaretIndex;
                                string newText = currentText.Insert(currentCaretIndex, upperText);

                                // Update text and caret position
                                txtIdNumber.Text = newText;
                                txtIdNumber.CaretIndex = currentCaretIndex + upperText.Length;
                            }
                        }
                        else if (caretIndex >= 5 && caretIndex < 9)
                        {
                            // Positions 5-8: only digits
                            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
                        }
                        else if (caretIndex == 9)
                        {
                            // Last position: only letter (accept both upper and lower case)
                            e.Handled = !Regex.IsMatch(e.Text, "^[a-zA-Z]+$");

                            // Convert to uppercase if it's a valid letter
                            if (!e.Handled)
                            {
                                // Cancel the original input and insert uppercase version
                                e.Handled = true;

                                // Insert uppercase text at current position
                                string upperText = e.Text.ToUpper();
                                int currentCaretIndex = txtIdNumber.CaretIndex;
                                string newText = currentText.Insert(currentCaretIndex, upperText);

                                // Update text and caret position
                                txtIdNumber.Text = newText;
                                txtIdNumber.CaretIndex = currentCaretIndex + upperText.Length;
                            }
                        }
                        break;

                    default:
                        e.Handled = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in IdNumber_PreviewTextInput: {ex.Message}");
            }
        }

        /// <summary>
        /// Enhanced ID Number pasting validation
        /// </summary>
        private void IdNumber_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            try
            {
                if (cmbIdType.SelectedItem == null || cmbIdType.SelectedItem == idPlaceholder)
                {
                    e.CancelCommand();
                    return;
                }

                if (e.DataObject.GetDataPresent(DataFormats.Text))
                {
                    string pastedText = (e.DataObject.GetData(DataFormats.Text) as string) ?? string.Empty;
                    string selectedIdType = ((ComboBoxItem)cmbIdType.SelectedItem).Content.ToString() ?? string.Empty;

                    bool isValid = selectedIdType switch
                    {
                        "Aadhar" => Regex.IsMatch(pastedText, @"^[0-9]{12}$"),
                        "PNR Number" => Regex.IsMatch(pastedText, @"^[0-9]{10}$"),
                        "PAN ID" => Regex.IsMatch(pastedText, @"^[a-zA-Z]{5}[0-9]{4}[a-zA-Z]{1}$"),
                        _ => false
                    };

                    if (!isValid)
                    {
                        e.CancelCommand();
                    }
                    else if (selectedIdType == "PAN ID")
                    {
                        // For PAN ID, convert to uppercase
                        e.CancelCommand();

                        // Manually set the uppercase text
                        txtIdNumber.Text = pastedText.ToUpper();
                        txtIdNumber.CaretIndex = txtIdNumber.Text.Length;
                    }
                }
                else
                {
                    e.CancelCommand();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in IdNumber_Pasting: {ex.Message}");
                e.CancelCommand();
            }
        }

        // ===== Constructor with Dashboard instance =====
        public NewBooking(Dashboard dashboard) : this()
        {
            _dashboardInstance = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            GenerateBillIDFromPhone();  // Initialize DispatcherTimer to update date and time

            dateTimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // update every second
            };
            dateTimeTimer.Tick += UpdateDateTime;
            dateTimeTimer.Start();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            // Stop and dispose timers to prevent memory leaks
            dateTimeTimer?.Stop();
            enterResetTimer?.Stop();
            printEnterResetTimer?.Stop();
        }

        private void BillingControl_CloseRequested(object? sender, EventArgs e)
        {
            // Clear all input fields in NewBooking
            txtCustomer.Text = string.Empty;
            txtPhone.Text = string.Empty;
            txtPersons.Text = string.Empty;
            txtBookingID.Text = string.Empty;
            txtRate.Text = string.Empty;
            txtTotalAmount.Text = string.Empty;

            // Reset ComboBoxes
            txtSeats.SelectedIndex = 0;
            txtHours.SelectedIndex = 0;

            // Reset placeholders visibility if any
            var seatsPlaceholder = GetSeatsPlaceholder();
            if (seatsPlaceholder != null) seatsPlaceholder.Visibility = Visibility.Visible;
            hoursPlaceholder.Visibility = Visibility.Visible;

            // Generate new Booking ID for next entry
            GenerateBillIDFromPhone();

        }



        private void UpdateDateTime(object? sender, EventArgs e)
        {
            txtBookingDate.Text = DateTime.Now.ToString("MM/dd/yyyy");
            txtBookingTime.Text = DateTime.Now.ToString("HH:mm");
        }

        // Amount Calculation
        private void UpdateAmount()
        {
            if (!int.TryParse(txtPersons.Text, out int persons) || persons <= 0)
            {
                txtRate.Text = "0";
                txtTotalAmount.Text = "0";
                return;
            }

            var selectedSeatItem = txtSeats.SelectedItem as ComboBoxItem;
            string seatType = selectedSeatItem?.Content?.ToString()?.ToLower() ?? "";
            string hoursText = (txtHours.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

            if (string.IsNullOrEmpty(seatType) || string.IsNullOrEmpty(hoursText))
            {
                txtRate.Text = "0";
                txtTotalAmount.Text = "0";
                return;
            }

            var selectedHourItem = txtHours.SelectedItem as ComboBoxItem;
            decimal pricePerPerson = 0;

            // Check if this is a pricing tier (Sleeper with stored amount in Tag)
            if (selectedHourItem?.Tag is decimal tierAmount)
            {
                // For pricing tiers, use the amount directly (already calculated for the hour range)
                pricePerPerson = tierAmount;
                //Console.WriteLine($"Using pricing tier amount: ₹{tierAmount}");
            }
            else
            {
                // For regular hours (Sitting), calculate: rate * hours
                var hoursParts = hoursText.Split(' ');
                if (hoursParts.Length == 0 || !int.TryParse(hoursParts[0], out int totalHours) || totalHours <= 0)
                {
                    txtRate.Text = "0";
                    txtTotalAmount.Text = "0";
                    return;
                }

                // Get base rate from database
                decimal rate = OfflineBookingStorage.GetBookingTypeAmount(seatType);

                // If no rate found, show error
                if (rate <= 0)
                {
                    MessageBox.Show($"Could not find rate for '{seatType}'.\nPlease refresh booking types.",
                        "Rate Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtRate.Text = "0";
                    txtTotalAmount.Text = "0";
                    return;
                }

                // Calculate: rate * hours
                pricePerPerson = rate * totalHours;
                //Console.WriteLine($"Calculated price: ₹{rate} × {totalHours} hrs = ₹{pricePerPerson}");
            }

            // Set price per person
            txtRate.Text = pricePerPerson.ToString("0.00");
            
            // Recalculate total with discount and advance
            RecalculatePricing();
        }





        // ===== BillingControl Print Event =====
        private void BillingControl_PrintRequested(
            string billId,
            string customerName,
            string phoneNo,
            DateTime startTime,
            int totalHours,
            int persons,
            double rate,
            double paidAmount)
        {
            // This method is no longer needed since Dashboard counts are updated dynamically
            // The UpdateCountsFromBookings() method in Dashboard will be called after booking is added
            if (_dashboardInstance != null)
            {
                _dashboardInstance.LoadBookings();
                _dashboardInstance.UpdateCountsFromBookings();
            }
        }

        // Button Click Event to Generate Bill
        private void GenerateBill_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateForm())
            {
                ShowAlert("warning", "Please fill all required fields before generating the bill.");
                return;
            }

            // Populate summary overlay with current form data
            PopulateSummaryOverlay();

            // Show pricing summary overlay
            PricingSummaryOverlay.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Populate the pricing summary overlay with current booking details
        /// </summary>
        private void PopulateSummaryOverlay()
        {
            try
            {
                // Number of Persons
                if (summaryPersons != null && int.TryParse(txtPersons.Text, out int persons))
                {
                    summaryPersons.Text = persons.ToString();
                }

                // Seat Type
                if (summarySeatType != null)
                {
                    summarySeatType.Text = (txtSeats.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "-";
                }

                // Per Person Price - Get from database based on seat type and selected tier (for Sleeper)
                string selectedSeatType = (txtSeats?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
                if (summaryPrice != null && !string.IsNullOrEmpty(selectedSeatType))
                {
                    decimal pricePerPerson = 0;
                    
                    if (selectedSeatType.ToLower() == "sleeper" || selectedSeatType.ToLower() == "sleeping")
                    {
                        // For Sleeper, get price from the selected tier (hour range)
                        if (summaryHoursCombo.SelectedIndex > 0)
                        {
                            var selectedItem = summaryHoursCombo.SelectedItem as ComboBoxItem;
                            if (selectedItem != null && selectedItem.Tag != null && decimal.TryParse(selectedItem.Tag.ToString(), out decimal tierPrice))
                            {
                                pricePerPerson = tierPrice;
                            }
                            else
                            {
                                pricePerPerson = OfflineBookingStorage.GetBookingTypeAmount(selectedSeatType);
                            }
                        }
                        else
                        {
                            pricePerPerson = OfflineBookingStorage.GetBookingTypeAmount(selectedSeatType);
                        }
                    }
                    else
                    {
                        // For Sitting: Standard price per hour
                        pricePerPerson = OfflineBookingStorage.GetBookingTypeAmount(selectedSeatType);
                    }
                    
                    summaryPrice.Text = pricePerPerson.ToString("F2");
                }

                // Start and End Times
                UpdateStartAndEndTimes();

                // Total Hours ComboBox - Sync with form selection
                if (summaryHoursCombo != null && txtHours.SelectedIndex > 0)
                {
                    summaryHoursCombo.SelectedIndex = txtHours.SelectedIndex;
                    if (summaryHoursPlaceholder != null)
                    {
                        summaryHoursPlaceholder.Visibility = Visibility.Collapsed;
                    }
                }
                else if (summaryHoursCombo != null)
                {
                    summaryHoursCombo.SelectedIndex = 0;
                    if (summaryHoursPlaceholder != null)
                    {
                        summaryHoursPlaceholder.Visibility = Visibility.Visible;
                    }
                }

                // Discount (start fresh)
                if (summaryDiscount != null)
                {
                    summaryDiscount.Text = "0";
                }

                // Room Number - clear it
                if (summaryRoomNumber != null)
                {
                    summaryRoomNumber.Clear();
                }

                // Check if Sleeper seat type is selected to show/hide room number field
                UpdateRoomNumberVisibility();

                // Recalculate Total Amount
                RecalculateSummaryTotal();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error populating summary: {ex.Message}");
                ShowAlert("error", $"Error loading summary: {ex.Message}");
            }
        }

        /// <summary>
        /// Show or hide room number field based on seat type selection
        /// </summary>
        private void UpdateRoomNumberVisibility()
        {
            if (roomNumberGrid == null || txtSeats == null) return;

            var selectedSeatType = (txtSeats.SelectedItem as ComboBoxItem)?.Content?.ToString();
            
            if (selectedSeatType == "Sleeper")
            {
                roomNumberGrid.Visibility = Visibility.Visible;
            }
            else
            {
                roomNumberGrid.Visibility = Visibility.Collapsed;
                if (summaryRoomNumber != null)
                {
                    summaryRoomNumber.Clear();
                }
            }
        }

        /// <summary>
        /// Recalculate the total amount in the summary based on persons, price, hours, and discount
        /// </summary>
        private void RecalculateSummaryTotal()
        {
            try
            {
                if (summaryPersons == null || summaryPrice == null || summaryDiscount == null || summaryTotalAmount == null || summaryHoursCombo == null)
                {
                    return; // Controls not initialized yet
                }

                if (!int.TryParse(summaryPersons.Text, out int persons) || persons <= 0)
                    persons = 0;

                if (!decimal.TryParse(summaryPrice.Text, out decimal pricePerPerson) || pricePerPerson < 0)
                    pricePerPerson = 0;

                if (!decimal.TryParse(summaryDiscount.Text, out decimal discount) || discount < 0)
                    discount = 0;

                // For Sleeper: Price already includes the tier (hour range), so just multiply by persons
                // For Sitting: Price is per hour, so multiply by hours and persons
                string seatType = (txtSeats?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                decimal totalAmount = 0;

                if (seatType.ToLower() == "sleeper" || seatType.ToLower() == "sleeping")
                {
                    // Sleeper: persons * pricePerPerson (price already includes tier)
                    totalAmount = (persons * pricePerPerson) - discount;
                }
                else
                {
                    // Sitting: Parse hours and calculate persons * price * hours
                    int hours = 0;
                    string hoursText = (summaryHoursCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(hoursText))
                    {
                        string[] parts = hoursText.Split(' ');
                        if (int.TryParse(parts[0], out int parsedHours) && parsedHours > 0)
                            hours = parsedHours;
                    }
                    // Calculate: (persons * price per person * hours) - discount
                    totalAmount = (persons * pricePerPerson * hours) - discount;
                }

                if (totalAmount < 0) totalAmount = 0;

                summaryTotalAmount.Text = totalAmount.ToString("F2");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error recalculating summary total: {ex.Message}");
                if (summaryTotalAmount != null)
                {
                    summaryTotalAmount.Text = "0";
                }
            }
        }

        /// <summary>
        /// Handle discount change in summary overlay
        /// </summary>
        private void SummaryDiscount_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                RecalculateSummaryTotal();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in discount text changed: {ex.Message}");
            }
        }

        /// <summary>
        /// <summary>
        /// Placeholder for old hour adjustment methods (now using ComboBox)
        /// </summary>

        /// <summary>
        /// Get total hours from database based on seat type
        /// For Sleeper: Get max hours from GetHourlyPricingTiers()
        /// For Sitting: Returns 12 (max from 1-12 hr range)
        /// </summary>
        private int GetTotalHoursBySeatType(string seatType)
        {
            try
            {
                if (string.IsNullOrEmpty(seatType))
                    return 0;

                // Check if it's Sleeper type
                if (seatType.Equals("Sleeper", StringComparison.OrdinalIgnoreCase) || 
                    seatType.Equals("Sleeping", StringComparison.OrdinalIgnoreCase))
                {
                    // Get hourly pricing tiers from database for Sleeper
                    var pricingTiers = OfflineBookingStorage.GetHourlyPricingTiers();
                    
                    if (pricingTiers != null && pricingTiers.Count > 0)
                    {
                        // Return the maximum hours from the pricing tiers
                        return pricingTiers.Max(t => t.MaxHours);
                    }
                }
                
                // For Sitting or other types, return 12 (max from 1-12 hr range)
                return 12;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting total hours by seat type: {ex.Message}");
                return 12; // Default fallback
            }
        }

        /// <summary>
        /// <summary>
        /// Handle Enter key press on Print button
        /// </summary>
        /// <summary>
        /// Handle mouse clicks on Print button - allow single click
        /// </summary>
        private void PrintButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Allow mouse clicks to work normally
            // The double-Enter requirement only applies to keyboard navigation
            e.Handled = false;
        }

        /// <summary>
        /// Handle Enter key press in summary card - discount/room number fields only
        /// NOTE: Print button requires double-Enter key press only
        /// </summary>
        private void SummaryCard_KeyDown(object sender, KeyEventArgs e)
        {
            // Do NOT trigger print from summary card fields
            // Print button requires explicit double-Enter focus on Print button itself
            if (e.Key == Key.Return)
            {
                // Move to next field or close overlay
                e.Handled = false;
            }
        }

        /// <summary>
        /// Print button - execute the original GenerateBill logic
        /// </summary>
        private async void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            // Update the txtTotalAmount from summary before generating bill
            if (decimal.TryParse(summaryTotalAmount.Text, out decimal summaryTotal))
            {
                txtTotalAmount.Text = summaryTotal.ToString("F2");
            }

            // Close the overlay
            PricingSummaryOverlay.Visibility = Visibility.Collapsed;

            // Execute original bill generation logic
            await ExecuteGenerateBill();
        }

        /// <summary>
        /// Handle Enter key press on Print button for double-enter confirmation only
        /// Single Enter or Click will NOT trigger print
        /// </summary>
        private void PrintButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Return)
                {
                    printButtonEnterCount++;
                    e.Handled = true;

                    // Reset timer on new press
                    printEnterResetTimer.Stop();
                    printEnterResetTimer.Start();

                    if (printButtonEnterCount == 2)
                    {
                        // Double Enter - execute print
                        printButtonEnterCount = 0;
                        printEnterResetTimer.Stop();
                        
                        // Trigger print
                        PrintButton_Click(sender, new RoutedEventArgs());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PrintButton_PreviewKeyDown: {ex.Message}");
            }
        }

        /// <summary>
        /// <summary>
        /// Handle double-click on overlay card to trigger print
        /// </summary>
        private void OverlayCard_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Check if it's a double-click
                if (e.ClickCount == 2)
                {
                    // Call the print button click handler
                    PrintButton_Click(PrintButton, new RoutedEventArgs());
                    e.Handled = true; // Mark event as handled to prevent double-click from selecting text
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OverlayCard_PreviewMouseDown: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel button - close overlay without processing
        /// </summary>
        private void CancelOverlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PricingSummaryOverlay != null)
                {
                    PricingSummaryOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in CancelOverlay_Click: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute the actual bill generation (separated from UI trigger)
        /// </summary>
        private async Task ExecuteGenerateBill()
        {
            // Show loading overlay
            LoaderOverlay.Visibility = Visibility.Visible;

            // Disable button to prevent multiple clicks during async operation
            GenerateBillButton.IsEnabled = false;

            try
            {
                string customerName = txtCustomer.Text.Trim();
                string billId = txtBookingID.Text.Trim();
                string phoneNo = txtPhone.Text.Trim();

                string seatType = ((txtSeats.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "").Trim();
                string proofType = ((cmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "").ToLower().Trim();
                string proofId = txtIdNumber.Text.Trim();
                string paymentMethod = "cash";

                int totalHours = 0;
                var selectedHours = (txtHours.SelectedItem as ComboBoxItem)?.Content?.ToString();
                
                if (!string.IsNullOrEmpty(selectedHours))
                {
                    // Handle both formats: "3 hr" and "1-3 hrs"
                    string hoursPart = selectedHours.Split(' ')[0]; // Get "3" or "1-3"
                    
                    if (hoursPart.Contains("-"))
                    {
                        // Handle range format "1-3" - use the max value
                        var rangeParts = hoursPart.Split('-');
                        
                        if (rangeParts.Length == 2 && int.TryParse(rangeParts[1], out int maxHours))
                        {
                            totalHours = maxHours;
                        }
                    }
                    else
                    {
                        // Handle single number format "3"
                        if (int.TryParse(hoursPart, out int singleHour))
                        {
                            totalHours = singleHour;
                        }
                    }
                }

                int persons = int.TryParse(txtPersons.Text, out var parsedPersons) ? parsedPersons : 0;
                decimal rate = decimal.TryParse(txtRate.Text, out var r) ? r : 0;
                decimal paidAmount = 0; // No advance amount

                decimal totalAmount = decimal.TryParse(txtTotalAmount.Text, out var t) ? t : 0;
                decimal balance = totalAmount - paidAmount;

                string? workerId = LocalStorage.GetItem("workerId");

                // Calculate expected end time (out_time) based on start time + total hours
                TimeSpan currentInTime = DateTime.Now.TimeOfDay;
                TimeSpan calculatedOutTime = currentInTime.Add(TimeSpan.FromHours(totalHours));
                
                // Handle if out_time goes beyond midnight (next day)
                if (calculatedOutTime.TotalHours >= 24)
                {
                    calculatedOutTime = calculatedOutTime.Subtract(TimeSpan.FromHours(24));
                }

                var booking = new Booking1
                {
                    booking_id = billId,
                    worker_id = workerId,
                    guest_name = customerName,
                    phone_number = phoneNo,
                    number_of_persons = persons,
                    booking_type = seatType,
                    room_number = summaryRoomNumber?.Text,
                    total_hours = totalHours,
                    booking_date = DateTime.Now,
                    in_time = currentInTime,
                    out_time = calculatedOutTime,  // Set expected end time
                    proof_type = proofType,
                    proof_id = proofId,
                    price_per_person = rate,
                    total_amount = totalAmount,
                    paid_amount = paidAmount,
                    balance_amount = balance,
                    payment_method = paymentMethod,
                    status = "active"
                };

                // Use the new online-first save method
                await OfflineBookingStorage.SaveBookingAsync(booking, showMessages: false);

                // Refresh Dashboard to update counts and show new booking
                if (_dashboardInstance != null)
                {
                    _dashboardInstance.LoadBookings();
                    _dashboardInstance.UpdateCountsFromBookings();
                }

                ClearBookingForm();

                cmbIdType.SelectedIndex = 0;
                lblIdInput.Text = "Enter ID Number";
                txtIdNumber.Text = string.Empty;

                // --- Direct Print (No Preview) ---
                PrintBillDirectly(booking);
            }
            catch (Exception ex)
            {
                ShowAlert("error", $"Error saving booking: {ex.Message}");
                Logger.LogError(ex);
            }
            finally
            {
                // Hide loading overlay
                LoaderOverlay.Visibility = Visibility.Collapsed;
                
                // Re-enable button after operation completes
                GenerateBillButton.IsEnabled = true;
            }
        }

        // Handle Ctrl+Shift+P shortcut for direct print
        private void NewBooking_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.P)
            {
                e.Handled = true;
                DirectPrintSampleBill();
            }
        }

        // Direct print without preview
        private void DirectPrintSampleBill()
        {
            try
            {
                // Create a sample booking for print preview
                var sampleBooking = new Booking1
                {
                    booking_id = "SAMPLE123456",
                    guest_name = "Sample Customer",
                    phone_number = "9876543210",
                    number_of_persons = 2,
                    booking_type = "standard",
                    total_hours = 3,
                    price_per_person = 100,
                    total_amount = 600,
                    paid_amount = 300,
                    balance_amount = 300
                };

                PrintBillDirectly(sampleBooking);
            }
            catch (Exception ex)
            {
                ShowAlert("error", $"Failed to print sample bill: {ex.Message}");
                Logger.LogError(ex);
            }
        }

        // Print bill directly without preview
        private void PrintBillDirectly(Booking1 booking)
        {
            try
            {
                bool printed = ReceiptHelper.GenerateAndPrintReceipt(booking);
                
                if (!printed)
                {
                    MessageBox.Show("Failed to print receipt. Please check printer connection.", 
                        "Printer Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Print error: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void ClearBookingForm()
        {
            // Reset Enter count and timer
            idNumberEnterCount = 0;
            enterResetTimer.Stop();

            // Clear all textboxes
            txtFirstName.Text = string.Empty;
            txtCustomer.Text = string.Empty;
            txtPhone.Text = string.Empty;
            txtPersons.Text = string.Empty;
            txtRate.Text = string.Empty;
            txtTotalAmount.Text = string.Empty;
            txtIdNumber.Text = string.Empty;
            txtBookingID.Text = string.Empty;

            // Reset dropdowns
            txtSeats.SelectedIndex = 0;
            txtHours.SelectedIndex = 0;
            cmbIdType.SelectedIndex = 0;

            // Reset placeholders
            var seatsPlaceholder = GetSeatsPlaceholder();
            if (seatsPlaceholder != null) seatsPlaceholder.Visibility = Visibility.Visible;
            hoursPlaceholder.Visibility = Visibility.Visible;

            // Reset foreground colors to grey (placeholder color)
            SetForegroundColor(txtSeats, true);
            SetForegroundColor(txtHours, true);
            SetForegroundColor(cmbIdType, true);
            SetForegroundColor(txtFirstName, true);
            SetForegroundColor(txtPhone, true);
            SetForegroundColor(txtPersons, true);
            SetForegroundColor(txtIdNumber, true);

            // Reset label text
            lblIdInput.Text = "Enter ID Number";

            // Clear error labels
            errCustomer.Visibility = Visibility.Collapsed;
            errPhone.Visibility = Visibility.Collapsed;
            errPersons.Visibility = Visibility.Collapsed;
            errSeats.Visibility = Visibility.Collapsed;
            errHours.Visibility = Visibility.Collapsed;
            errIdType.Visibility = Visibility.Collapsed;
            errIdNumber.Visibility = Visibility.Collapsed;

            // Reload seating types from LocalStorage to restore ComboBox items
            string? seatingTypesStr = LocalStorage.GetItem("seating_types");
            if (!string.IsNullOrEmpty(seatingTypesStr) && int.TryParse(seatingTypesStr, out int seatingTypes))
            {
                ConfigureSeatingOptions(seatingTypes);
            }
            else
            {
                // Default to both options if not found
                ConfigureSeatingOptions(2);
            }

            // Generate a fresh new Bill ID
            GenerateBillIDFromPhone();

            // Optionally, move focus to the first field
            txtFirstName.Focus();
        }


        private bool ValidateForm()
        {
            bool isValid = true;

            // Reset all errors first
            errCustomer.Visibility = Visibility.Collapsed;
            errPhone.Visibility = Visibility.Collapsed;
            errPersons.Visibility = Visibility.Collapsed;
            errSeats.Visibility = Visibility.Collapsed;
            errHours.Visibility = Visibility.Collapsed;
            errIdType.Visibility = Visibility.Collapsed;
            errIdNumber.Visibility = Visibility.Collapsed;

            // Validate Name
            if (string.IsNullOrWhiteSpace(txtFirstName.Text))
            {
                errCustomer.Visibility = Visibility.Visible;
                isValid = false;
            }

            // Validate Phone
            if (string.IsNullOrWhiteSpace(txtPhone.Text) || txtPhone.Text.Length != PHONE_NUMBER_LENGTH || !_regexNumeric.IsMatch(txtPhone.Text))
            {
                errPhone.Visibility = Visibility.Visible;
                isValid = false;
            }

            // Validate Persons
            if (string.IsNullOrWhiteSpace(txtPersons.Text) || !int.TryParse(txtPersons.Text, out int persons))
            {
                errPersons.Visibility = Visibility.Visible;
                isValid = false;
            }
            else if (persons <= 0 || persons > 300)
            {
                errPersons.Visibility = Visibility.Visible;
                MessageBox.Show("Number of persons must be between 1 and 300.", 
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                isValid = false;
            }

            // Validate Seat Type
            if (txtSeats.SelectedItem is ComboBoxItem seatItem)
            {
                if (seatItem.Content.ToString() == "Select")
                {
                    errSeats.Visibility = Visibility.Visible;
                    isValid = false;
                }
            }
            else
            {
                errSeats.Visibility = Visibility.Visible;
                isValid = false;
            }

            // Validate Hours
            if (txtHours.SelectedItem is ComboBoxItem hourItem)
            {
                if (hourItem.Content.ToString() == "Select hours")
                {
                    errHours.Visibility = Visibility.Visible;
                    isValid = false;
                }
            }
            else
            {
                errHours.Visibility = Visibility.Visible;
                isValid = false;
            }

            // Validate Discount (must not exceed base total)
            if (!string.IsNullOrWhiteSpace(txtDiscount.Text))
            {
                if (decimal.TryParse(txtDiscount.Text, out decimal discount) && discount > 0)
                {
                    // Get base total (price per person * number of persons)
                    if (decimal.TryParse(txtRate.Text, out decimal pricePerPerson) && 
                        int.TryParse(txtPersons.Text, out int personCount))
                    {
                        decimal baseTotal = pricePerPerson * personCount;
                        
                        if (discount > baseTotal)
                        {
                            MessageBox.Show($"Discount (₹{discount:0.00}) cannot exceed the base total amount (₹{baseTotal:0.00}).", 
                                "Invalid Discount", MessageBoxButton.OK, MessageBoxImage.Warning);
                            isValid = false;
                        }
                    }
                }
            }

            // Validate ID Type
            if (cmbIdType.SelectedItem is ComboBoxItem idItem)
            {
                if (idItem.Content.ToString() == "Select ID")
                {
                    errIdType.Visibility = Visibility.Visible;
                    isValid = false;
                }
            }
            else
            {
                errIdType.Visibility = Visibility.Visible;
                isValid = false;
            }

            // Validate ID Number
            if (string.IsNullOrWhiteSpace(txtIdNumber.Text))
            {
                errIdNumber.Visibility = Visibility.Visible;
                isValid = false;
            }

            return isValid;
        }

        // Cancel Button Click Event
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Close any open overlays without clearing the form
            if (PricingSummaryOverlay != null && PricingSummaryOverlay.Visibility == Visibility.Visible)
            {
                PricingSummaryOverlay.Visibility = Visibility.Collapsed;
            }
            // Note: Form data is NOT cleared on cancel button click
        }


        // ===== Input Validations =====
        private void UppercaseOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = !_regex.IsMatch(e.Text);

        private void NumbersOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !_regexNumeric.IsMatch(e.Text);

            if (!e.Handled)
            {
                if (sender is TextBox tb && tb.Text.Length >= tb.MaxLength && tb.MaxLength > 0)
                    e.Handled = true;
            }
        }



        private void Alphanumeric_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !_regexAlphanumeric.IsMatch(e.Text);

            if (!e.Handled)
            {
                if (sender is TextBox tb && tb.Text.Length >= tb.MaxLength && tb.MaxLength > 0)
                    e.Handled = true;
            }
        }




        private void UppercaseOnly_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (!_regex.IsMatch(text)) e.CancelCommand();
            }
            else e.CancelCommand();
        }

        private void NumbersOnly_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (!_regexNumeric.IsMatch(text)) e.CancelCommand();
            }
            else e.CancelCommand();
        }

        // ===== Booking ID =====
        private void GenerateBillIDFromPhone()
        {
            string phone = txtPhone.Text.Trim();

            // Only generate if phone number looks valid
            if (string.IsNullOrWhiteSpace(phone) || phone.Length < 5)
                return;

            string datePart = DateTime.Now.ToString("ddMMyyyy");
            string timePart = DateTime.Now.ToString("HHmm");
            string billID = $"{datePart}{phone}{timePart}";  // ✅ no underscores

            txtBookingID.Text = billID;
        }



        private void txtTotalAmount_TextChanged(object sender, TextChangedEventArgs e) { }

        private void Control_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Allow all typing — only handle Enter key for navigation
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                // Define the sequential order of controls
                var controlOrder = new Control[]
                {
                    txtFirstName, txtPhone, txtPersons, txtSeats, txtHours, cmbIdType, txtIdNumber
                };

                // Find current control index
                int currentIndex = Array.IndexOf(controlOrder, sender);
                
                if (currentIndex == -1)
                    return;

                // Special action for txtPhone
                if (sender == txtPhone)
                {
                    GenerateBillIDFromPhone();
                }

                // Validate current control
                var currentControl = controlOrder[currentIndex];
                if (!ValidateControl(currentControl))
                {
                    // Stay on current control if validation fails
                    currentControl.Focus();
                    return;
                }

                // Move to next control
                if (currentIndex + 1 < controlOrder.Length)
                {
                    int nextIndex = currentIndex + 1;
                    Control nextControl = controlOrder[nextIndex];

                    // Special handling: Skip Seat Type if only one item or already selected
                    if (nextControl == txtSeats && (txtSeats.Items.Count <= 1 || txtSeats.SelectedIndex > 0))
                    {
                        // Move to Total Hours instead
                        if (nextIndex + 1 < controlOrder.Length)
                        {
                            controlOrder[nextIndex + 1].Focus();
                        }
                        else
                        {
                            if (AreAllRequiredFieldsFilled())
                            {
                                GenerateBillButton.Focus();
                            }
                            else
                            {
                                ShowAlert("warning", "Please fill in all required fields before continuing.");
                            }
                        }
                    }
                    else
                    {
                        nextControl.Focus();
                    }
                }
                else
                {
                    // We're at the last field, check if all required fields are filled
                    if (AreAllRequiredFieldsFilled())
                    {
                        GenerateBillButton.Focus();
                    }
                    else
                    {
                        ShowAlert("warning", "Please fill in all required fields before continuing.");
                    }
                }
            }
        }

        private bool ValidateControl(object control)
        {
            switch (control)
            {
                case TextBox tb when tb == txtFirstName:
                    if (string.IsNullOrWhiteSpace(tb.Text))
                    {
                        errCustomer.Visibility = Visibility.Visible;
                        return false;
                    }
                    else { errCustomer.Visibility = Visibility.Collapsed; }
                    break;

                case TextBox tb when tb == txtPhone:
                    if (string.IsNullOrWhiteSpace(tb.Text))
                    {
                        errPhone.Visibility = Visibility.Visible;
                        return false;
                    }
                    else { errPhone.Visibility = Visibility.Collapsed; }
                    break;

                case TextBox tb when tb == txtPersons:
                    if (string.IsNullOrWhiteSpace(tb.Text))
                    {
                        errPersons.Visibility = Visibility.Visible;
                        return false;
                    }
                    else { errPersons.Visibility = Visibility.Collapsed; }
                    break;

                case ComboBox cb when cb == txtSeats:
                    if (cb.SelectedIndex <= 0)
                    {
                        errSeats.Visibility = Visibility.Visible;
                        return false;
                    }
                    else { errSeats.Visibility = Visibility.Collapsed; }
                    break;

                case ComboBox cb when cb == txtHours:
                    if (cb.SelectedIndex <= 0)
                    {
                        errHours.Visibility = Visibility.Visible;
                        return false;
                    }
                    else { errHours.Visibility = Visibility.Collapsed; }
                    break;

                // Skip txtPaid validation

                case ComboBox cb when cb == cmbIdType:
                    if (cb.SelectedIndex <= 0)
                    {
                        errIdType.Visibility = Visibility.Visible;
                        return false;
                    }
                    else { errIdType.Visibility = Visibility.Collapsed; }
                    break;

                case TextBox tb when tb == txtIdNumber:
                    if (string.IsNullOrWhiteSpace(tb.Text))
                    {
                        errIdNumber.Visibility = Visibility.Visible;
                        return false;
                    }
                    else { errIdNumber.Visibility = Visibility.Collapsed; }
                    break;
            }
            return true;
        }

        private bool AreAllRequiredFieldsFilled()
        {
            var requiredControls = new Control[]
            {
        txtFirstName, txtPhone, txtPersons, txtSeats, txtHours, cmbIdType, txtIdNumber
            };

            foreach (var control in requiredControls)
            {
                // TextBox check
                if (control is TextBox tb)
                {
                    if (string.IsNullOrWhiteSpace(tb.Text))
                        return false;
                }
                // ComboBox check
                else if (control is ComboBox cb)
                {
                    if (cb.SelectedIndex <= 0)
                        return false;
                }
            }

            return true;
        }


        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Load booking types from local database
            LoadBookingTypesFromDatabase();
            
            // Initialize the hours dropdown to show placeholder
            if (txtHours.SelectedIndex == -1)
            {
                txtHours.SelectedIndex = 0; // Select placeholder
            }
            
            // Initialize dropdown foreground colors to grey (placeholder state)
            SetForegroundColor(txtHours, true);
            SetForegroundColor(cmbIdType, true);
            
            // Initialize TextBox foreground colors to grey (placeholder state)
            SetForegroundColor(txtFirstName, true);
            SetForegroundColor(txtPhone, true);
            SetForegroundColor(txtPersons, true);
            SetForegroundColor(txtIdNumber, true);

            // Ensure proper scrolling setup after layout is complete
            Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsureContentVisible();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Handles mouse wheel scrolling for the entire UserControl
        /// </summary>
        private void UserControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Only prevent scrolling when ComboBox dropdowns are open to avoid conflicts
            if (IsAnyComboBoxOpen())
            {
                // Don't interfere with dropdown scrolling
                return;
            }

            // Let the ScrollViewer handle all other scrolling naturally
            // No manual intervention needed - the ScrollViewer configuration handles it
        }

        /// <summary>
        /// Scrolls to the top of the form content
        /// </summary>
        public void ScrollToTop()
        {
            FormScrollViewer?.ScrollToTop();
        }

        /// <summary>
        /// Scrolls to the bottom of the form content
        /// </summary>
        public void ScrollToBottom()
        {
            FormScrollViewer?.ScrollToBottom();
        }

        /// <summary>
        /// Scrolls to a specific vertical position in the form
        /// </summary>
        /// <param name="offset">The vertical offset to scroll to</param>
        public void ScrollToVerticalOffset(double offset)
        {
            FormScrollViewer?.ScrollToVerticalOffset(offset);
        }

        /// <summary>
        /// Ensures the scroll view is properly positioned and content is visible
        /// </summary>
        private void EnsureContentVisible()
        {
            if (FormScrollViewer != null)
            {
                // Update layout to ensure proper measurement
                FormScrollViewer.UpdateLayout();
                
                // If content is larger than viewport, ensure scrollbar is available
                if (FormScrollViewer.ExtentHeight > FormScrollViewer.ViewportHeight)
                {
                    // Scroll to show the form is scrollable (slight scroll from top)
                    FormScrollViewer.ScrollToVerticalOffset(10);
                }
            }
        }

        private void UserControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Optional: Change cursor to Hand when hovering over the form (for better UX)
            if (e.LeftButton == MouseButtonState.Released && e.RightButton == MouseButtonState.Released)
            {
                Mouse.OverrideCursor = Cursors.Hand;
            }
        }

        private void UserControl_MouseLeave(object sender, MouseEventArgs e)
        {
            // Reset cursor when mouse leaves the user control
            Mouse.OverrideCursor = null;
        }

        /// <summary>
        /// Checks if any ComboBox in the form has its dropdown open
        /// </summary>
        private bool IsAnyComboBoxOpen()
        {
            return (txtSeats?.IsDropDownOpen == true) || 
                   (txtHours?.IsDropDownOpen == true) || 
                   (cmbIdType?.IsDropDownOpen == true);
        }

        /// <summary>
        /// Load booking types from local database and populate the dropdown
        /// </summary>
        private void LoadBookingTypesFromDatabase()
        {
            try
            {
                // Get booking types from database
                var bookingTypes = OfflineBookingStorage.GetBookingTypes();

                // Clear existing items except the placeholder
                txtSeats.Items.Clear();
                
                // Re-add placeholder
                var placeholderItem = new ComboBoxItem
                {
                    Content = "Select type ",
                    IsEnabled = false,
                    IsSelected = true,
                    Style = (Style)FindResource("WhiteComboBoxItemStyle")
                };
                placeholderItem.Name = "seatsPlaceholder";
                placeholderItem.SetValue(System.Windows.Controls.Primitives.Selector.IsSelectedProperty, true);
                txtSeats.Items.Add(placeholderItem);

                // Add booking types from database
                if (bookingTypes.Count > 0)
                {
                    foreach (var type in bookingTypes)
                    {
                        var item = new ComboBoxItem
                        {
                            Content = type.Type,
                            Tag = type.Amount, // Store amount in Tag for easy access
                            Style = (Style)FindResource("WhiteComboBoxItemStyle")
                        };
                        txtSeats.Items.Add(item);
                    }

                    Logger.Log($"Loaded {bookingTypes.Count} booking types from database");
                }
                else
                {
                    // No booking types found - just log it, don't show popup
                    Logger.Log("No booking types found in database settings. User needs to login to fetch settings.");
                }

                // Reset selection to placeholder
                txtSeats.SelectedIndex = 0;
                var seatsPlaceholder = GetSeatsPlaceholder();
                if (seatsPlaceholder != null) seatsPlaceholder.Visibility = Visibility.Visible;
                
                // Set initial foreground color to grey (placeholder color)
                SetForegroundColor(txtSeats, true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Error loading booking types",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handle discount text changed - recalculate total and advance
        /// </summary>
        private void txtDiscount_TextChanged(object sender, TextChangedEventArgs e)
        {
            RecalculatePricing();
        }

        /// <summary>
        /// Recalculate total amount after discount and advance payment
        /// </summary>
        private void RecalculatePricing()
        {
            try
            {
                // Get price per person (total price per person including hours)
                if (!decimal.TryParse(txtRate.Text, out decimal pricePerPerson) || pricePerPerson <= 0)
                {
                    // If no valid price, clear total and return
                    if (string.IsNullOrWhiteSpace(txtRate.Text))
                    {
                        txtTotalAmount.Text = "0";
                    }
                    return;
                }

                // Get number of persons
                if (!int.TryParse(txtPersons.Text, out int persons) || persons <= 0)
                {
                    // If no valid persons, clear total and return
                    if (string.IsNullOrWhiteSpace(txtPersons.Text))
                    {
                        txtTotalAmount.Text = "0";
                    }
                    return;
                }

                // Calculate base total (before discount)
                decimal baseTotal = pricePerPerson * persons;

                // Apply discount with validation
                decimal discount = 0;
                if (!string.IsNullOrWhiteSpace(txtDiscount.Text))
                {
                    if (decimal.TryParse(txtDiscount.Text, out decimal discountAmount))
                    {
                        // Only apply positive discount values
                        if (discountAmount > 0)
                        {
                            // Discount cannot exceed base total
                            if (discountAmount > baseTotal)
                            {
                                // Cap discount at base total
                                discount = baseTotal;
                                
                                // Temporarily remove event handler to prevent recursion
                                txtDiscount.TextChanged -= txtDiscount_TextChanged;
                                txtDiscount.Text = baseTotal.ToString("0.00");
                                txtDiscount.TextChanged += txtDiscount_TextChanged;
                                
                                MessageBox.Show($"Discount cannot exceed the total amount of ₹{baseTotal:0.00}.\n\nDiscount has been adjusted to ₹{baseTotal:0.00}.",
                                    "Discount Limited", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                discount = discountAmount;
                            }
                        }
                    }
                }

                // Calculate final total after applying discount
                decimal finalTotal = baseTotal - discount;
                
                // Update total amount display
                txtTotalAmount.Text = finalTotal.ToString("0.00");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Error calculating discount: {ex.Message}", "Calculation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void txtRate_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        /// <summary>
        /// Update overlay card dynamically from form changes
        /// </summary>
        private void UpdateOverlayFromForm()
        {
            try
            {
                if (PricingSummaryOverlay.Visibility != Visibility.Visible)
                    return; // Only update if overlay is visible

                // Update Number of Persons
                if (summaryPersons != null && int.TryParse(txtPersons.Text, out int persons))
                {
                    summaryPersons.Text = persons.ToString();
                }

                // Update Seat Type
                if (summarySeatType != null)
                {
                    var seatType = (txtSeats.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "-";
                    summarySeatType.Text = seatType;
                }

                // Update Per Person Price (for Sleeper, get the tier price)
                if (summaryPrice != null)
                {
                    string selectedSeatType = (txtSeats?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(selectedSeatType))
                    {
                        decimal pricePerPerson = 0;
                        
                        if (selectedSeatType.ToLower() == "sleeper" || selectedSeatType.ToLower() == "sleeping")
                        {
                            // For Sleeper, get price from the selected tier
                            if (summaryHoursCombo.SelectedIndex > 0)
                            {
                                var selectedItem = summaryHoursCombo.SelectedItem as ComboBoxItem;
                                if (selectedItem != null && selectedItem.Tag != null && decimal.TryParse(selectedItem.Tag.ToString(), out decimal tierPrice))
                                {
                                    pricePerPerson = tierPrice;
                                }
                                else
                                {
                                    pricePerPerson = OfflineBookingStorage.GetBookingTypeAmount(selectedSeatType);
                                }
                            }
                            else
                            {
                                pricePerPerson = OfflineBookingStorage.GetBookingTypeAmount(selectedSeatType);
                            }
                        }
                        else
                        {
                            // For Sitting: Standard price per hour
                            pricePerPerson = OfflineBookingStorage.GetBookingTypeAmount(selectedSeatType);
                        }
                        
                        summaryPrice.Text = pricePerPerson.ToString("F2");
                    }
                    else
                    {
                        summaryPrice.Text = "0";
                    }
                }

                // Update Total Hours ComboBox to match form selection
                if (summaryHoursCombo != null && txtHours.SelectedIndex > 0)
                {
                    summaryHoursCombo.SelectedIndex = txtHours.SelectedIndex;
                }

                // Update Start and End Times
                UpdateStartAndEndTimes();

                // Update Room Number visibility and discount
                UpdateRoomNumberVisibility();
                if (summaryDiscount != null && string.IsNullOrEmpty(summaryDiscount.Text))
                {
                    summaryDiscount.Text = "0";
                }

                // Recalculate total
                RecalculateSummaryTotal();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating overlay from form: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculate and update start and end times based on seat type and hours
        /// </summary>
        private void UpdateStartAndEndTimes()
        {
            try
            {
                if (summaryStartTime == null || summaryEndTime == null)
                    return;

                DateTime now = DateTime.Now;
                summaryStartTime.Text = now.ToString("HH:mm");

                // Get hours from the overlay combobox first (if visible), otherwise from form
                string hoursText = "";
                if (PricingSummaryOverlay.Visibility == Visibility.Visible && summaryHoursCombo.SelectedIndex > 0)
                {
                    hoursText = (summaryHoursCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                }
                else
                {
                    hoursText = (txtHours.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                }

                // Parse hours - handle both "1-3 hrs" and "1 hr" formats
                int hours = 0;
                if (hoursText.Contains("-"))
                {
                    // For range like "1-3 hrs", use the max value (3)
                    string[] parts = hoursText.Split('-');
                    if (parts.Length > 1 && int.TryParse(parts[1].Split(' ')[0], out int maxHours))
                    {
                        hours = maxHours;
                    }
                }
                else
                {
                    // For single value like "1 hr"
                    int.TryParse(hoursText.Split(' ')[0], out hours);
                }

                if (hours > 0)
                {
                    DateTime endTime = now.AddHours(hours);
                    summaryEndTime.Text = endTime.ToString("HH:mm");
                }
                else
                {
                    summaryEndTime.Text = "00:00";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating start/end times: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle overlay hours combo box selection change
        /// </summary>
        private void SummaryHours_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Prevent infinite loop by blocking recursive updates
                if (sender != summaryHoursCombo)
                    return;

                // Sync with form hours selection
                if (summaryHoursCombo.SelectedIndex > 0)
                {
                    txtHours.SelectedIndex = summaryHoursCombo.SelectedIndex;
                    summaryHoursPlaceholder.Visibility = Visibility.Collapsed;
                    SetForegroundColor(summaryHoursCombo, false);
                }
                else
                {
                    txtHours.SelectedIndex = 0;
                    summaryHoursPlaceholder.Visibility = Visibility.Visible;
                    SetForegroundColor(summaryHoursCombo, true);
                }

                // Update end time
                UpdateStartAndEndTimes();

                // Update per person price (for Sleeper, this gets the tier price)
                if (summaryPrice != null && summaryHoursCombo.SelectedIndex > 0)
                {
                    string selectedSeatType = (txtSeats?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                    if (selectedSeatType.ToLower() == "sleeper" || selectedSeatType.ToLower() == "sleeping")
                    {
                        var selectedItem = summaryHoursCombo.SelectedItem as ComboBoxItem;
                        if (selectedItem != null && selectedItem.Tag != null && decimal.TryParse(selectedItem.Tag.ToString(), out decimal tierPrice))
                        {
                            summaryPrice.Text = tierPrice.ToString("F2");
                        }
                    }
                }

                // Recalculate total
                RecalculateSummaryTotal();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in summary hours selection: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle dropdown opened for summary hours combobox
        /// </summary>
        private void SummaryHours_DropDownOpened(object sender, EventArgs e)
        {
            // Scroll to top when dropdown opens
            var comboBox = sender as ComboBox;
            if (comboBox != null && comboBox.Items.Count > 0)
            {
                // Use Dispatcher to ensure the dropdown is fully rendered
                comboBox.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Scroll to the first item (placeholder)
                    comboBox.Items.MoveCurrentToFirst();
                    var firstItem = comboBox.ItemContainerGenerator.ContainerFromIndex(0);
                    if (firstItem != null)
                    {
                        (firstItem as ComboBoxItem)?.BringIntoView();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// Sync overlay hours combobox from form selection
        /// </summary>
        private void SyncOverlayHoursFromForm()
        {
            try
            {
                if (summaryHoursCombo != null && PricingSummaryOverlay.Visibility == Visibility.Visible)
                {
                    summaryHoursCombo.SelectedIndex = txtHours.SelectedIndex;

                    if (txtHours.SelectedIndex > 0)
                    {
                        summaryHoursPlaceholder.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        summaryHoursPlaceholder.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error syncing overlay hours: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle overlay room number changes
        /// </summary>
        private void SummaryRoomNumber_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                RecalculateSummaryTotal();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in room number change: {ex.Message}");
            }
        }
    }
}
