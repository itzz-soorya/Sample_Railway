using System.Windows;

namespace UserModule.Components
{
    public partial class BookingConfirmationDialog : Window
    {
        public BookingConfirmationDialog(string mainMessage, string details)
        {
            InitializeComponent();
            
            txtMainMessage.Text = mainMessage;
            txtDetails.Text = details;
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        public static void Show(string mainMessage, string details)
        {
            var dialog = new BookingConfirmationDialog(mainMessage, details);
            dialog.ShowDialog();
        }
    }
}
