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
                    Margin = 2,
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
            double extraCharges = 0,
            string heading1 = "Railway Booking",
            string heading2 = "",
            string info1 = "",
            string info2 = "",
            string note = "Thank you for your visit!",
            string hallName = "",
            string bookingType = "",
            string poweredByNote = "Powered by AR TECHNOLOGI",
            DateTime? inTime = null)
        {
            // For Sleeper with pricing tiers, ratePerPerson already includes the hour range cost
            // For Sitting, it's an hourly rate that needs to be multiplied by hours
            bool isSleeper = bookingType.Equals("Sleeper", StringComparison.OrdinalIgnoreCase) || 
                           bookingType.Equals("Sleeping", StringComparison.OrdinalIgnoreCase);
            
            double baseAmount = isSleeper 
                ? ratePerPerson * persons 
                : ratePerPerson * persons * Math.Max(1, totalHours);
            
            double totalAmount = baseAmount + extraCharges;
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
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
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
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
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
                    Margin = new Thickness(0, 1, 4, 1),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Width = 280
                };
                
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                var leftBlock = new TextBlock 
                { 
                    Text = label,
                    FontSize = 13,
                    FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                    Margin = new Thickness(5, 0, 0, 0),
                    TextAlignment = TextAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(leftBlock, 0);
                
                var colonBlock = new TextBlock 
                { 
                    Text = ":",
                    FontSize = 13,
                    FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                    Margin = new Thickness(3, 0, 2, 0),
                    TextAlignment = TextAlignment.Left
                };
                Grid.SetColumn(colonBlock, 1);
                
                var rightBlock = new TextBlock 
                { 
                    Text = value,
                    FontSize = 13,
                    FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                    TextAlignment = TextAlignment.Left,
                    Margin = new Thickness(3, 0, 5, 0),
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 150
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

            // Billing details
            AddRow("Total Hours", totalHours.ToString());
            AddRow("Rate / Person", $"₹{ratePerPerson:F0}");
            
            // Show extra charges if any
            if (extraCharges > 0)
            {
                AddRow("Extra Charges", $"₹{extraCharges:F0}");
            }
            
            AddRow("Total Amount", $"₹{totalAmount:F0}");
            
            // Add In Time and Out Time in 24-hour railway format
            DateTime currentInTime = inTime ?? DateTime.Now;
            DateTime calculatedOutTime = currentInTime.AddHours(totalHours);
            
            AddRow("In Time", currentInTime.ToString("hh:mm tt"));
            AddRow("Out Time", calculatedOutTime.ToString("hh:mm tt"), isLast: true);

            // Barcode image with higher DPI
            var barcode = new Image
            {
                Source = GenerateBarcodeImage(billId, width: 600, height: 120),
                Width = 260,
                Height = 50,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 0),
                Stretch = Stretch.UniformToFill
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

            // Note and Powered by note in one line
            var bottomStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3)
            };

            if (!string.IsNullOrWhiteSpace(note))
            {
                bottomStack.Children.Add(new TextBlock
                {
                    Text = note,
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 1),
                    MaxWidth = 280
                });
            }

            if (!string.IsNullOrWhiteSpace(poweredByNote))
            {
                bottomStack.Children.Add(new TextBlock
                {
                    Text = poweredByNote,
                    FontSize = 7,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 0),
                    MaxWidth = 280
                });
            }

            if (bottomStack.Children.Count > 0)
            {
                stack.Children.Add(bottomStack);
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
                extraCharges: extraCharges,
                heading1: printerDetails.heading1,
                heading2: printerDetails.heading2,
                info1: printerDetails.info1,
                info2: printerDetails.info2,
                note: !string.IsNullOrWhiteSpace(printerDetails.note) ? printerDetails.note : "Thank you for your visit!",
                hallName: printerDetails.hallName,
                bookingType: booking.booking_type ?? "",
                inTime: booking.booking_date
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