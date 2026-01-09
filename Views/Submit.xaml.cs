using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using UserModule.Models;

namespace UserModule
{
    /// <summary>
    /// Interaction logic for Submit.xaml
    /// </summary>
    public partial class Submit : UserControl
    {
        private const int SittingPrice = 50;
        private const int SleeperPrice = 100;

        public Submit()
        {
            InitializeComponent();
            this.Unloaded += Submit_Unloaded;


            txtStatus.Text = "Completed";
            txtOutTime.Text = DateTime.Now.ToShortTimeString();



        }

        private void Submit_Unloaded(object sender, RoutedEventArgs e)
        {
            var header = FindParentHeader();
            if (header != null)
            {
                header.SetSubmitButtonVisibility(true);
            }
        }

        private Header? FindParentHeader()
        {
            DependencyObject parent = this;
            while (parent != null)
            {
                parent = VisualTreeHelper.GetParent(parent);
                if (parent is Header header)
                    return header;
            }
            return null;
        }

        public void PopulateFields(Booking booking)
        {
            if (booking == null) return;

            txtBookingId.Text = booking.BookingId;
            txtGuestName.Text = booking.Name;
            txtPhoneNumber.Text = booking.PhoneNo;
            txtSeatType.Text = booking.SeatType;
            txtNumberOfPersons.Text = booking.NumberOfPersons.ToString();
            txtTotalHours.Text = booking.TotalHours.ToString();

            int pricePerPerson = booking.SeatType?.ToLower() == "sleeper" ? SleeperPrice : SittingPrice;
            txtPricePerPerson.Text = pricePerPerson.ToString();
            txtAdvanceAmount.Text = booking.PaidAmount.ToString("0.00");

            // For Sleeper with pricing tiers, pricePerPerson already includes the hour range cost
            // For Sitting, it's an hourly rate that needs to be multiplied by hours
            bool isSleeper = booking.SeatType?.Equals("Sleeper", StringComparison.OrdinalIgnoreCase) == true || 
                           booking.SeatType?.Equals("Sleeping", StringComparison.OrdinalIgnoreCase) == true;
            
            double totalAmount = isSleeper 
                ? pricePerPerson * booking.NumberOfPersons 
                : pricePerPerson * booking.NumberOfPersons * booking.TotalHours;
            
            double balanceAmount = totalAmount - booking.PaidAmount;
            txtBalanceAmount.Text = $"₹{balanceAmount:0.00}";

            txtBookingDate.Text = booking.StartTime?.ToShortDateString() ?? "";
            txtInTime.Text = booking.StartTime?.ToShortTimeString() ?? "";
            txtOutTime.Text = booking.EndTime?.ToShortTimeString() ?? DateTime.Now.ToShortTimeString();

            txtIdType.Text = booking.IdType ?? "";
            txtIdNumber.Text = booking.IdNumber ?? "";

        }
        private void PaymentButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtBookingId.Text))
            {
                MessageBox.Show("Please enter a Bill ID.");
                return;
            }


            // Create PaymentPage overlay
            var paymentPage = new PaymentPage(this);
            paymentPage.HorizontalAlignment = HorizontalAlignment.Stretch;
            paymentPage.VerticalAlignment = VerticalAlignment.Stretch;

            // Add PaymentPage on top of SubmitGrid
            SubmitGrid.Children.Add(paymentPage);
        }


    }
}

