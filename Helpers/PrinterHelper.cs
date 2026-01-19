﻿//printer helper
using System;
using System.Drawing.Printing;
using System.IO;
using System.Management;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;


namespace UserModule
{
    // Printer configuration profile for different thermal printer models
    public class PrinterProfile
    {
        public string PrinterType { get; set; } = "Generic";
        public double ReceiptWidth { get; set; } = 304; // pixels at 96 DPI
        public double PageWidth { get; set; } = 304;
        public double PageHeight { get; set; } = 842;
        public int DPI { get; set; } = 203;
        public double BarcodeWidth { get; set; } = 250;
        public double BarcodeHeight { get; set; } = 45;
        public double LeftMargin { get; set; } = 5;
        public double RightMargin { get; set; } = 5;
    }

    public static class PrinterHelper
    {
        // Detect printer type and return appropriate profile
        private static PrinterProfile GetPrinterProfile(string printerName)
        {
            string printerLower = printerName.ToLower();

            // Epson TM-T82 / TM-T88 series (80mm paper)
            if (printerLower.Contains("epson") && (printerLower.Contains("tm-t82") || printerLower.Contains("tm-t88")))
            {
                return new PrinterProfile
                {
                    PrinterType = "Epson TM-T82/T88",
                    ReceiptWidth = 304,
                    PageWidth = 304,
                    PageHeight = 842,
                    DPI = 203,
                    BarcodeWidth = 250,
                    BarcodeHeight = 45,
                    LeftMargin = 22,
                    RightMargin = 8
                };
            }
            // TVS Printer series (80mm paper)
            else if (printerLower.Contains("tvs"))
            {
                return new PrinterProfile
                {
                    PrinterType = "TVS",
                    ReceiptWidth = 304,
                    PageWidth = 304,
                    PageHeight = 842,
                    DPI = 203,
                    BarcodeWidth = 250,
                    BarcodeHeight = 45,
                    LeftMargin = 5,
                    RightMargin = 5
                };
            }
            // Star Micronics (80mm paper)
            else if (printerLower.Contains("star"))
            {
                return new PrinterProfile
                {
                    PrinterType = "Star Micronics",
                    ReceiptWidth = 304,
                    PageWidth = 304,
                    PageHeight = 842,
                    DPI = 203,
                    BarcodeWidth = 250,
                    BarcodeHeight = 45,
                    LeftMargin = 6,
                    RightMargin = 6
                };
            }
            // Generic 80mm thermal printer (default)
            else
            {
                return new PrinterProfile
                {
                    PrinterType = "Generic 80mm",
                    ReceiptWidth = 304,
                    PageWidth = 304,
                    PageHeight = 842,
                    DPI = 203,
                    BarcodeWidth = 250,
                    BarcodeHeight = 45,
                    LeftMargin = 5,
                    RightMargin = 5
                };
            }
        }

