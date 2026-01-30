using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UserModule.Models;
using UserModule.Components;

namespace UserModule
{
    public partial class SimpleScanControl : UserControl
    {
        public event EventHandler? CloseRequested;
        private Booking1? currentBooking;

        public SimpleScanControl()
        {
            try
            {
                InitializeComponent();
                
                // Safe focus setting
                Loaded += (s, e) => 
                {
                    try 
                    { 
                        txtTest?.Focus(); 
                    } 
                    catch (Exception ex) 
                    { 
                        Logger.LogError(ex); 
                    }
                };

                // Set default out time to current time
                txtOutTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                throw; // Re-throw to let caller handle
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Refresh the page - reset to initial state (scan section)
            ResetPage();
        }

        /// <summary>
        /// Resets the SimpleScanControl page to its initial state
        /// </summary>
        private void ResetPage()
        {
            try
            {
                // Clear the scan input field
                if (txtTest != null)
                {
                    txtTest.Clear();
                    txtTest.Focus();
                }

                // Hide payment section and show scan section
                if (ScanSection != null)
                    ScanSection.Visibility = Visibility.Visible;
                
                if (PaymentSection != null)
                    PaymentSection.Visibility = Visibility.Collapsed;

                // Reset payment form fields
                if (lblPaidAmount != null)
                    lblPaidAmount.Text = "₹0";

                if (txtBalanceAmount != null)
                    txtBalanceAmount.Text = "₹0";

                if (cmbPaymentMethod != null)
                    cmbPaymentMethod.SelectedIndex = 1; // Set to Cash (default)

                if (errPaymentMethod != null)
                    errPaymentMethod.Visibility = Visibility.Collapsed;

                if (btnCompletePayment != null)
                    btnCompletePayment.IsEnabled = false;

                // Reset current booking
                currentBooking = null;

                // Reset out time to current time
                if (txtOutTime != null)
                    txtOutTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                Logger.Log("SimpleScanControl page refreshed");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private void txtTest_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ProcessScan();
            }
        }

        private void ProcessScan()
        {
            string bookingId = txtTest.Text.Trim();
            
            if (string.IsNullOrEmpty(bookingId))
            {
                MessageBox.Show("Please enter or scan a booking ID", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Check if booking exists and get its status
                var booking = OfflineBookingStorage.GetBookingById(bookingId);
                
                if (booking == null)
                {
                    MessageBox.Show($"❌ Booking ID '{bookingId}' not found in database", "Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    txtTest.SelectAll();
                    return;
                }

                // Check current status
                if (booking.status?.ToLower() == "completed")
                {
                    MessageBox.Show($"⚠️ Booking ID '{bookingId}' is already completed", "Already Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtTest.SelectAll();
                    return;
                }

                // Store current booking and show payment section
                currentBooking = booking;
                ShowPaymentSection(booking);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Error processing booking: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private int CalculateActualHours(DateTime inDateTime, DateTime outDateTime)
        {
            // Calculate actual duration including date
            TimeSpan duration = outDateTime - inDateTime;
            int totalMinutes = (int)duration.TotalMinutes;
            
            // Convert to hours and round up, minimum 1 hour
            return Math.Max(1, (int)Math.Ceiling(totalMinutes / 60.0));
        }

        private void ShowPaymentSection(Booking1 booking)
        {
            try
            {
                // Hide scan section
                ScanSection.Visibility = Visibility.Collapsed;
                
                // Populate customer info
                lblCustomerName.Text = booking.guest_name ?? "N/A";
                lblCustomerPhone.Text = $"Phone: {booking.phone_number ?? "N/A"}";
                lblSeatType.Text = $"Booking ID: {booking.booking_id} | Type: {booking.booking_type}";
                lblInTimeDisplay.Text = $"In Time: {booking.in_time.ToString(@"hh\:mm\:ss")}";

                // Get current time as out_time
                DateTime currentTime = DateTime.Now;
                TimeSpan currentOutTime = currentTime.TimeOfDay;
                
                // Calculate actual time spent using full DateTime (handles next-day checkout)
                DateTime inDateTime = booking.booking_date.Date + booking.in_time;
                DateTime outDateTime = currentTime;
                TimeSpan actualDuration = outDateTime - inDateTime;
                int actualMinutes = (int)actualDuration.TotalMinutes;
                
                // Get grace time from settings
                var settings = OfflineBookingStorage.GetWorkerSettings();
                int graceMinutes = 25; // Default fallback
                
                // Check if this is Sleeper (pricing tier) or Sitting (hourly rate)
                bool isSleeper = booking.booking_type?.Equals("Sleeper", StringComparison.OrdinalIgnoreCase) == true || 
                               booking.booking_type?.Equals("Sleeping", StringComparison.OrdinalIgnoreCase) == true;
                
                if (settings != null)
                {
                    // Get grace time based on booking type
                    if (isSleeper)
                    {
                        // Get sleeper grace time from settings
                        graceMinutes = settings.GraceTimeType2;
                    }
                    else
                    {
                        // Get sitting grace time from settings
                        graceMinutes = settings.GraceTimeType1;
                    }
                    
                    Logger.Log($"Grace time applied: {graceMinutes} minutes for {booking.booking_type} (Sitting={settings.GraceTimeType1}, Sleeper={settings.GraceTimeType2})");
                }
                else
                {
                    Logger.Log($"No settings found, using default grace time: {graceMinutes} minutes");
                }
                
                // Calculate actual hours from in_time to current out_time using full DateTime
                // inDateTime and outDateTime already declared above (lines 181-182)
                int actualTotalHours = CalculateActualHours(inDateTime, outDateTime);
                
                // Get booked hours
                int bookedHours = booking.total_hours;
                
                Logger.Log($"Booking details: In={booking.in_time}, Out={currentOutTime}, Booked={bookedHours}hr, Actual={actualMinutes}min, Grace={graceMinutes}min");
                
                // Calculate booked time + grace period in minutes
                int bookedMinutes = bookedHours * 60;
                int allowedMinutes = bookedMinutes + graceMinutes;
                
                Logger.Log($"Calculation: BookedMin={bookedMinutes}, AllowedMin={allowedMinutes}, ActualMin={actualMinutes}");
                
                // Only charge extra if exceeded booked time + grace period
                if (actualMinutes > allowedMinutes)
                {
                    // Calculate chargeable overtime minutes (after grace period)
                    int overtimeMinutes = actualMinutes - allowedMinutes;
                    // Round up to next hour for charging
                    int chargeableExtraHours = (int)Math.Ceiling(overtimeMinutes / 60.0);
                    actualTotalHours = bookedHours + chargeableExtraHours;
                    
                    Logger.Log($"Grace period exceeded. Actual: {actualMinutes}min, Allowed: {allowedMinutes}min, Charging: {chargeableExtraHours} extra hours");
                }
                else
                {
                    // Within grace period - no extra charges
                    actualTotalHours = bookedHours;
                    Logger.Log($"Within grace period. Actual: {actualMinutes}min, Allowed: {allowedMinutes}min");
                }
                
                // Use the stored total_amount (which includes any discount applied during booking)
                decimal baseAmount = booking.total_amount;
                
                // Calculate extra charges if stayed longer
                decimal extraCharges = 0;
                decimal actualTotalAmount = baseAmount;
                
                if (actualTotalHours > bookedHours)
                {
                    int extraHours = actualTotalHours - bookedHours;
                    
                    if (isSleeper)
                    {
                        // For Sleeper: use pricing tiers
                        var pricingTiers = OfflineBookingStorage.GetHourlyPricingTiers();
                        
                        if (pricingTiers != null && pricingTiers.Count > 0)
                        {
                            // Find the tier that covers the total actual hours
                            var tier = pricingTiers.Find(t => actualTotalHours >= t.MinHours && actualTotalHours <= t.MaxHours);
                            
                            if (tier != null)
                            {
                                // Use the tier amount for total actual hours
                                actualTotalAmount = tier.Amount * booking.number_of_persons;
                                extraCharges = actualTotalAmount - baseAmount;
                            }
                            else
                            {
                                // If no tier found, use the highest tier and add extra based on the last tier's effective rate
                                var lastTier = pricingTiers[pricingTiers.Count - 1];
                                decimal effectiveHourlyRate = lastTier.Amount / lastTier.MaxHours;
                                actualTotalAmount = baseAmount + (effectiveHourlyRate * extraHours * booking.number_of_persons);
                                extraCharges = actualTotalAmount - baseAmount;
                            }
                        }
                        else
                        {
                            // Fallback: estimate hourly rate from booked amount
                            decimal estimatedHourlyRate = booking.price_per_person / bookedHours;
                            extraCharges = estimatedHourlyRate * booking.number_of_persons * extraHours;
                            actualTotalAmount = baseAmount + extraCharges;
                        }
                    }
                    else
                    {
                        // For Sitting: simple hourly calculation
                        extraCharges = booking.price_per_person * booking.number_of_persons * extraHours;
                        actualTotalAmount = baseAmount + extraCharges;
                    }
                }
                
                decimal paidAmount = booking.paid_amount;
                decimal balanceAmount = actualTotalAmount - paidAmount;

                // Display amounts
                lblOriginalAmount.Text = $"₹{baseAmount:F2}";
                lblExtraCharges.Text = $"₹{extraCharges:F2}";
                lblTotalAmount.Text = $"₹{actualTotalAmount:F2}";
                lblPaidAmount.Text = $"₹{paidAmount:F2}";
                txtBalanceAmount.Text = balanceAmount.ToString("F2");

                // Set current out time dynamically (this will update as time passes)
                txtOutTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // Set Cash as default payment method (index 1)
                cmbPaymentMethod.SelectedIndex = 1;
                
                // Explicitly enable the Complete Payment button since Cash is selected
                btnCompletePayment.IsEnabled = true;

                // Show payment section
                PaymentSection.Visibility = Visibility.Visible;
                
                // Set focus to Complete Payment button so user can press Enter
                Dispatcher.InvokeAsync(() => btnCompletePayment.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Error displaying payment section: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void cmbPaymentMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ValidatePaymentForm();
        }

        private void ValidatePaymentForm()
        {
            try
            {
                // Defensive null checks in case control not yet initialized
                if (cmbPaymentMethod == null || btnCompletePayment == null || errPaymentMethod == null)
                    return;

                // Check if a valid payment method is selected (not the placeholder at index 0)
                bool isValid = cmbPaymentMethod.SelectedIndex > 0 && cmbPaymentMethod.Items.Count > 0;

                // Enable/disable complete button
                btnCompletePayment.IsEnabled = isValid;
                
                // Show/hide error message
                if (cmbPaymentMethod.SelectedIndex == 0 && cmbPaymentMethod.IsFocused)
                {
                    errPaymentMethod.Visibility = Visibility.Visible;
                }
                else
                {
                    errPaymentMethod.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private async void btnCompletePayment_Click(object sender, RoutedEventArgs e)
        {


            try
            {
                // If no balance to pay (user left on time, already paid full amount)
                if (decimal.TryParse(txtBalanceAmount.Text, out decimal balanceAmount) && balanceAmount <= 0)
                {
                    if (currentBooking == null || string.IsNullOrEmpty(currentBooking.booking_id))
                        return;
                    
                    // Auto-complete booking without additional payment
                    DateTime checkoutDateTime = DateTime.Now;
                    TimeSpan checkoutTime = checkoutDateTime.TimeOfDay;
                    
                    decimal checkoutTotalAmount = decimal.Parse(lblTotalAmount.Text.Replace("₹", ""));
                    decimal checkoutPaidAmount = decimal.Parse(lblPaidAmount.Text.Replace("₹", ""));
                    
                    var checkoutResult = await OfflineBookingStorage.CompleteBookingWithPaymentAsync(
                        currentBooking.booking_id,
                        0, // No balance payment
                        checkoutTotalAmount,
                        0, // No extra charges
                        currentBooking.payment_method ?? "Cash", // Use original payment method
                        checkoutTime
                    );

                    // Sync worker summaries to API (no balance to update since 0)
                    string closingWorkerId = LocalStorage.GetItem("workerId") ?? "";
                    string adminId = LocalStorage.GetItem("adminId") ?? "";
                    
                    if (!string.IsNullOrEmpty(closingWorkerId) && !string.IsNullOrEmpty(adminId))
                    {
                        await OfflineBookingStorage.SyncWorkerSummariesAsync();
                        Logger.Log($"Worker summaries synced after completing booking with no balance");
                    }

                    BookingConfirmationDialog.Show(
                        "Booking closed successfully!",
                        $"Booking ID: {currentBooking.booking_id}\n" +
                        $"Customer: {currentBooking.guest_name}\n" +
                        $"Total Amount: ₹{checkoutTotalAmount:F2}\n" +
                        $"Already Paid - No Balance\n" +
                        $"Out Time: {checkoutDateTime:yyyy-MM-dd HH:mm:ss}");

                    Logger.Log($"Booking {currentBooking.booking_id} closed - No extra charges");

                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }
                
                if (currentBooking == null)
                {
                    MessageBox.Show("No booking selected!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrEmpty(currentBooking.booking_id))
                {
                    MessageBox.Show("Invalid booking ID!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Validate inputs
                if (cmbPaymentMethod == null || cmbPaymentMethod.SelectedIndex <= 0)
                {
                    MessageBox.Show("Please select a payment method!", "Validation Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Calculate out time at the exact moment of payment completion
                DateTime outDateTime = DateTime.Now;
                TimeSpan outTime = outDateTime.TimeOfDay;
                
                // Update the display to show the exact out time being used
                txtOutTime.Text = outDateTime.ToString("yyyy-MM-dd HH:mm:ss");

                string paymentMethod = ((ComboBoxItem)cmbPaymentMethod.SelectedItem).Content.ToString() ?? "Cash";

                // Calculate amounts
                decimal extraCharges = decimal.Parse(lblExtraCharges.Text.Replace("₹", ""));
                decimal totalAmount = decimal.Parse(lblTotalAmount.Text.Replace("₹", ""));
                decimal alreadyPaid = decimal.Parse(lblPaidAmount.Text.Replace("₹", ""));
                decimal balancePayment = totalAmount - alreadyPaid;

                // Disable button to prevent double clicks
                btnCompletePayment.IsEnabled = false;

                // Complete payment
                var result = await OfflineBookingStorage.CompleteBookingWithPaymentAsync(
                    currentBooking.booking_id,
                    balancePayment,
                    totalAmount,
                    extraCharges,
                    paymentMethod,
                    outTime
                );

                bool success = result.Contains("✅");

                // Update worker balance in API for the closing worker (balance collected)
                if (success && balancePayment > 0)
                {
                    string closingWorkerId = LocalStorage.GetItem("workerId") ?? "";
                    string adminId = LocalStorage.GetItem("adminId") ?? "";
                    
                    if (!string.IsNullOrEmpty(closingWorkerId) && !string.IsNullOrEmpty(adminId))
                    {
                        await OfflineBookingStorage.UpdateWorkerBalanceAsync(closingWorkerId, adminId, balancePayment);
                        Logger.Log($"Worker balance API updated for {closingWorkerId}: +₹{balancePayment}");
                    }
                }

                if (success)
                {
                    BookingConfirmationDialog.Show(
                        "Payment completed successfully!",
                        $"Booking ID: {currentBooking.booking_id}\n" +
                        $"Customer: {currentBooking.guest_name}\n" +
                        $"Total Amount: ₹{totalAmount:F2}\n" +
                        $"Paid Amount: ₹{totalAmount:F2}\n" +
                        $"Payment Method: {paymentMethod}\n" +
                        $"Out Time: {outDateTime:yyyy-MM-dd HH:mm:ss}");

                    Logger.Log($"Payment completed for booking {currentBooking.booking_id} - Balance Paid: {balancePayment}, Total: {totalAmount}, Method: {paymentMethod}");

                    // Update booking with final amounts for receipt
                    currentBooking.paid_amount = totalAmount;
                    currentBooking.total_amount = totalAmount;
                    currentBooking.balance_amount = 0;
                    currentBooking.out_time = outTime; // Set the out_time for receipt
                    currentBooking.payment_method = paymentMethod;

                    // No automatic printing after closing - user can manually print if needed
                    // Printing was removed to avoid duplicate receipts

                    // Close the control
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    MessageBox.Show($"Failed to complete payment!\n\n{result}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    btnCompletePayment.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Error completing payment: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                btnCompletePayment.IsEnabled = true;
            }
        }
    }
}