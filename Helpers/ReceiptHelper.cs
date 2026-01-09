using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using UserModule.Models;

namespace UserModule
{
    public static class ReceiptHelper
    {
        private static BitmapSource GenerateBarcodeImage(string text, int width = 600, int height = 120)
        {
            if (string.IsNullOrEmpty(text)) text = "NA";

            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = 5,
                    PureBarcode = false
                }
            };

            var pixelData = writer.Write(text);

            int stride = pixelData.Width * 4;
            return BitmapSource.Create(pixelData.Width, pixelData.Height,
                600, 600, PixelFormats.Bgra32, null, pixelData.Pixels, stride);
        }

        private static UIElement BuildReceiptVisual(
            string billId,
            string customerName,
            string phoneNo,
            int totalHours,
            int persons,
            double ratePerPerson,
            double paidAmount,
            double totalAmount = 0,
            double extraCharges = 0,
            string heading1 = "Railway Booking",
            string heading2 = "",
            string info1 = "",
            string info2 = "",
            string note = "Thank you for your visit!",
            string hallName = "",
            string bookingType = "",
            DateTime? inTime = null,
            string proofType = "",
            string proofValue = "")
        {
            // Calculate base amount (before discount and extra charges)
            bool isSleeper = bookingType.Equals("Sleeper", StringComparison.OrdinalIgnoreCase) || 
                           bookingType.Equals("Sleeping", StringComparison.OrdinalIgnoreCase);
            
            double baseAmount = isSleeper 
                ? ratePerPerson * persons 
                : ratePerPerson * persons * Math.Max(1, totalHours);
            
            // If totalAmount is provided (from booking with discount applied), use it
            // Otherwise calculate from baseAmount (for backwards compatibility)
            if (totalAmount <= 0)
            {
                totalAmount = baseAmount + extraCharges;
            }
            else
            {
                // Total amount already includes discount, just add extra charges if any
                totalAmount += extraCharges;
            }
            
            double balance = totalAmount - paidAmount;

            // Root panel optimized for 78mm thermal paper (width ~295px at 96dpi)
            var root = new StackPanel
            {
                Background = Brushes.White,
                Width = 295,
                Margin = new Thickness(0, 2, 0, 2),
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var stack = root;

            // Header - Heading 1
            if (!string.IsNullOrWhiteSpace(heading1))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = heading1.ToUpper(),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 1)
                });
            }

            // Header - Heading 2
            if (!string.IsNullOrWhiteSpace(heading2))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = heading2.ToUpper(),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 1)
                });
            }

            // Info 1
            if (!string.IsNullOrWhiteSpace(info1))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = info1,
                    FontSize = 8,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 1)
                });
            }

            // Info 2
            if (!string.IsNullOrWhiteSpace(info2))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = info2,
                    FontSize = 8,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3)
                });
            }

            // Hall Name (if provided)
            if (!string.IsNullOrWhiteSpace(hallName))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = hallName,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3)
                });
            }

            // Helper function to add key-value rows with minimal spacing
            void AddRow(string label, string value, bool isLast = false, bool isBold = false)
            {
                var grid = new Grid
                {
                    Margin = new Thickness(0, 1, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130, GridUnitType.Pixel) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                var leftBlock = new TextBlock 
                { 
                    Text = label,
                    FontSize = 13,
                    FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                    Margin = new Thickness(5, 0, 0, 0),
                    TextAlignment = TextAlignment.Left
                };
                Grid.SetColumn(leftBlock, 0);
                
                var colonBlock = new TextBlock 
                { 
                    Text = ":",
                    FontSize = 13,
                    FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                    Margin = new Thickness(3, 0, 8, 0)
                };
                Grid.SetColumn(colonBlock, 1);
                
                var rightBlock = new TextBlock 
                { 
                    Text = value,
                    FontSize = 13,
                    FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                    TextAlignment = TextAlignment.Left
                };
                Grid.SetColumn(rightBlock, 2);
                
                grid.Children.Add(leftBlock);
                grid.Children.Add(colonBlock);
                grid.Children.Add(rightBlock);
                stack.Children.Add(grid);
            }

            // Customer details in key-value format
            AddRow("Name", customerName);
            AddRow("Phone", phoneNo);
            AddRow("Date", (inTime ?? DateTime.Now).ToString("dd-MM-yyyy"));
            AddRow("Persons", persons.ToString());
            
            // Proof details
            if (!string.IsNullOrWhiteSpace(proofType))
            {
                AddRow("Proof Type", proofType);
            }
            if (!string.IsNullOrWhiteSpace(proofValue))
            {
                AddRow("Proof Value", proofValue);
            }

            // Billing details
            AddRow("Total Hours", totalHours.ToString());
            AddRow("Rate / Person", $"₹{ratePerPerson:F0}");
            AddRow("Base Amount", $"₹{baseAmount:F0}");
            
            // Show extra charges if any
            if (extraCharges > 0)
            {
                AddRow("Extra Charges", $"₹{extraCharges:F0}");
            }
            
            AddRow("Total Amount", $"₹{totalAmount:F0}");
            AddRow("Paid Amount", $"₹{paidAmount:F0}");
            AddRow("Balance", $"₹{balance:F0}", isBold: true);
            
            // Add In Time and Out Time in 24-hour railway format
            DateTime currentInTime = inTime ?? DateTime.Now;
            DateTime calculatedOutTime = currentInTime.AddHours(totalHours);
            
            AddRow("In Time", currentInTime.ToString("HH:mm"));
            AddRow("Out Time", calculatedOutTime.ToString("HH:mm"), isLast: true);

            // Barcode image with higher DPI
            var barcode = new Image
            {
                Source = GenerateBarcodeImage(billId, width: 600, height: 120),
                Width = 260,
                Height = 50,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 2),
                Stretch = Stretch.Uniform
            };
            stack.Children.Add(barcode);

            stack.Children.Add(new TextBlock
            {
                Text = "Scan to close the bill",
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            });

            // Note at the bottom
            if (!string.IsNullOrWhiteSpace(note))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"Note: {note}",
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    TextAlignment = TextAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(3, 3, 3, 2)
                });
            }

            return root;
        }

        public static bool GenerateAndPrintReceipt(Booking1 booking, string? savePngPath = null, double extraCharges = 0)
        {
            if (booking == null) throw new ArgumentNullException(nameof(booking));

            // calculate values (defensive conversions)
            int hours = booking.total_hours;
            int persons = booking.number_of_persons;
            double rate = Convert.ToDouble(booking.price_per_person);
            double paid = Convert.ToDouble(booking.paid_amount);
            double total = Convert.ToDouble(booking.total_amount); // Use the actual total (includes discount)

            // Get printer details from storage
            var printerDetails = OfflineBookingStorage.GetPrinterDetails();

            var visual = BuildReceiptVisual(
                billId: booking.booking_id ?? "N/A",
                customerName: booking.guest_name ?? "",
                phoneNo: booking.phone_number ?? "",
                totalHours: hours,
                persons: persons,
                ratePerPerson: rate,
                paidAmount: paid,
                totalAmount: total,
                extraCharges: extraCharges,
                heading1: printerDetails.heading1,
                heading2: printerDetails.heading2,
                info1: printerDetails.info1,
                info2: printerDetails.info2,
                note: printerDetails.note,
                hallName: printerDetails.hallName,
                bookingType: booking.booking_type ?? "",
                inTime: booking.booking_date,
                proofType: booking.proof_type ?? "",
                proofValue: booking.proof_id ?? ""
            );

            // Optionally save to PNG file for record
            if (!string.IsNullOrWhiteSpace(savePngPath))
            {
                try
                {
                    SaveVisualToPng(visual, savePngPath);
                }
                catch (Exception)
                {
                    // swallow or log — saving is optional
                    MessageBox.Show($"Failed to save receipt PNG", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // Print (uses your PrinterHelper.TryPrint that checks printer online)
            bool printed = PrinterHelper.TryPrint(visual);

            return printed;
        }

        /// <summary>
        /// Utility: render UIElement to PNG file on disk.
        /// </summary>
        private static void SaveVisualToPng(UIElement visual, string path)
        {
            if (visual == null) throw new ArgumentNullException(nameof(visual));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

            // measure & arrange at desired size (use 320x... like receipt width)
            double width = 320;
            double height = 600; // generous; RenderTargetBitmap will crop if smaller needed
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(new Size(width, height)));
            visual.UpdateLayout();

            var rtb = new RenderTargetBitmap((int)width, (int)height, 300, 300, PixelFormats.Pbgra32);
            rtb.Render(visual);

            // Trim transparent bottom if you want (left as-is for simplicity)
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(fs);
            }
        }
    }
}