        // Get current printer profile
        public static PrinterProfile GetCurrentPrinterProfile()
        {
            try
            {
                var settings = new PrinterSettings();
                string printerName = settings.PrinterName;
                return GetPrinterProfile(printerName ?? "Generic");
            }
            catch
            {
                return new PrinterProfile(); // Return default
            }
        }
        public static bool TryPrint(UIElement visualToPrint)
        {
            try
            {
                if (PrinterSettings.InstalledPrinters.Count == 0)
                {
                    MessageBox.Show("No printers are installed.", "Printer Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var settings = new PrinterSettings();
                string printerName = settings.PrinterName;

                if (string.IsNullOrWhiteSpace(printerName))
                {
                    MessageBox.Show("No default printer found.", "Printer Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (!IsPrinterOnline(printerName))
                {
                    MessageBox.Show($"Printer '{printerName}' appears to be offline or disconnected.",
                        "Printer Offline", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                PrintVisualToPrinter(visualToPrint, printerName);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Printing failed", "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private static bool IsPrinterOnline(string printerName)
        {
            try
            {
                string query = $"SELECT * FROM Win32_Printer WHERE Name = '{printerName.Replace("\\", "\\\\")}'";
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject printer in searcher.Get())
                    {
                        bool workOffline = Convert.ToBoolean(printer["WorkOffline"] ?? false);
                        int status = Convert.ToInt32(printer["PrinterStatus"] ?? 0);

                        // Status codes: 2 = Idle, 3 = Printing, 4 = Warmup, 5 = Stopped Printing
                        if (workOffline || (status < 2 || status > 4))
                            return false;
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static void PrintVisualToPrinter(UIElement visual, string printerName)
        {
            // Get printer-specific profile
            var printerProfile = GetPrinterProfile(printerName);

            PrintDialog printDialog = new PrintDialog
            {
                PrintQueue = new PrintQueue(new PrintServer(), printerName)
            };

            // Set page size dynamically based on printer profile
            printDialog.PrintTicket.PageMediaSize = new PageMediaSize(
                PageMediaSizeName.ISOA8, 
                printerProfile.PageWidth, 
                printerProfile.PageHeight
            );
            printDialog.PrintTicket.PageOrientation = PageOrientation.Portrait;

            var areaWidth = printDialog.PrintableAreaWidth;
            var areaHeight = printDialog.PrintableAreaHeight;

            // Measure with infinite size to get natural size, then arrange
            visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            visual.Arrange(new Rect(new Point(0, 0), visual.DesiredSize));
            visual.UpdateLayout();

            try
            {
                printDialog.PrintVisual(visual, "Billing Receipt");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Failed to print", "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void PrintBill(string billId, string customerName, string seatType, int totalHours,
int persons, decimal rate, decimal totalAmount, decimal paidAmount, decimal balance)
        {
            const int dpi = 300; // High-quality print resolution

            // Generate barcode using ZXing
            var barcodeWriter = new ZXing.BarcodeWriterPixelData
            {
                Format = ZXing.BarcodeFormat.CODE_128,
                Options = new ZXing.Common.EncodingOptions
                {
                    Height = 80,   // slightly taller for print
                    Width = 600,   // wider for 300 dpi clarity
                    Margin = 2
                }
            };

            var pixelData = barcodeWriter.Write(billId);

            // Convert to WPF BitmapSource with 300 DPI
            var barcodeSource = BitmapSource.Create(
                pixelData.Width,
                pixelData.Height,
                dpi, dpi, // <-- increased DPI
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                pixelData.Pixels,
                pixelData.Width * 4);

            // Create Image control
            var image = new System.Windows.Controls.Image
            {
                Source = barcodeSource,
                Width = 250,
                Height = 60,
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Bill layout
            // Reduce panel outer margin to minimize gap above the receipt
            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(25, 0,0, 6),
                Orientation = Orientation.Vertical
            };

            // Top separator line above the receipt (smaller bottom margin)
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = Brushes.Black,
                Margin = new Thickness(0, 0, 0, 2)
            });

            // Use hall name from local storage if available, otherwise fallback to a default title
            string hallName = LocalStorage.GetItem("hallName") ?? "Billing Receipt";
            panel.Children.Add(new TextBlock
            {
                Text = hallName,
                FontWeight = FontWeights.Bold,
                FontSize = 18,
                TextAlignment = TextAlignment.Center,
            });

            panel.Children.Add(new TextBlock { Text = $"Customer: {customerName}", FontSize = 14 });
            panel.Children.Add(new TextBlock { Text = $"Seat Type: {seatType}", FontSize = 14 });
            panel.Children.Add(new TextBlock { Text = $"Total Hours: {totalHours}", FontSize = 14 });
            panel.Children.Add(new TextBlock { Text = $"Persons: {persons}", FontSize = 14 });
            panel.Children.Add(new TextBlock { Text = $"Rate per Person: ₹{rate:F2}", FontSize = 14 });
            panel.Children.Add(new TextBlock { Text = $"Total Amount: ₹{totalAmount:F2}", FontSize = 14 });
            panel.Children.Add(new TextBlock { Text = $"Paiddd Amount: ₹{paidAmount:F2}", FontSize = 14 });
            panel.Children.Add(new TextBlock
            {
                Text = $"Balance: ₹{balance:F2}",
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });
            panel.Children.Add(new TextBlock { Text = $"Date: {DateTime.Now:dd-MM-yyyy hh:mm tt}", FontSize = 13 });

            panel.Children.Add(image);

            panel.Children.Add(new TextBlock
            {
                Text = "Thank you for visiting!",
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Powered by AR TECHNOLOGIES ",
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            });

            // Bottom separator line below the receipt
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = Brushes.Black,
                Margin = new Thickness(0, 6, 0, 0)
            });

            // Print with proper 300 DPI scaling
            TryPrintHighDPI(panel, dpi);
        }

        private static void TryPrintHighDPI(Visual visual, int dpi)
        {
            PrintDialog printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                if (visual is FrameworkElement element)
                {
                    // Measure and arrange layout properly
                    element.Measure(new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight));
                    element.Arrange(new Rect(new Point(0, 0),
                        new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight)));
                }

                // Print directly at natural layout size (no scaling)
                printDialog.PrintVisual(visual, "Billing Receipt");
            }
        }




    }
}