//ReceiptHelper

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
            string proofValue = "",
            TimeSpan? outTime = null,
            PrinterProfile? printerProfile = null)
        {
            // Get printer profile if not provided
            if (printerProfile == null)
            {
                printerProfile = PrinterHelper.GetCurrentPrinterProfile();
            }
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

            // Root panel with dynamic width based on printer profile
            var root = new StackPanel
            {
                Background = Brushes.White,
                Width = printerProfile.ReceiptWidth,
                Margin = new Thickness(0, 0, 0, 0),
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
                    Margin = new Thickness(5, 2, 5, 1)
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
                    Margin = new Thickness(5, 0, 5, 1)
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

            // Time and Date row
            DateTime currentInTime = inTime ?? DateTime.Now;
            var timeDateGrid = new Grid
            {
                Margin = new Thickness(printerProfile.LeftMargin, 2, printerProfile.RightMargin, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            timeDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90, GridUnitType.Pixel) });
            timeDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90, GridUnitType.Pixel) });
            
            var timeBlock = new TextBlock
            {
                Text = $"Time {currentInTime.ToString("HH:mm")}",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(0, 0, 0, 0)
            };
            Grid.SetColumn(timeBlock, 0);
            
            var dateBlock = new TextBlock
            {
                Text = currentInTime.ToString("dd/MM/yyyy"),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, 0, 0)
            };
            Grid.SetColumn(dateBlock, 1);
            
            timeDateGrid.Children.Add(timeBlock);
            timeDateGrid.Children.Add(dateBlock);
            stack.Children.Add(timeDateGrid);

            // Helper function to add key-value rows with minimal spacing
            void AddRow(string label, string value, bool isLast = false, bool isBold = false)
            {
                var grid = new Grid
                {
                    Margin = new Thickness(0, 1, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125, GridUnitType.Pixel) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                var leftBlock = new TextBlock 
                { 
                    Text = label,
                    FontSize = 13,
                    FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                    Margin = new Thickness(printerProfile.LeftMargin, 0, 0, 0),
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

            // Customer details
            AddRow("Name", customerName);
            AddRow("Phone", phoneNo);
            
            // Proof details (combined format: "aadhar: 21221212")
            if (!string.IsNullOrWhiteSpace(proofType) && !string.IsNullOrWhiteSpace(proofValue))
            {
                AddRow(proofType, proofValue);
            }

            // Billing details
            AddRow("Persons", persons.ToString());
            AddRow("Total Hours", totalHours.ToString());
            AddRow("Rate / Person", $"₹{ratePerPerson:F0}");
            
            // Calculate Out Time: if outTime is provided use it, otherwise calculate from inTime + totalHours
            string displayOutTime = "Pending";
            if (outTime.HasValue)
            {
                displayOutTime = outTime.Value.ToString(@"hh\:mm");
            }
            else if (inTime.HasValue)
            {
                var calculatedOutTime = currentInTime.Add(TimeSpan.FromHours(totalHours));
                displayOutTime = calculatedOutTime.ToString("HH:mm");
            }
            AddRow("Out Time", displayOutTime);
            
            AddRow("Total Amount", $"₹{totalAmount:F0}", isBold: true);
            
            // Barcode image with dynamic sizing based on printer
            var barcode = new Image
            {
                Source = GenerateBarcodeImage(billId, width: (int)(printerProfile.BarcodeWidth * 2.2), height: (int)(printerProfile.BarcodeHeight * 2.2)),
                Width = printerProfile.BarcodeWidth,
                Height = printerProfile.BarcodeHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(printerProfile.LeftMargin, 3, 0, 2),
                Stretch = Stretch.Uniform
            };
            stack.Children.Add(barcode);

            // Note at the bottom (left-aligned)
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
                    Margin = new Thickness(printerProfile.LeftMargin, 3, printerProfile.RightMargin, 2)
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

            // Debug: Log out_time value
            Logger.Log($"Printing receipt - Booking ID: {booking.booking_id}, Out Time: {(booking.out_time.HasValue ? booking.out_time.Value.ToString(@"hh\:mm") : "null")} (HasValue: {booking.out_time.HasValue})");

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
                proofValue: booking.proof_id ?? "",
                outTime: booking.out_time
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

            var printerProfile = PrinterHelper.GetCurrentPrinterProfile();

            // measure & arrange at desired size using printer profile width
            double width = printerProfile.ReceiptWidth;
            double height = 600; // generous; RenderTargetBitmap will crop if smaller needed
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(new Size(width, height)));
            visual.UpdateLayout();

            var rtb = new RenderTargetBitmap((int)width, (int)height, printerProfile.DPI, printerProfile.DPI, PixelFormats.Pbgra32);
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