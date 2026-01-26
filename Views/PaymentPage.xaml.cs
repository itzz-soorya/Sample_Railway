using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using UserModule.Models;

namespace UserModule
{
    public partial class PaymentPage : UserControl
    {
        private readonly Submit _submitForm;

        public PaymentPage(Submit submitForm)
        {
            InitializeComponent();
            _submitForm = submitForm;


            // Populate fields from Submit
            CustomerNameText.Text = _submitForm.txtGuestName.Text;
            BillIdText.Text = _submitForm.txtBookingId.Text;
            PersonsText.Text = _submitForm.txtNumberOfPersons.Text;
            TotalHoursText.Text = _submitForm.txtTotalHours.Text + " Hrs";
            OutTimeText.Text = _submitForm.txtOutTime.Text;

            decimal.TryParse(_submitForm.txtPricePerPerson.Text, out decimal rate);
            decimal.TryParse(_submitForm.txtAdvanceAmount.Text, out decimal paid);
            int.TryParse(_submitForm.txtNumberOfPersons.Text, out int persons);

            decimal total = rate * persons;
            decimal balance = total - paid;

            RateText.Text = $"₹{rate:F2}";
            PaidAmountText.Text = $"₹{paid:F2}";
            BalanceText.Text = $"₹{balance:F2}";
            TotalPaymentText.Text = $"₹{total:F2}";
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 1️⃣ Update Submit form fields
            _submitForm.txtStatus.Text = "Completed";
            _submitForm.txtOutTime.Text = DateTime.Now.ToShortTimeString();

            // 2️⃣ Get booking from offline storage to get actual balance
            string bookingId = _submitForm.txtBookingId.Text;
            Logger.Log($"=== CLOSING BOOKING: {bookingId} ===");
            
            var booking1 = OfflineBookingStorage.GetBookingById(bookingId);
            decimal actualBalanceAmount = 0;
            
            if (booking1 != null)
            {
                actualBalanceAmount = booking1.balance_amount;
                Logger.Log($"Booking found in DB - Balance: ₹{actualBalanceAmount}, Worker: {booking1.worker_id}");
            }
            else
            {
                Logger.Log($"ERROR: Booking {bookingId} not found in database!");
            }

            // Also update old Booking object for compatibility
            var booking = Booking.GetBookingById(bookingId);
            if (booking != null)
            {
                booking.Status = "Completed";
                booking.EndTime = DateTime.Now;
            }

            // 2.5️⃣ Handle extra charges (balance amount) for worker
            if (actualBalanceAmount > 0)
            {
                // Get current worker and admin IDs
                string? workerId = LocalStorage.GetItem("workerId");
                string? adminId = LocalStorage.GetItem("adminId");

                Logger.Log($"Current Worker ID: {workerId}, Admin ID: {adminId}, Balance to add: ₹{actualBalanceAmount}");

                if (!string.IsNullOrEmpty(workerId) && !string.IsNullOrEmpty(adminId))
                {
                    Logger.Log($"Calling UpdateWorkerBalanceAsync...");
                    
                    // Update worker balance (online/offline)
                    bool updated = await OfflineBookingStorage.UpdateWorkerBalanceAsync(workerId, adminId, actualBalanceAmount);
                    
                    if (!updated)
                    {
                        Logger.Log($"✓ Balance amount ₹{actualBalanceAmount} saved OFFLINE for worker {workerId}");
                    }
                    else
                    {
                        Logger.Log($"✓ Balance amount ₹{actualBalanceAmount} updated ONLINE for worker {workerId}");
                    }
                }
                else
                {
                    Logger.Log($"ERROR: Worker ID or Admin ID is empty - cannot save balance");
                }
            }
            else
            {
                Logger.Log($"No balance amount to save (balance is {actualBalanceAmount})");
            }

            Logger.Log($"=== END CLOSING BOOKING ===\n");

            // 3️⃣ Remove blur and PaymentPage overlay
            _submitForm.SubmitGrid.Effect = null;
            _submitForm.SubmitGrid.Children.Remove(this);

            // 4️⃣ Refresh Dashboard
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null && mainWindow.MainContent.Content is Header header)
            {
                if (header.MainContentHost.Content is Dashboard dashboard)
                {
                    dashboard.RefreshBookings();
                    dashboard.UpdateCountsFromBookings();
                }

                // Optionally reload Dashboard
                header.LoadContent(new Dashboard());
            }

            //MessageBox.Show("Payment Completed! Status updated and Dashboard refreshed.");
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Remove blur and close PaymentPage overlay
            _submitForm.SubmitGrid.Effect = null;
            _submitForm.SubmitGrid.Children.Remove(this);
        }
    }
}
