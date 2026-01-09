using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace UserModule
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Set icon in code to avoid pack URI issues in single-file deployment
            try
            {
                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assests", "app.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    this.Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                }
            }
            catch { /* Icon is optional */ }

            // Load Login first
            var loginControl = new Login();
            loginControl.LoginSuccess += OnLoginSuccess;
            MainContent.Content = loginControl;

        }

        // ✅ Use this to load UserControls inside MainContent
        public void LoadContent(UserControl content)
        {
            MainContent.Content = content;
        }

        // Called when login succeeds
        private void OnLoginSuccess(string username)
        {
            // Replace MainContent with Header after login
            var header = new Header();
            MainContent.Content = header;

            // Set the username in Header and load dashboard
            header.SetLoggedInUser(username);
            header.MainContentHost.Content = new Dashboard();
        }
    }
}
