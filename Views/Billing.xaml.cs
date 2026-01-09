using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace UserModule
{
    public partial class Billing : UserControl
    {
        public event EventHandler? CloseRequested;
        public event Action<string, string, string, DateTime, int, int, double, double>? PrintRequested;



        // Make nullable or initialize with empty string
        public string CurrentSeatType { get; private set; } = string.Empty;

        public Billing()
        {
            InitializeComponent();


            // Handle Enter key globally
            this.PreviewKeyDown += Billing_PreviewKeyDown;

            // Attach Enter key to all focusable children
            AttachEnterHandler(this);

        }
        private void Billing_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                // If success popup is visible → OK button
                if (SuccessPopup.Visibility == Visibility.Visible)
                {
                    OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return;
                }

                // Otherwise → Print button
                PrintButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }

        private void AttachEnterHandler(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TextBox tb)
                    tb.PreviewKeyDown += Billing_PreviewKeyDown;
                else if (child is Button btn)
                    btn.PreviewKeyDown += Billing_PreviewKeyDown;
                else
                    AttachEnterHandler(child); // recursive
            }
        }
        public void SetBillData(
            string billId,
            string customerName,
            string seatType,
            string time,
            int totalHours,
            int persons,
            double rate,
            double paidAmount,
            string phoneNo)
        {
            BillIdText.Text = billId ?? string.Empty;
            CustomerNameText.Text = customerName ?? string.Empty;
            SeatTypeText.Text = seatType ?? string.Empty;
            TimeText.Text = time ?? string.Empty;
            TotalHoursText.Text = totalHours.ToString();
            PersonsText.Text = persons.ToString();
            RateText.Text = $"₹{rate}";
            
            // For Sleeper with pricing tiers, rate already includes the hour range cost
            // For Sitting, it's an hourly rate that needs to be multiplied by hours
            bool isSleeper = "Sleeper".Equals(seatType, StringComparison.OrdinalIgnoreCase) || 
                           "Sleeping".Equals(seatType, StringComparison.OrdinalIgnoreCase);
            
            double amount = isSleeper ? rate * persons : rate * persons * totalHours;
            
            AmountText.Text = $"₹{amount}";
            PaidAmountText.Text = $"₹{paidAmount}";
            BalanceText.Text = $"₹{amount - paidAmount}";
            PhoneTextBox.Text = phoneNo ?? string.Empty;

            CurrentSeatType = seatType ?? string.Empty;

            BillingBarcode.Source = GenerateBarcode(billId ?? string.Empty);
        }
        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            PrintRequested?.Invoke(
                BillIdText.Text,
                CustomerNameText.Text,
                PhoneTextBox.Text,
                DateTime.Now,
                int.TryParse(TotalHoursText.Text, out var th) ? th : 0,
                int.TryParse(PersonsText.Text, out var p) ? p : 0,
                double.TryParse(RateText.Text.Replace("₹", ""), out var r) ? r : 0,
                double.TryParse(PaidAmountText.Text.Replace("₹", ""), out var pa) ? pa : 0
            );

            BillingContent.Visibility = Visibility.Collapsed;
            SuccessPopup.Visibility = Visibility.Visible;
        }

        private void SuccessClose_Click(object sender, RoutedEventArgs e)
        {
            var parent = this.Parent as Panel;
            parent?.Children.Remove(this);

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        private void GenerateBill()
        {
            string billId = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
            BillIdText.Text = $"Bill ID: {billId}";

            BillingBarcode.Source = GenerateBarcode(billId);
        }

        private BitmapSource GenerateBarcode(string billId)
        {
            if (string.IsNullOrEmpty(billId)) billId = "00000000"; // fallback

            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Height = 50,
                    Width = 160,
                    Margin = 2
                }
            };

            var pixelData = writer.Write(billId);
            int stride = pixelData.Width * 4;

            return BitmapSource.Create(
                pixelData.Width, pixelData.Height,
                96, 96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                pixelData.Pixels,
                stride);
        }

        public void RaisePrintEvent(string billId, string customerName, string phoneNo, DateTime startTime,
                            int totalHours, int persons, double rate, double paidAmount)
        {
            PrintRequested?.Invoke(billId, customerName, phoneNo, startTime, totalHours, persons, rate, paidAmount);
        }

    }
}
