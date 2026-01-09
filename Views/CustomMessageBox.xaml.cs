using System;
using System.Linq;
using System.Windows;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace UserModule
{
    public partial class CustomMessageBox : Window
    {
        private string bookingId;

        public CustomMessageBox(string details)
        {
            InitializeComponent();
            DetailsTextBlock.Text = details;
            bookingId = ExtractBookingId(details);
        }

        private string ExtractBookingId(string details)
        {
            foreach (var line in details.Split('\n'))
            {
                if (line.StartsWith("Booking ID:"))
                {
                    return line.Replace("Booking ID:", "").Trim();
                }
            }
            return string.Empty;
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // MessageBox.Show($"Fetched Booking ID: {bookingId}", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
             await SendBookingToNetwork(bookingId);
            this.DialogResult = true;
            this.Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async Task SendBookingToNetwork(string bookingId)
        {
            await OfflineBookingStorage.SyncSingleBookingAsync(bookingId);
        }
    }
}
