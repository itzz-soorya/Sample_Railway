using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using UserModule.Models;

namespace UserModule
{
    public partial class Report : UserControl
    {
        private List<Booking1> allBookings = new List<Booking1>();
        private List<Booking1> filteredBookings = new List<Booking1>();
        private string searchText = "";
        private int currentPage = 1;
        private const int pageSize = 20;
        private int totalPages = 1;

        public Report()
        {
            InitializeComponent();
            
            // Set default date range (last 30 days)
            ToDatePicker.SelectedDate = DateTime.Now;
            FromDatePicker.SelectedDate = DateTime.Now.AddDays(-30);
            
            // Load data
            LoadReportData();
        }

        private void LoadReportData()
        {
            try
            {
                // Get all bookings from local database
                allBookings = OfflineBookingStorage.GetBasicBookings();
                
                if (allBookings != null && allBookings.Any())
                {
                    // Apply current date filter
                    ApplyDateFilter();
                }
                else
                {
                    // No data available
                    ClearReport();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show("Failed to load report data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyDateFilter()
        {
            DateTime fromDate = FromDatePicker.SelectedDate ?? DateTime.Now.AddDays(-30);
            DateTime toDate = ToDatePicker.SelectedDate ?? DateTime.Now;
            
            // Set time to cover full day range
            fromDate = fromDate.Date; // Start of day
            toDate = toDate.Date.AddDays(1).AddSeconds(-1); // End of day

            // Get current logged-in worker info
            string currentWorkerId = LocalStorage.GetItem("workerId") ?? "";
            string currentUsername = LocalStorage.GetItem("username") ?? "";

            // Filter bookings by:
            // 1. Date range and completed status
            // 2. Either created by this worker (worker_id) OR closed by this worker (closed_by)
            filteredBookings = allBookings
                .Where(b => b.created_at.HasValue && 
                           b.created_at.Value >= fromDate && 
                           b.created_at.Value <= toDate &&
                           b.status?.ToLower() == "completed" &&
                           (b.worker_id == currentWorkerId || 
                            b.closed_by == currentUsername))
                .OrderByDescending(b => b.created_at)
                .ToList();

            // Reset to first page
            currentPage = 1;

            // Update all statistics
            UpdateSummaryCards();
            UpdateBookingTypeBreakdown();
            UpdateStatusBreakdown();
            UpdatePaymentMethodBreakdown();
            UpdateDataGrid();
        }

        private void UpdateSummaryCards()
        {
            if (filteredBookings == null || !filteredBookings.Any())
            {
                txtTotalBookings.Text = "0";
                txtTotalRevenue.Text = "₹0";
                return;
            }

            // Calculate totals
            int totalBookings = filteredBookings.Count;
            decimal totalRevenue = filteredBookings.Sum(b => b.total_amount);

            // Update UI
            txtTotalBookings.Text = totalBookings.ToString();
            txtTotalRevenue.Text = $"₹{totalRevenue:N0}";
        }

        private void UpdateBookingTypeBreakdown()
        {
            if (filteredBookings == null || !filteredBookings.Any())
            {
                txtSittingCount.Text = "0";
                txtSittingRevenue.Text = "₹0";
                txtSleeperCount.Text = "0";
                txtSleeperRevenue.Text = "₹0";
                return;
            }

            // Sitting bookings
            var sittingBookings = filteredBookings
                .Where(b => "Sitting".Equals(b.booking_type, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            int sittingCount = sittingBookings.Count;
            decimal sittingRevenue = sittingBookings.Sum(b => b.total_amount);

            // Sleeper bookings
            var sleeperBookings = filteredBookings
                .Where(b => "Sleeper".Equals(b.booking_type, StringComparison.OrdinalIgnoreCase) || 
                           "Sleeping".Equals(b.booking_type, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            int sleeperCount = sleeperBookings.Count;
            decimal sleeperRevenue = sleeperBookings.Sum(b => b.total_amount);

            // Update UI
            txtSittingCount.Text = sittingCount.ToString();
            txtSittingRevenue.Text = $"₹{sittingRevenue:N0}";
            txtSleeperCount.Text = sleeperCount.ToString();
            txtSleeperRevenue.Text = $"₹{sleeperRevenue:N0}";
        }

        private void UpdateStatusBreakdown()
        {
            if (filteredBookings == null || !filteredBookings.Any())
            {
                txtActiveCount.Text = "0";
                txtActiveAmount.Text = "₹0";
                txtCompletedCount.Text = "0";
                txtCompletedAmount.Text = "₹0";
                return;
            }

            // Active bookings
            var activeBookings = filteredBookings
                .Where(b => "active".Equals(b.status, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            int activeCount = activeBookings.Count;
            decimal activeAmount = activeBookings.Sum(b => b.total_amount);

            // Completed bookings
            var completedBookings = filteredBookings
                .Where(b => "completed".Equals(b.status, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            int completedCount = completedBookings.Count;
            decimal completedAmount = completedBookings.Sum(b => b.total_amount);

            // Update UI
            txtActiveCount.Text = activeCount.ToString();
            txtActiveAmount.Text = $"₹{activeAmount:N0}";
            txtCompletedCount.Text = completedCount.ToString();
            txtCompletedAmount.Text = $"₹{completedAmount:N0}";
        }

        private void UpdatePaymentMethodBreakdown()
        {
            if (filteredBookings == null || !filteredBookings.Any())
            {
                txtPaymentCount.Text = "0";
                txtCashAmount.Text = "₹0";
                txtOnlineAmount.Text = "₹0";
                return;
            }

            // Group by payment method and sum paid amounts
            // Note: This shows the final payment method used
            // For bookings with mixed payments (advance online + balance cash),
            // only the last payment method is recorded in current system
            
            int totalCount = filteredBookings.Count;
            decimal cashAmount = 0;
            decimal onlineAmount = 0;

            foreach (var booking in filteredBookings)
            {
                string paymentMethod = booking.payment_method?.ToLower() ?? "cash";
                decimal paidAmt = booking.paid_amount;

                // Check if payment method is cash
                if (paymentMethod == "cash")
                {
                    cashAmount += paidAmt;
                }
                // Otherwise treat as online (UPI, Online, Card, PhonePe, GooglePay, etc.)
                else
                {
                    onlineAmount += paidAmt;
                }
            }

            // Update UI
            txtPaymentCount.Text = totalCount.ToString();
            txtCashAmount.Text = $"₹{cashAmount:N0}";
            txtOnlineAmount.Text = $"₹{onlineAmount:N0}";
        }

        private void UpdateDataGrid()
        {
            if (filteredBookings == null || !filteredBookings.Any())
            {
                ReportDataGrid.ItemsSource = null;
                totalPages = 1;
                currentPage = 1;
                UpdatePaginationControls();
                return;
            }

            // Apply search filter
            var searchResults = filteredBookings;
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                searchResults = filteredBookings.Where(b =>
                    (b.booking_id?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (b.guest_name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (b.phone_number?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false)
                ).ToList();
            }

            // Calculate total pages
            totalPages = (int)Math.Ceiling(searchResults.Count / (double)pageSize);
            
            // Ensure current page is within bounds
            if (currentPage > totalPages) currentPage = totalPages;
            if (currentPage < 1) currentPage = 1;

            // Get items for current page
            var pagedData = searchResults
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ReportDataGrid.ItemsSource = null;
            ReportDataGrid.ItemsSource = pagedData;
            
            UpdatePaginationControls(searchResults.Count);
        }

        private void UpdatePaginationControls(int recordCount = 0)
        {
            if (recordCount == 0) recordCount = filteredBookings?.Count ?? 0;
            
            txtPageInfo.Text = $"Page {currentPage} of {totalPages}";
            txtRecordInfo.Text = $"Showing {Math.Min((currentPage - 1) * pageSize + 1, recordCount)} - {Math.Min(currentPage * pageSize, recordCount)} of {recordCount} records";
            
            btnPreviousPage.IsEnabled = currentPage > 1;
            btnNextPage.IsEnabled = currentPage < totalPages;
        }

        private void PreviousPage_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage > 1)
            {
                currentPage--;
                UpdateDataGrid();
            }
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage < totalPages)
            {
                currentPage++;
                UpdateDataGrid();
            }
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchText = txtSearch.Text?.Trim() ?? "";
            currentPage = 1; // Reset to first page
            UpdateDataGrid();
        }

        private void ClearReport()
        {
            txtTotalBookings.Text = "0";
            txtTotalRevenue.Text = "₹0";
            
            txtSittingRevenue.Text = "₹0";
            txtSleeperCount.Text = "0";
            txtSleeperRevenue.Text = "₹0";
            
            txtActiveCount.Text = "0";
            txtActiveAmount.Text = "₹0";
            txtCompletedCount.Text = "0";
            txtCompletedAmount.Text = "₹0";
            
            txtPaymentCount.Text = "0";
            txtCashAmount.Text = "₹0";
            txtOnlineAmount.Text = "₹0";
            
            ReportDataGrid.ItemsSource = null;
        }

        // Event Handlers
        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Please select both From Date and To Date.", "Invalid Date Range", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (FromDatePicker.SelectedDate > ToDatePicker.SelectedDate)
            {
                MessageBox.Show("From Date cannot be later than To Date.", "Invalid Date Range", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ApplyDateFilter();
        }

        private void QuickFilter_Today(object sender, RoutedEventArgs e)
        {
            FromDatePicker.SelectedDate = DateTime.Now.Date;
            ToDatePicker.SelectedDate = DateTime.Now.Date;
            ApplyDateFilter();
        }

        private void QuickFilter_Last7Days(object sender, RoutedEventArgs e)
        {
            FromDatePicker.SelectedDate = DateTime.Now.AddDays(-7).Date;
            ToDatePicker.SelectedDate = DateTime.Now.Date;
            ApplyDateFilter();
        }

        private void QuickFilter_Last30Days(object sender, RoutedEventArgs e)
        {
            FromDatePicker.SelectedDate = DateTime.Now.AddDays(-30).Date;
            ToDatePicker.SelectedDate = DateTime.Now.Date;
            ApplyDateFilter();
        }

        private void QuickFilter_ThisMonth(object sender, RoutedEventArgs e)
        {
            DateTime now = DateTime.Now;
            FromDatePicker.SelectedDate = new DateTime(now.Year, now.Month, 1);
            ToDatePicker.SelectedDate = now.Date;
            ApplyDateFilter();
        }

        private void PrintReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (filteredBookings == null || !filteredBookings.Any())
                {
                    MessageBox.Show("No data to print.", "Empty Report", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Create print document
                PrintDialog printDialog = new PrintDialog();
                
                if (printDialog.ShowDialog() == true)
                {
                    // Create a FlowDocument for printing
                    FlowDocument document = CreatePrintDocument();
                    
                    // Print the document
                    IDocumentPaginatorSource idpSource = document;
                    printDialog.PrintDocument(idpSource.DocumentPaginator, "Railax Report");
                    
                    MessageBox.Show("Report sent to printer successfully.", "Print Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show("Failed to print report.", "Print Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private FlowDocument CreatePrintDocument()
        {
            FlowDocument doc = new FlowDocument();
            doc.PagePadding = new Thickness(50);
            doc.ColumnWidth = double.PositiveInfinity;

            // Title
            Paragraph title = new Paragraph(new Run("Railax - Booking Report"));
            title.FontSize = 24;
            title.FontWeight = FontWeights.Bold;
            title.TextAlignment = TextAlignment.Center;
            title.Margin = new Thickness(0, 0, 0, 20);
            doc.Blocks.Add(title);

            // Date Range
            Paragraph dateRange = new Paragraph(new Run(
                $"Date Range: {FromDatePicker.SelectedDate:dd/MM/yyyy} to {ToDatePicker.SelectedDate:dd/MM/yyyy}"));
            dateRange.FontSize = 12;
            dateRange.TextAlignment = TextAlignment.Center;
            dateRange.Margin = new Thickness(0, 0, 0, 20);
            doc.Blocks.Add(dateRange);

            // Summary Section
            Paragraph summary = new Paragraph();
            summary.Inlines.Add(new Bold(new Run("Summary\n")));
            summary.Inlines.Add(new Run($"Total Bookings: {txtTotalBookings.Text}\n"));
            summary.Inlines.Add(new Run($"Total Revenue: {txtTotalRevenue.Text}\n"));
            summary.FontSize = 12;
            summary.Margin = new Thickness(0, 0, 0, 20);
            doc.Blocks.Add(summary);

            // Booking Details Table
            Table table = new Table();
            table.CellSpacing = 0;
            table.BorderBrush = Brushes.Black;
            table.BorderThickness = new Thickness(1);

            // Add columns
            table.Columns.Add(new TableColumn { Width = new GridLength(100) });
            table.Columns.Add(new TableColumn { Width = new GridLength(120) });
            table.Columns.Add(new TableColumn { Width = new GridLength(80) });
            table.Columns.Add(new TableColumn { Width = new GridLength(70) });
            table.Columns.Add(new TableColumn { Width = new GridLength(80) });
            table.Columns.Add(new TableColumn { Width = new GridLength(70) });

            // Table header
            TableRowGroup headerGroup = new TableRowGroup();
            TableRow headerRow = new TableRow();
            headerRow.Background = Brushes.LightGray;
            
            string[] headers = { "Booking ID", "Guest Name", "Type", "Date", "Amount", "Status" };
            foreach (string header in headers)
            {
                TableCell cell = new TableCell(new Paragraph(new Run(header)));
                cell.FontWeight = FontWeights.Bold;
                cell.BorderBrush = Brushes.Black;
                cell.BorderThickness = new Thickness(1);
                cell.Padding = new Thickness(5);
                headerRow.Cells.Add(cell);
            }
            headerGroup.Rows.Add(headerRow);
            table.RowGroups.Add(headerGroup);

            // Table data
            TableRowGroup dataGroup = new TableRowGroup();
            foreach (var booking in filteredBookings.Take(50)) // Limit to 50 for print
            {
                TableRow row = new TableRow();
                
                row.Cells.Add(CreateTableCell(booking.booking_id ?? ""));
                row.Cells.Add(CreateTableCell(booking.guest_name ?? ""));
                row.Cells.Add(CreateTableCell(booking.booking_type ?? ""));
                row.Cells.Add(CreateTableCell(booking.booking_date.ToString("dd/MM/yyyy")));
                row.Cells.Add(CreateTableCell($"₹{booking.total_amount:N0}"));
                row.Cells.Add(CreateTableCell(booking.status ?? ""));
                
                dataGroup.Rows.Add(row);
            }
            table.RowGroups.Add(dataGroup);
            doc.Blocks.Add(table);

            return doc;
        }

        private TableCell CreateTableCell(string text)
        {
            TableCell cell = new TableCell(new Paragraph(new Run(text)));
            cell.BorderBrush = Brushes.Black;
            cell.BorderThickness = new Thickness(1);
            cell.Padding = new Thickness(5);
            cell.FontSize = 10;
            return cell;
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Excel export feature coming soon!", "Feature Not Available", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportCSV_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (filteredBookings == null || !filteredBookings.Any())
                {
                    MessageBox.Show("No data to export.", "Empty Report", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Create save file dialog
                Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog();
                saveDialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                saveDialog.FileName = $"Railax_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                saveDialog.DefaultExt = ".csv";

                if (saveDialog.ShowDialog() == true)
                {
                    StringBuilder csv = new StringBuilder();
                    
                    // Header
                    csv.AppendLine("Booking ID,Guest Name,Phone,Type,Date,In Time,Hours,Persons,Total Amount,Paid Amount,Balance,Status,Payment Method");
                    
                    // Data rows
                    foreach (var booking in filteredBookings)
                    {
                        csv.AppendLine($"\"{booking.booking_id}\"," +
                            $"\"{booking.guest_name}\"," +
                            $"\"{booking.phone_number}\"," +
                            $"\"{booking.booking_type}\"," +
                            $"\"{booking.booking_date:dd/MM/yyyy}\"," +
                            $"\"{booking.in_time}\"," +
                            $"{booking.total_hours}," +
                            $"{booking.number_of_persons}," +
                            $"{booking.total_amount}," +
                            $"{booking.paid_amount}," +
                            $"{booking.balance_amount}," +
                            $"\"{booking.status}\"," +
                            $"\"{booking.payment_method}\"");
                    }
                    
                    // Write to file
                    File.WriteAllText(saveDialog.FileName, csv.ToString(), Encoding.UTF8);
                    
                    MessageBox.Show($"Report exported successfully to:\n{saveDialog.FileName}", 
                        "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show("Failed to export CSV file.", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReprintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as System.Windows.Controls.Button;
                if (button?.Tag is Booking1 booking)
                {
                    // Print receipt using existing helper
                    bool success = ReceiptHelper.GenerateAndPrintReceipt(booking);
                    
                    Logger.Log($"Reprinted receipt for booking {booking.booking_id}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show("Failed to print receipt.", "Print Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // Converter for placeholder visibility
    public class TextToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converter for last 4 digits of booking ID
    public class Last4DigitsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value?.ToString() ?? "";
            return text.Length > 4 ? text.Substring(text.Length - 4) : text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converter for balance calculation
    public class BalanceConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] != null && values[1] != null)
            {
                try
                {
                    decimal total = System.Convert.ToDecimal(values[0]);
                    decimal paid = System.Convert.ToDecimal(values[1]);
                    return total - paid;
                }
                catch
                {
                    return 0m;
                }
            }
            return 0m;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converter for payment display (amount with C/O indicator)
    public class PaymentDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] != null)
            {
                try
                {
                    decimal amount = System.Convert.ToDecimal(values[0]);
                    string paymentMethod = values[1]?.ToString() ?? "";
                    
                    if (amount == 0)
                        return "0";
                    
                    string indicator = "";
                    if (paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase))
                        indicator = "(C)";
                    else if (paymentMethod.Equals("Online", StringComparison.OrdinalIgnoreCase) ||
                             paymentMethod.Equals("UPI", StringComparison.OrdinalIgnoreCase) ||
                             paymentMethod.Equals("Card", StringComparison.OrdinalIgnoreCase))
                        indicator = "(O)";
                    
                    return $"{amount:N0}{indicator}";
                }
                catch
                {
                    return "0";
                }
            }
            return "0";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
