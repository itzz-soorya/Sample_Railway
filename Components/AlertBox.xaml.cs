using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace UserModule.Components
{
    public partial class AlertBox : UserControl
    {
        public enum AlertType
        {
            Success,
            Error,
            Warning,
            Info
        }

        public AlertBox()
        {
            InitializeComponent();
        }

        public async void ShowAlert(AlertType type, string message)
        {
            // Hide all first
            InfoAlert.Visibility = Visibility.Collapsed;
            WarningAlert.Visibility = Visibility.Collapsed;
            ErrorAlert.Visibility = Visibility.Collapsed;
            SuccessAlert.Visibility = Visibility.Collapsed;

            switch (type)
            {
                case AlertType.Success:
                    SuccessAlert.Visibility = Visibility.Visible;
                    SuccessText.Text = message;
                    break;

                case AlertType.Error:
                    ErrorAlert.Visibility = Visibility.Visible;
                    ErrorText.Text = message;
                    break;

                case AlertType.Warning:
                    WarningAlert.Visibility = Visibility.Visible;
                    WarningText.Text = message;
                    break;

                case AlertType.Info:
                    InfoAlert.Visibility = Visibility.Visible;
                    InfoText.Text = message;
                    break;
            }

            // Auto-hide after 3 seconds
            await Task.Delay(3000);
            InfoAlert.Visibility = Visibility.Collapsed;
            WarningAlert.Visibility = Visibility.Collapsed;
            ErrorAlert.Visibility = Visibility.Collapsed;
            SuccessAlert.Visibility = Visibility.Collapsed;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            InfoAlert.Visibility = Visibility.Collapsed;
            WarningAlert.Visibility = Visibility.Collapsed;
            ErrorAlert.Visibility = Visibility.Collapsed;
            SuccessAlert.Visibility = Visibility.Collapsed;
        }
    }
}
