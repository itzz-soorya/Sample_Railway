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

        private int CalculateActualHours(TimeSpan inTime, TimeSpan outTime)
        {
            // Convert to minutes
            int inMinutes = (int)(inTime.TotalMinutes);
            int outMinutes = (int)(outTime.TotalMinutes);

            // Calculate difference
            int diffMinutes = outMinutes - inMinutes;

            // Handle next-day checkout
            if (diffMinutes < 0)
            {
                diffMinutes += 24 * 60; // Add 24 hours
            }

            // Convert to hours and round up, minimum 1 hour
            return Math.Max(1, (int)Math.Ceiling(diffMinutes / 60.0));
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
                
                // Calculate actual hours from in_time to current out_time
                int actualTotalHours = CalculateActualHours(booking.in_time, currentOutTime);
                
                // Get booked hours
                int bookedHours = booking.total_hours;
                
                // Check if this is Sleeper (pricing tier) or Sitting (hourly rate)
                bool isSleeper = booking.booking_type?.Equals("Sleeper", StringComparison.OrdinalIgnoreCase) == true || 
                               booking.booking_type?.Equals("Sleeping", StringComparison.OrdinalIgnoreCase) == true;
                
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
                        checkoutPaidAmount,
                        checkoutTotalAmount,
                        0, // No extra charges
                        currentBooking.payment_method ?? "Cash", // Use original payment method
                        checkoutTime
                    );

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
                decimal paidAmount = decimal.Parse(lblPaidAmount.Text.Replace("₹", ""));

                // Disable button to prevent double clicks
                btnCompletePayment.IsEnabled = false;

                // Complete payment
                var result = await OfflineBookingStorage.CompleteBookingWithPaymentAsync(
                    currentBooking.booking_id,
                    paidAmount,
                    totalAmount,
                    extraCharges,
                    paymentMethod,
                    outTime
                );

                bool success = result.Contains("✅");

                // If successful and there's a balance amount, update worker balance
                if (success)
                {
                    decimal workerBalance = totalAmount - paidAmount;
                    if (workerBalance > 0)
                    {
                        string? workerId = LocalStorage.GetItem("workerId");
                        string? adminId = LocalStorage.GetItem("adminId");

                        if (!string.IsNullOrEmpty(workerId) && !string.IsNullOrEmpty(adminId))
                        {
                            await OfflineBookingStorage.UpdateWorkerBalanceAsync(workerId, adminId, workerBalance);
                            Logger.Log($"Worker balance updated: ₹{workerBalance} for worker {workerId}");
                        }
                    }
                }

                if (success)
                {
                    BookingConfirmationDialog.Show(
                        "Payment completed successfully!",
                        $"Booking ID: {currentBooking.booking_id}\n" +
                        $"Customer: {currentBooking.guest_name}\n" +
                        $"Total Amount: ₹{totalAmount:F2}\n" +
                        $"Paid Amount: ₹{paidAmount:F2}\n" +
                        $"Payment Method: {paymentMethod}\n" +
                        $"Out Time: {outDateTime:yyyy-MM-dd HH:mm:ss}");

                    Logger.Log($"Payment completed for booking {currentBooking.booking_id} - Amount: {paidAmount}, Method: {paymentMethod}");

                    // Update booking with final amounts for receipt
                    currentBooking.paid_amount = paidAmount;
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
                    // MessageBox.Show($"Failed to complete payment!\n\n{result}", "Error", 
                    //     MessageBoxButton.OK, MessageBoxImage.Error);
                    btnCompletePayment.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // MessageBox.Show($"Error completing payment: {ex.Message}", "Error", 
                //     MessageBoxButton.OK, MessageBoxImage.Error);
                btnCompletePayment.IsEnabled = true;
            }
        }
    }
}