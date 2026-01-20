using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UserModule.Models;

namespace UserModule
{
    public partial class ScanBillingControl : UserControl
    {
        private Booking1? currentBooking;
        public event EventHandler? CloseRequested;
        public event EventHandler? BillingCompleted;

        public ScanBillingControl()
        {
            InitializeComponent();
            Loaded += ScanBillingControl_Loaded;
        }

        private void ScanBillingControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Auto-focus on Bill ID textbox for barcode scanning
            txtBillId.Focus();
            
            // Set current out time in HH:MM format (24-hour)
            txtOutTime.Text = DateTime.Now.ToString("HH:mm");
        }

        private void txtBillId_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ProcessBillId();
            }
        }

        private void txtBillId_TextChanged(object sender, TextChangedEventArgs e)
        {
            errBillId.Visibility = Visibility.Collapsed;
        }

        private void ProcessBillId()
        {
            string billId = txtBillId.Text.Trim();

            if (string.IsNullOrEmpty(billId))
            {
                errBillId.Text = "* Please scan or enter Bill ID";
                errBillId.Visibility = Visibility.Visible;
                return;
            }

            // Search for the booking in local database
            currentBooking = OfflineBookingStorage.GetBookingById(billId);

            if (currentBooking == null)
            {
                errBillId.Text = "* Bill ID not found in database";
                errBillId.Visibility = Visibility.Visible;
                txtBillId.SelectAll();
                return;
            }

            // Check if already completed
            if (currentBooking.status?.ToLower() == "completed")
            {
                errBillId.Text = "* This bill is already completed";
                errBillId.Visibility = Visibility.Visible;
                txtBillId.SelectAll();
                return;
            }

            // Load booking details
            LoadBookingDetails();
        }

        private void LoadBookingDetails()
        {
            if (currentBooking == null) return;

            // Show billing details section
            BillingDetailsSection.Visibility = Visibility.Visible;
            btnSubmit.IsEnabled = false;

            // Display customer info
            txtCustomerName.Text = currentBooking.guest_name ?? "N/A";
            txtCustomerPhone.Text = $"Phone: {currentBooking.phone_number ?? "N/A"}";
            txtSeatType.Text = currentBooking.booking_type ?? "N/A";
            
            // Calculate actual hours used (railway time - only full hours, ignore minutes)
            DateTime now = DateTime.Now;
            DateTime inDateTime = DateTime.Today.Add(currentBooking.in_time);
            TimeSpan actualDuration = now - inDateTime;
            int actualFullHours = (int)actualDuration.TotalHours; // Only count complete hours
            
            decimal totalAmount = currentBooking.total_amount;
            decimal paidAmount = currentBooking.paid_amount;
            decimal overtimeCharges = 0;
            
            // Check for overtime (only if full hours exceed booked hours)
            if (actualFullHours > currentBooking.total_hours)
            {
                int extraHours = actualFullHours - currentBooking.total_hours;
                overtimeCharges = extraHours * currentBooking.price_per_person * currentBooking.number_of_persons;
                
                // Show overtime details
                txtOvertimeDetails.Text = $"Overtime: {extraHours} hours × ₹{currentBooking.price_per_person} × {currentBooking.number_of_persons} persons = ₹{overtimeCharges:F2}";
                txtOvertimeDetails.Visibility = Visibility.Visible;
            }
            else
            {
                txtOvertimeDetails.Visibility = Visibility.Collapsed;
            }
            
            // Calculate final balance
            decimal balance = totalAmount - paidAmount + overtimeCharges;

            txtTotalAmount.Text = $"Total: ₹{totalAmount:F2}";
            txtBalanceAmount.Text = balance.ToString("F2");
            
            // Format in_time and current out_time as HH:MM (24-hour format)
            txtInTime.Text = currentBooking.in_time.ToString(@"HH\:mm");
            txtOutTime.Text = now.ToString("HH:mm");

            // Focus on payment method
            cmbPaymentMethod.Focus();
        }

        private void cmbPaymentMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            errPaymentMethod.Visibility = Visibility.Collapsed;
            
            // Enable submit button if a valid payment method is selected
            if (cmbPaymentMethod.SelectedIndex > 0)
            {
                btnSubmit.IsEnabled = true;
            }
            else
            {
                btnSubmit.IsEnabled = false;
            }
        }

        private async void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            // Validate payment method
            if (cmbPaymentMethod.SelectedIndex <= 0)
            {
                errPaymentMethod.Visibility = Visibility.Visible;
                return;
            }

            if (currentBooking == null || string.IsNullOrEmpty(currentBooking.booking_id)) return;

            try
            {
                // Get payment method
                string paymentMethod = ((ComboBoxItem)cmbPaymentMethod.SelectedItem).Content.ToString() ?? "Cash";

                // Calculate final balance with overtime (railway time - only full hours)
                DateTime now = DateTime.Now;
                DateTime inDateTime = DateTime.Today.Add(currentBooking.in_time);
                TimeSpan actualDuration = now - inDateTime;
                int actualFullHours = (int)actualDuration.TotalHours; // Only count complete hours
                
                decimal overtimeCharges = 0;
                if (actualFullHours > currentBooking.total_hours)
                {
                    int extraHours = actualFullHours - currentBooking.total_hours;
                    overtimeCharges = extraHours * currentBooking.price_per_person * currentBooking.number_of_persons;
                }
                
                decimal finalBalance = currentBooking.total_amount - currentBooking.paid_amount + overtimeCharges;
                
                // Update booking with out_time, overtime charges, and final balance
                string result = await OfflineBookingStorage.CompleteBookingWithOvertimeAsync(
                    currentBooking.booking_id,
                    now.TimeOfDay,
                    overtimeCharges,
                    finalBalance,
                    paymentMethod);
                
                // If successful and there's a balance amount, update worker balance
                if (result.Contains("✅") && finalBalance > 0)
                {
                    string? workerId = LocalStorage.GetItem("workerId");
                    string? adminId = LocalStorage.GetItem("adminId");

                    if (!string.IsNullOrEmpty(workerId) && !string.IsNullOrEmpty(adminId))
                    {
                        await OfflineBookingStorage.UpdateWorkerBalanceAsync(workerId, adminId, finalBalance);
                        Logger.Log($"Worker balance updated: ₹{finalBalance} for worker {workerId}");
                    }
                }
                
                // Show result message
                string details = $"Bill ID: {currentBooking.booking_id}\n" +
                                $"Customer: {currentBooking.guest_name}\n" +
                                (overtimeCharges > 0 ? $"Overtime Charges: ₹{overtimeCharges:F2}\n" : "") +
                                $"Final Balance: ₹{finalBalance:F2}\n" +
                                $"Payment Method: {paymentMethod}";
                
                MessageBox.Show(
                    $"{result}\n\n{details}",
                    "Billing Update",
                    MessageBoxButton.OK,
                    result.Contains("✅") ? MessageBoxImage.Information : MessageBoxImage.Warning);

                // If successful, raise events
                if (result.Contains("✅"))
                {
                    BillingCompleted?.Invoke(this, EventArgs.Empty);
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // MessageBox.Show(
                //     $"Error completing billing: {ex.Message}",
                //     "Error",
                //     MessageBoxButton.OK,
                //     MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Close without saving
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
