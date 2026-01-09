using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UserModule.Models;

namespace UserModule
{
    public partial class BillForm : UserControl
    {
        public BillForm()
        {
            InitializeComponent();
        }

        // 🔹 Triggered when barcode scanner sends Enter
        private async void txtBillID_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string billId = txtBillID.Text.Trim();

                if (!string.IsNullOrEmpty(billId))
                {
                    try
                    {
                        await OfflineBookingStorage.MarkBookingAsCompletedAsync(billId);

                        // ✅ Log success
                        Logger.Log($"Bill ID {billId} marked as completed successfully.");

                        // ✅ Show success message to user
                        MessageBox.Show($"Bill ID {billId} has been marked as completed successfully.",
                                        "Booking Completed", MessageBoxButton.OK, MessageBoxImage.Information);

                        // ✅ Automatically close the form after successful scan
                        CloseForm();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex);
                        // MessageBox.Show("Error completing booking. Please try again.",
                        //                 "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Please enter a valid Bill ID before pressing Enter.",
                                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void CloseForm()
        {
            var parentPanel = this.Parent as Panel;
            if (parentPanel != null)
            {
                parentPanel.Children.Remove(this);
                return;
            }

            var parentContent = this.Parent as ContentControl;
            if (parentContent != null)
            {
                parentContent.Content = null;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            CloseForm();
        }

    }
}

