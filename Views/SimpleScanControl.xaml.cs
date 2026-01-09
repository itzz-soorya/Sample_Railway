using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UserModule.Models;

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
            CloseRequested?.Invoke(this, EventArgs.Empty);
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
                // MessageBox.Show($"Error processing booking: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (diffMinutes <= 0)
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
                
                // Calculate base amount based on booking type
                decimal baseAmount = isSleeper 
                    ? booking.price_per_person * booking.number_of_persons 
                    : booking.price_per_person * booking.number_of_persons * bookedHours;
                
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
                txtPaidAmount.Text = paidAmount.ToString("F2");
                txtBalanceAmount.Text = balanceAmount.ToString("F2");

                // Set current out time dynamically
                txtOutTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // Show payment section
                PaymentSection.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // MessageBox.Show($"Error displaying payment section: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void cmbPaymentMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ValidatePaymentForm();
        }

        private void txtPaidAmount_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // Get total amount from label
                if (lblTotalAmount != null && txtPaidAmount != null && txtBalanceAmount != null)
                {
                    string totalText = lblTotalAmount.Text.Replace("₹", "").Trim();
                    string paidText = txtPaidAmount.Text.Trim();
                    
                    if (decimal.TryParse(totalText, out decimal totalAmount) && 
                        decimal.TryParse(paidText, out decimal paidAmount))
                    {
                        decimal balance = totalAmount - paidAmount;
                        txtBalanceAmount.Text = balance.ToString("F2");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private void ValidatePaymentForm()
        {
            try
            {
                // Defensive null checks in case control not yet initialized
                if (cmbPaymentMethod == null || btnCompletePayment == null || errPaymentMethod == null)
                    return;

                // Only check if payment method is selected
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
                if (decimal.TryParse(txtBalanceAmount.Text, out decimal balanceAmount) && balanceAmount <= 0)
                {
                    MessageBox.Show("No balance to pay! Booking already settled.", "Info",
                        MessageBoxButton.OK, MessageBoxImage.Information);
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

                // Get out time from the read-only textbox (already set to current time)
                string outTimeStr = txtOutTime.Text.Trim();
                DateTime outDateTime;
                if (string.IsNullOrEmpty(outTimeStr))
                {
                    outDateTime = DateTime.Now;
                }
                else
                {
                    if (!DateTime.TryParse(outTimeStr, out outDateTime))
                    {
                        // MessageBox.Show("Invalid out time format. Using current time.", "Warning", 
                        //     MessageBoxButton.OK, MessageBoxImage.Warning);
                        outDateTime = DateTime.Now;
                    }
                }
                
                // Convert to TimeSpan (time of day only)
                TimeSpan outTime = outDateTime.TimeOfDay;

                string paymentMethod = ((ComboBoxItem)cmbPaymentMethod.SelectedItem).Content.ToString() ?? "Cash";

                // Calculate amounts
                decimal extraCharges = decimal.Parse(lblExtraCharges.Text.Replace("₹", ""));
                decimal totalAmount = decimal.Parse(lblTotalAmount.Text.Replace("₹", ""));
                decimal paidAmountFromText = decimal.Parse(txtPaidAmount.Text);
                decimal paidAmount = paidAmountFromText; // Use the editable paid amount

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

                if (success)
                {
                    MessageBox.Show($"✅ Payment completed successfully!\n\n" +
                        $"📋 Booking ID: {currentBooking.booking_id}\n" +
                        $"👤 Customer: {currentBooking.guest_name}\n" +
                        $"💰 Total Amount: ₹{totalAmount:F2}\n" +
                        $"💵 Paid Amount: ₹{paidAmount:F2}\n" +
                        $"💳 Payment Method: {paymentMethod}\n" +
                        $"⏰ Out Time: {outDateTime:yyyy-MM-dd HH:mm:ss}", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    Logger.Log($"Payment completed for booking {currentBooking.booking_id} - Amount: {paidAmount}, Method: {paymentMethod}");

                    // Update booking with final amounts for receipt
                    currentBooking.paid_amount = paidAmount;
                    currentBooking.total_amount = totalAmount;
                    currentBooking.balance_amount = 0;

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