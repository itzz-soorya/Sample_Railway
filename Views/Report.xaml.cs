using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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
        private WorkerSummaryRecord? currentSummary = null;
        private string searchText = "";
        private int currentPage = 1;
        private const int pageSize = 20;
        private int totalPages = 1;

        public Report()
        {
            Logger.Log("[Report] Constructor started");
            InitializeComponent();
            
            // Set default to today (show active session only)
            ToDatePicker.SelectedDate = DateTime.Now;
            FromDatePicker.SelectedDate = DateTime.Now;
            Logger.Log($"[Report] Date pickers set: From={FromDatePicker.SelectedDate:yyyy-MM-dd}, To={ToDatePicker.SelectedDate:yyyy-MM-dd}");
            
            // Load data with server sync
            LoadReportDataWithSync();
            Logger.Log("[Report] Constructor completed");
        }

        private async void LoadReportDataWithSync()
        {
            Logger.Log("[Report] LoadReportDataWithSync started");
            try
            {
                // Get current worker ID
                string currentWorkerId = LocalStorage.GetItem("workerId") ?? "";
                Logger.Log($"[Report] Current worker ID: {currentWorkerId}");
                
                if (string.IsNullOrEmpty(currentWorkerId))
                {
                    Logger.Log("[Report] Worker ID is empty, clearing report");
                    ClearReport();
                    return;
                }

                // Check if admin closed balance and close session if needed
                await CheckAndCloseSessionIfAdminClosedBalance();

                // Sync completed bookings from server first
                try
                {
                    string syncResult = await OfflineBookingStorage.SyncCompletedBookingsFromServerAsync(currentWorkerId);
                    Logger.Log($"Server sync result: {syncResult}");
                }
                catch (Exception syncEx)
                {
                    Logger.LogError(syncEx);
                    Logger.Log("Server sync failed, loading local data only");
                }

                // Load data from local database
                LoadReportData();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show("Failed to load report data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadReportData()
        {
            Logger.Log("[Report] LoadReportData started");
            try
            {
                // Get current worker ID and admin ID
                string currentWorkerId = LocalStorage.GetItem("workerId") ?? "";
                string adminId = LocalStorage.GetItem("adminId") ?? "";
                Logger.Log($"[Report] WorkerId: {currentWorkerId}, AdminId: {adminId}");
                
                // Load active worker summary (try current worker first, then any active session)
                if (!string.IsNullOrEmpty(currentWorkerId) && !string.IsNullOrEmpty(adminId))
                {
                    currentSummary = OfflineBookingStorage.GetActiveWorkerSummary(currentWorkerId, adminId);
                    Logger.Log($"[Report] GetActiveWorkerSummary result: {(currentSummary != null ? "Found" : "Null")}");
                }
                
                // If no session for current worker, get ANY active session (admin hasn't closed balance yet)
                if (currentSummary == null && !string.IsNullOrEmpty(adminId))
                {
                    currentSummary = OfflineBookingStorage.GetAnyActiveWorkerSummary(adminId);
                    
                    // Update currentWorkerId to match the active session found
                    if (currentSummary != null)
                    {
                        currentWorkerId = currentSummary.WorkerId;
                        Logger.Log($"Using active session for worker: {currentWorkerId}");
                    }
                }
                
                // Only show report if there's an active session
                if (currentSummary == null)
                {
                    // No active session - clear the report
                    ClearReport();
                    return;
                }
                
                // Get all bookings from local database (for detail view)
                allBookings = OfflineBookingStorage.GetBasicBookings();
                
                if (allBookings != null && allBookings.Any())
                {
                    // Apply current date filter
                    ApplyDateFilter();
                }
                else
                {
                    // No bookings but active session exists - show summary
                    UpdateAllStatistics();
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
            Logger.Log("[Report] ApplyDateFilter started");
            DateTime fromDate = FromDatePicker.SelectedDate ?? DateTime.Now.AddDays(-30);
            DateTime toDate = ToDatePicker.SelectedDate ?? DateTime.Now;
            
            // Set time to cover full day range
            fromDate = fromDate.Date; // Start of day
            toDate = toDate.Date.AddDays(1).AddSeconds(-1); // End of day
            Logger.Log($"[Report] Date range: {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}");

            // Get current logged-in worker info
            string currentWorkerId = LocalStorage.GetItem("workerId") ?? "";
            string currentUsername = LocalStorage.GetItem("username") ?? "";
            string adminId = LocalStorage.GetItem("adminId") ?? "";

            // Determine if showing current day only (active session)
            bool isToday = toDate.Date == DateTime.Now.Date && fromDate.Date == DateTime.Now.Date;
            
            if (isToday)
            {
                // Show only active session data for today
                currentSummary = OfflineBookingStorage.GetActiveWorkerSummary(currentWorkerId, adminId);
                Logger.Log("Showing active session data for today");
            }
            else
            {
                // For date range filtering, use aggregated summary
                currentSummary = OfflineBookingStorage.GetAggregatedWorkerSummary(currentWorkerId, fromDate, toDate);
                
                // If no aggregated data for range, show active session
                if (currentSummary == null)
                {
                    currentSummary = OfflineBookingStorage.GetActiveWorkerSummary(currentWorkerId, adminId);
                    Logger.Log("No aggregated data found, showing active session");
                }
                else
                {
                    Logger.Log($"Showing aggregated data from {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}");
                }
            }

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
            UpdateAllStatistics();
            UpdateDataGrid();
        }

        private void UpdateSummaryCards()
        {
            Logger.Log("[Report] UpdateSummaryCards started");
            
            // Prefer worker_summary data when available (it has accurate amounts)
            if (currentSummary != null && currentSummary.Status == "active")
            {
                txtTotalBookings.Text = currentSummary.TotalBooking.ToString();
                decimal summaryRevenue = currentSummary.SittingBookingTotalAmount + currentSummary.SleepingTotalAmount;
                txtTotalRevenue.Text = $"₹{summaryRevenue:N0}";
                Logger.Log($"[Report] Summary cards updated from worker summary: Bookings={currentSummary.TotalBooking}, Revenue=₹{summaryRevenue}");
                return;
            }
            
            // Fallback: Calculate from bookings if no worker summary
            // Get current worker info
            string currentUsername = LocalStorage.GetItem("username") ?? "";
            string currentWorkerId = LocalStorage.GetItem("workerId") ?? "";
            
            if (allBookings != null && allBookings.Any() && !string.IsNullOrEmpty(currentUsername))
            {
                // Get active bookings by this worker
                var activeBookings = allBookings
                    .Where(b => "active".Equals(b.status, StringComparison.OrdinalIgnoreCase) &&
                               (b.worker_id == currentWorkerId || b.booked_by == currentUsername))
                    .ToList();
                
                // Get completed bookings where this worker was involved (created OR closed)
                var completedBookings = allBookings
                    .Where(b => "completed".Equals(b.status, StringComparison.OrdinalIgnoreCase))
                    .Where(b => b.booked_by == currentUsername || b.closed_by == currentUsername)
                    .ToList();
                
                // Calculate active bookings revenue
                decimal activeRevenue = activeBookings.Sum(b => b.paid_amount);
                
                // Calculate completed bookings revenue (for this worker)
                decimal completedRevenue = completedBookings.Sum(b => 
                {
                    // If they created AND closed the booking, they get full payment
                    if (b.booked_by == currentUsername && b.closed_by == currentUsername)
                        return b.paid_amount + b.balance_amount;
                    // If they only created it, they get paid_amount (initial payment)
                    else if (b.booked_by == currentUsername && b.closed_by != currentUsername)
                    {
                        // Backwards compatibility: If balance_amount is 0 but booking was closed by someone else,
                        // the paid_amount might include everything (old buggy data)
                        // In this case, we need to calculate the initial payment from price_per_person
                        if (b.balance_amount == 0 && b.total_amount > b.price_per_person * b.number_of_persons)
                        {
                            // Old data format - calculate initial payment
                            decimal initialPayment = b.price_per_person * b.number_of_persons * b.total_hours;
                            return Math.Min(initialPayment, b.paid_amount);
                        }
                        return b.paid_amount;
                    }
                    // If they only closed it, they get the balance_amount they collected
                    else if (b.closed_by == currentUsername && b.booked_by != currentUsername)
                    {
                        // Backwards compatibility: If balance_amount is 0 but total_amount > expected initial,
                        // calculate what the balance should have been
                        if (b.balance_amount == 0 && b.paid_amount > 0)
                        {
                            decimal expectedInitial = b.price_per_person * b.number_of_persons * b.total_hours;
                            if (b.total_amount > expectedInitial)
                            {
                                // Old data - calculate balance as difference
                                return b.total_amount - expectedInitial;
                            }
                        }
                        return b.balance_amount;
                    }
                    else
                        return 0;
                });
                
                // Total Revenue = Active Revenue + Completed Revenue
                decimal totalRevenue = activeRevenue + completedRevenue;
                int totalBookings = activeBookings.Count + completedBookings.Count;
                
                txtTotalBookings.Text = totalBookings.ToString();
                txtTotalRevenue.Text = $"₹{totalRevenue:N0}";
                Logger.Log($"[Report] Summary cards updated: Bookings={totalBookings}, Active Revenue=₹{activeRevenue}, Completed Revenue=₹{completedRevenue}, Total Revenue=₹{totalRevenue}");
                return;
            }
            
            // Fallback to worker_summary if no booking data
            if (currentSummary != null)
            {
                txtTotalBookings.Text = currentSummary.TotalBooking.ToString();
                decimal summaryRevenue = currentSummary.SittingBookingTotalAmount + currentSummary.SleepingTotalAmount;
                txtTotalRevenue.Text = $"₹{summaryRevenue:N0}";
                Logger.Log($"[Report] Summary cards updated from worker summary: Bookings={currentSummary.TotalBooking}, Revenue=₹{summaryRevenue}");
                return;
            }

            // No data available
            txtTotalBookings.Text = "0";
            txtTotalRevenue.Text = "₹0";
        }

        private void UpdateAllStatistics()
        {
            UpdateSummaryCards();
            UpdateBookingTypeBreakdown();
            UpdateStatusBreakdown();
            UpdatePaymentMethodBreakdown();
        }

        private void UpdateBookingTypeBreakdown()
        {
            Logger.Log("[Report] UpdateBookingTypeBreakdown started");
            
            // Prefer worker_summary data when available (it has accurate amounts)
            if (currentSummary != null && currentSummary.Status == "active")
            {
                txtSittingCount.Text = currentSummary.SittingBookingCount.ToString();
                txtSittingRevenue.Text = $"₹{currentSummary.SittingBookingTotalAmount:N0}";
                txtSleeperCount.Text = currentSummary.SleepingBookingCount.ToString();
                txtSleeperRevenue.Text = $"₹{currentSummary.SleepingTotalAmount:N0}";
                Logger.Log($"[Report] Booking type breakdown from summary: Sitting={currentSummary.SittingBookingCount}/₹{currentSummary.SittingBookingTotalAmount}, Sleeper={currentSummary.SleepingBookingCount}/₹{currentSummary.SleepingTotalAmount}");
                return;
            }
            
            // Fallback: Calculate from bookings if no worker summary
            // Get current worker info
            string currentUsername = LocalStorage.GetItem("username") ?? "";
            
            // Calculate from bookings directly (both active and completed)
            if (allBookings != null && allBookings.Any() && !string.IsNullOrEmpty(currentUsername))
            {
                // Get all bookings where this worker was involved
                var workerBookings = allBookings
                    .Where(b => ("active".Equals(b.status, StringComparison.OrdinalIgnoreCase) || 
                                "completed".Equals(b.status, StringComparison.OrdinalIgnoreCase)) &&
                               (b.booked_by == currentUsername || b.closed_by == currentUsername))
                    .ToList();
                
                // Sitting bookings
                var sittingBookings = workerBookings
                    .Where(b => "Sitting".Equals(b.booking_type, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                int sittingCount = sittingBookings.Count;
                decimal sittingRevenue = sittingBookings.Sum(b => 
                {
                    // Active bookings: only count if created by this worker
                    if ("active".Equals(b.status, StringComparison.OrdinalIgnoreCase))
                        return (b.booked_by == currentUsername) ? b.paid_amount : 0;
                    
                    // Completed bookings
                    if (b.booked_by == currentUsername && b.closed_by == currentUsername)
                        return b.paid_amount + b.balance_amount;
                    else if (b.booked_by == currentUsername)
                        return b.paid_amount;
                    else if (b.closed_by == currentUsername)
                        return b.balance_amount;
                    else
                        return 0;
                });

                // Sleeper bookings
                var sleeperBookings = workerBookings
                    .Where(b => "Sleeper".Equals(b.booking_type, StringComparison.OrdinalIgnoreCase) || 
                               "Sleeping".Equals(b.booking_type, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                int sleeperCount = sleeperBookings.Count;
                decimal sleeperRevenue = sleeperBookings.Sum(b => 
                {
                    // Active bookings: only count if created by this worker
                    if ("active".Equals(b.status, StringComparison.OrdinalIgnoreCase))
                        return (b.booked_by == currentUsername) ? b.paid_amount : 0;
                    
                    // Completed bookings
                    if (b.booked_by == currentUsername && b.closed_by == currentUsername)
                        return b.paid_amount + b.balance_amount;
                    else if (b.booked_by == currentUsername)
                        return b.paid_amount;
                    else if (b.closed_by == currentUsername)
                        return b.balance_amount;
                    else
                        return 0;
                });

                // Update UI
                txtSittingCount.Text = sittingCount.ToString();
                txtSittingRevenue.Text = $"₹{sittingRevenue:N0}";
                txtSleeperCount.Text = sleeperCount.ToString();
                txtSleeperRevenue.Text = $"₹{sleeperRevenue:N0}";
                Logger.Log($"[Report] Booking type breakdown: Sitting={sittingCount}/₹{sittingRevenue}, Sleeper={sleeperCount}/₹{sleeperRevenue}");
                return;
            }
            
            // Fallback to worker_summary
            if (currentSummary != null && currentSummary.Status == "active")
            {
                txtSittingCount.Text = currentSummary.SittingBookingCount.ToString();
                txtSittingRevenue.Text = $"₹{currentSummary.SittingBookingTotalAmount:N0}";
                txtSleeperCount.Text = currentSummary.SleepingBookingCount.ToString();
                txtSleeperRevenue.Text = $"₹{currentSummary.SleepingTotalAmount:N0}";
                Logger.Log($"[Report] Booking type breakdown from summary: Sitting={currentSummary.SittingBookingCount}/₹{currentSummary.SittingBookingTotalAmount}, Sleeper={currentSummary.SleepingBookingCount}/₹{currentSummary.SleepingTotalAmount}");
                return;
            }

            // No data available
            txtSittingCount.Text = "0";
            txtSittingRevenue.Text = "₹0";
            txtSleeperCount.Text = "0";
            txtSleeperRevenue.Text = "₹0";
        }

        private void UpdateStatusBreakdown()
        {
            Logger.Log("[Report] UpdateStatusBreakdown started");
            // Only show data if there's an active session (not closed/completed)
            if (currentSummary == null || currentSummary.Status != "active")
            {
                Logger.Log($"[Report] No active session: currentSummary={currentSummary != null}, Status={currentSummary?.Status}");
                txtActiveCount.Text = "0";
                txtActiveAmount.Text = "₹0";
                txtCompletedCount.Text = "0";
                txtCompletedAmount.Text = "₹0";
                return;
            }
            
            // Get current worker's bookings
            string currentWorkerId = LocalStorage.GetItem("workerId") ?? "";
            string currentUsername = LocalStorage.GetItem("username") ?? "";
            Logger.Log($"[Report] Status breakdown for WorkerId: {currentWorkerId}, Username: {currentUsername}");
            
            if (string.IsNullOrEmpty(currentWorkerId) || allBookings == null || !allBookings.Any())
            {
                Logger.Log("[Report] No worker ID or bookings available");
                txtActiveCount.Text = "0";
                txtActiveAmount.Text = "₹0";
                txtCompletedCount.Text = "0";
                txtCompletedAmount.Text = "₹0";
                return;
            }

            // Active bookings (any active booking by this worker)
            var activeBookings = allBookings
                .Where(b => "active".Equals(b.status, StringComparison.OrdinalIgnoreCase) &&
                           (b.worker_id == currentWorkerId || b.closed_by == currentUsername))
                .ToList();
            
            int activeCount = activeBookings.Count;
            decimal activeAmount = activeBookings.Sum(b => b.total_amount);
            Logger.Log($"[Report] Active bookings: {activeCount}, Amount: ₹{activeAmount}");

            // Completed bookings - show all completed bookings where this worker was involved (created OR closed)
            var completedBookings = allBookings
                .Where(b => "completed".Equals(b.status, StringComparison.OrdinalIgnoreCase))
                .Where(b => b.booked_by == currentUsername || b.closed_by == currentUsername)
                .ToList();
            
            int completedCount = completedBookings.Count;
            // Calculate revenue based on worker involvement (with backwards compatibility)
            decimal completedAmount = completedBookings.Sum(b => 
            {
                // If they created AND closed the booking, they get full payment
                if (b.booked_by == currentUsername && b.closed_by == currentUsername)
                    return b.paid_amount + b.balance_amount;
                // If they only created it, calculate initial payment
                else if (b.booked_by == currentUsername && b.closed_by != currentUsername)
                {
                    // Backwards compatibility for old data
                    if (b.balance_amount == 0 && b.total_amount > b.price_per_person * b.number_of_persons)
                    {
                        decimal initialPayment = b.price_per_person * b.number_of_persons * b.total_hours;
                        return Math.Min(initialPayment, b.paid_amount);
                    }
                    return b.paid_amount;
                }
                // If they only closed it, calculate balance collected
                else if (b.closed_by == currentUsername && b.booked_by != currentUsername)
                {
                    // Backwards compatibility for old data
                    if (b.balance_amount == 0 && b.paid_amount > 0)
                    {
                        decimal expectedInitial = b.price_per_person * b.number_of_persons * b.total_hours;
                        if (b.total_amount > expectedInitial)
                            return b.total_amount - expectedInitial;
                    }
                    return b.balance_amount;
                }
                else
                    return 0;
            });
            Logger.Log($"[Report] Completed bookings (session): {completedCount}, Amount: ₹{completedAmount}");

            // Update UI
            txtActiveCount.Text = activeCount.ToString();
            txtActiveAmount.Text = $"₹{activeAmount:N0}";
            txtCompletedCount.Text = completedCount.ToString();
            txtCompletedAmount.Text = $"₹{completedAmount:N0}";
        }

        /// <summary>
        /// Check worker balance and close session if admin clicked close balance (balance = 0 AND session has bookings)
        /// </summary>
        private async Task CheckAndCloseSessionIfAdminClosedBalance()
        {
            Logger.Log("[Report] CheckAndCloseSessionIfAdminClosedBalance started");
            try
            {
                string? workerId = LocalStorage.GetItem("workerId");
                string? adminId = LocalStorage.GetItem("adminId");
                Logger.Log($"[Report] Checking balance for WorkerId: {workerId}, AdminId: {adminId}");

                if (string.IsNullOrEmpty(workerId) || string.IsNullOrEmpty(adminId))
                {
                    Logger.Log("[Report] WorkerId or AdminId is empty, skipping balance check");
                    return;
                }

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                
                string apiUrl = $"https://railway-api-worker.artechnology.pro/api/Settings/worker-balance/{workerId}/{adminId}";
                Logger.Log($"[Report] Fetching balance from: {apiUrl}");
                var response = await client.GetAsync(apiUrl);
                Logger.Log($"[Report] API response status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    Logger.Log($"[Report] Balance API response: {jsonString}");
                    var balanceData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(jsonString);
                    
                    if (balanceData.TryGetProperty("balance", out System.Text.Json.JsonElement balanceElement))
                    {
                        decimal balance = balanceElement.GetDecimal();
                        Logger.Log($"[Report] Worker balance: {balance}");
                        
                        // If balance is 0 AND session has bookings, close the session (admin clicked close balance)
                        if (balance == 0)
                        {
                            Logger.Log("[Report] Balance is 0, checking if session should be closed");
                            // Check if current session has any bookings before closing
                            var activeSummary = OfflineBookingStorage.GetActiveWorkerSummary(workerId, adminId);
                            Logger.Log($"[Report] Active summary: {(activeSummary != null ? $"TotalBooking={activeSummary.TotalBooking}, Status={activeSummary.Status}" : "null")}");
                            
                            if (activeSummary != null && activeSummary.TotalBooking > 0)
                            {
                                // Session has bookings and balance is 0 - admin closed balance
                                Logger.Log("[Report] Closing session - balance 0 and has bookings");
                                bool sessionClosed = OfflineBookingStorage.CloseWorkerSession(workerId, adminId);
                                Logger.Log($"[Report] Worker session closed: {sessionClosed} for worker {workerId} - Admin closed balance");
                            }
                            else
                            {
                                Logger.Log("[Report] Not closing session - either no active summary or no bookings yet");
                            }
                        }
                        else
                        {
                            Logger.Log($"[Report] Balance is {balance}, not closing session");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // Silent fail - don't interrupt report loading if balance check fails
            }
        }

        private void UpdatePaymentMethodBreakdown()
        {
            Logger.Log("[Report] UpdatePaymentMethodBreakdown started");
            // Use worker_summary data if available and session is active
            if (currentSummary != null && currentSummary.Status == "active")
            {
                txtPaymentCount.Text = currentSummary.TotalBooking.ToString();
                txtCashAmount.Text = $"₹{currentSummary.InCashCollect:N0}";
                txtOnlineAmount.Text = $"₹{currentSummary.InUpiCollect:N0}";
                Logger.Log($"[Report] Payment breakdown: Cash=₹{currentSummary.InCashCollect}, UPI=₹{currentSummary.InUpiCollect}");
                return;
            }

            // Fallback: Calculate from bookings
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
            Logger.Log($"[Report] UpdatePaginationControls - Records: {recordCount}, Page: {currentPage}/{totalPages}");
            
            txtPageInfo.Text = $"Page {currentPage} of {totalPages}";
            txtRecordInfo.Text = $"Showing {Math.Min((currentPage - 1) * pageSize + 1, recordCount)} - {Math.Min(currentPage * pageSize, recordCount)} of {recordCount} records";
            
            btnPreviousPage.IsEnabled = currentPage > 1;
            btnNextPage.IsEnabled = currentPage < totalPages;
        }

        private void PreviousPage_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log($"[Report] PreviousPage_Click - Moving from page {currentPage} to {currentPage - 1}");
            if (currentPage > 1)
            {
                currentPage--;
                UpdateDataGrid();
            }
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log($"[Report] NextPage_Click - Moving from page {currentPage} to {currentPage + 1}");
            if (currentPage < totalPages)
            {
                currentPage++;
                UpdateDataGrid();
            }
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchText = txtSearch.Text?.Trim() ?? "";
            Logger.Log($"[Report] Search_TextChanged - Search text: '{searchText}'");
            currentPage = 1; // Reset to first page
            UpdateDataGrid();
        }

        private void ClearReport()
        {
            Logger.Log("[Report] ClearReport called");
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
            Logger.Log("[Report] ApplyFilter_Click - Manual filter apply");
            if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
            {
                Logger.Log("[Report] Date validation failed - missing dates");
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
            Logger.Log("[Report] QuickFilter_Today - Filtering today's bookings");
            FromDatePicker.SelectedDate = DateTime.Now.Date;
            ToDatePicker.SelectedDate = DateTime.Now.Date;
            ApplyDateFilter();
        }

        private void QuickFilter_Last7Days(object sender, RoutedEventArgs e)
        {
            Logger.Log("[Report] QuickFilter_Last7Days - Filtering last 7 days");
            FromDatePicker.SelectedDate = DateTime.Now.AddDays(-7).Date;
            ToDatePicker.SelectedDate = DateTime.Now.Date;
            ApplyDateFilter();
        }

        private void QuickFilter_Last30Days(object sender, RoutedEventArgs e)
        {
            Logger.Log("[Report] QuickFilter_Last30Days - Filtering last 30 days");
            FromDatePicker.SelectedDate = DateTime.Now.AddDays(-30).Date;
            ToDatePicker.SelectedDate = DateTime.Now.Date;
            ApplyDateFilter();
        }

        private void QuickFilter_ThisMonth(object sender, RoutedEventArgs e)
        {
            Logger.Log("[Report] QuickFilter_ThisMonth - Filtering this month");
            DateTime now = DateTime.Now;
            FromDatePicker.SelectedDate = new DateTime(now.Year, now.Month, 1);
            ToDatePicker.SelectedDate = now.Date;
            ApplyDateFilter();
        }

        private async void SyncData_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("[Report] SyncData_Click - Starting worker summary sync");
            try
            {
                btnSyncData.IsEnabled = false;
                btnSyncData.Content = "⏳ Syncing...";

                // Sync all worker summaries to server
                int syncedCount = await OfflineBookingStorage.SyncWorkerSummariesAsync();
                Logger.Log($"[Report] Sync completed: {syncedCount} records");

                if (syncedCount > 0)
                {
                    MessageBox.Show($"Successfully synced {syncedCount} worker summary record(s) to server.", 
                        "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    Logger.Log($"Worker summaries synced: {syncedCount} records");
                    
                    // Refresh data
                    LoadReportData();
                }
                else
                {
                    MessageBox.Show("No pending worker summaries to sync.", 
                        "Sync Status", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show($"Error syncing data: {ex.Message}", 
                    "Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnSyncData.IsEnabled = true;
                btnSyncData.Content = "🔄 Sync Data";
            }
        }

        private void PrintReport_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("[Report] PrintReport_Click - Printing report");
            try
            {
                if (filteredBookings == null || !filteredBookings.Any())
                {
                    Logger.Log("[Report] No data to print");
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
            Logger.Log("[Report] ExportToCSV_Click - Exporting report to CSV");
            try
            {
                if (filteredBookings == null || !filteredBookings.Any())
                {
                    Logger.Log("[Report] No data to export");
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
            Logger.Log("[Report] ReprintButton_Click - Reprinting receipt");
            try
            {
                var button = sender as System.Windows.Controls.Button;
                if (button?.Tag is Booking1 booking)
                {
                    Logger.Log($"[Report] Reprinting receipt for booking {booking.booking_id}");
                    
                    // Use the reprint receipt format (simplified format for reprints)
                    bool success = ReceiptHelper.GenerateAndPrintReprintReceipt(booking);
                    
                    if (success)
                    {
                        MessageBox.Show("Receipt reprinted successfully!", "Success", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        Logger.Log($"Reprinted receipt for booking {booking.booking_id}");
                    }
                    else
                    {
                        MessageBox.Show("Failed to print receipt. Please check if printer is online.", "Print Error", 
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("Unable to retrieve booking information.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                MessageBox.Show("Failed to print receipt. Please check if printer is online.", "Print Error", 
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

    // Converter for payment display (amount with (c)/(u) indicator)
    public class PaymentDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] != null)
            {
                try
                {
                    decimal amount = System.Convert.ToDecimal(values[0]);
                    string paymentMethod = values[1]?.ToString()?.Trim() ?? "";
                    
                    if (amount == 0)
                        return "0";
                    
                    string indicator = "(c)";  // Default to cash
                    
                    if (!string.IsNullOrWhiteSpace(paymentMethod))
                    {
                        // Check if it's already an indicator (o or c)
                        if (paymentMethod == "o")
                            indicator = "(o)";
                        else if (paymentMethod == "c")
                            indicator = "(c)";
                        // Otherwise check the full payment method name
                        else if (paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase))
                            indicator = "(c)";
                        else if (paymentMethod.Equals("Online", StringComparison.OrdinalIgnoreCase) ||
                                 paymentMethod.Equals("UPI", StringComparison.OrdinalIgnoreCase) ||
                                 paymentMethod.Equals("Card", StringComparison.OrdinalIgnoreCase) ||
                                 paymentMethod.Equals("GPay", StringComparison.OrdinalIgnoreCase) ||
                                 paymentMethod.Equals("PhonePe", StringComparison.OrdinalIgnoreCase) ||
                                 paymentMethod.Equals("Google Pay", StringComparison.OrdinalIgnoreCase) ||
                                 paymentMethod.Equals("Paytm", StringComparison.OrdinalIgnoreCase) ||
                                 paymentMethod.Equals("Net Banking", StringComparison.OrdinalIgnoreCase))
                            indicator = "(o)";
                    }
                    
                    return $"{amount:N0} {indicator}";
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
