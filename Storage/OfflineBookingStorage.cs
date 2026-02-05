using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UserModule;
using UserModule.Models;

public static class OfflineBookingStorage
{
    // Store database in AppData\Local instead of Program Files to avoid permission issues
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
        "Railax");
    private static readonly string DbPath = Path.Combine(AppDataFolder, "offline_bookings.db");
    private const string CreateBookingApiUrl = "https://railway-api-worker.artechnology.pro/api/Booking/create";
    private const string GetCompletedBookingsApiUrl = "https://railway-api-worker.artechnology.pro/api/Booking/completed-today";

    static OfflineBookingStorage()
    {
        // Ensure the directory exists
        Directory.CreateDirectory(AppDataFolder);
        InitializeDatabase();
    }
    // All the datas are stored in a local Sqlite database when offline
    private static void InitializeDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        // Create Bookings table
        string createBookingsTable = @"
        CREATE TABLE IF NOT EXISTS Bookings (
            booking_id TEXT PRIMARY KEY,
            worker_id TEXT,
            guest_name TEXT,
            phone_number TEXT,
            number_of_persons INTEGER,
            booking_type TEXT,
            total_hours INTEGER,
            booking_date TEXT,
            in_time TEXT,
            out_time TEXT,
            proof_type TEXT,
            proof_id TEXT,
            price_per_person REAL,
            total_amount REAL,
            paid_amount REAL,
            balance_amount REAL,
            payment_method TEXT,
            created_at TEXT,
            updated_at TEXT,
            status TEXT,
            IsSynced INTEGER DEFAULT 0,
            room_number TEXT,
            booked_by TEXT,
            closed_by TEXT,
            balance_payment_method TEXT
        );";

        using (var cmd = new SqliteCommand(createBookingsTable, connection))
        {
            cmd.ExecuteNonQuery();
        }

        // Add room_number column if it doesn't exist (migration for existing databases)
        try
        {
            string addRoomNumberColumn = @"
            ALTER TABLE Bookings ADD COLUMN room_number TEXT;";
            using (var cmd = new SqliteCommand(addRoomNumberColumn, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // Column already exists, ignore error
        }

        // Add booked_by column if it doesn't exist
        try
        {
            string addBookedByColumn = @"
            ALTER TABLE Bookings ADD COLUMN booked_by TEXT;";
            using (var cmd = new SqliteCommand(addBookedByColumn, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // Column already exists, ignore error
        }

        // Add closed_by column if it doesn't exist
        try
        {
            string addClosedByColumn = @"
            ALTER TABLE Bookings ADD COLUMN closed_by TEXT;";
            using (var cmd = new SqliteCommand(addClosedByColumn, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // Column already exists, ignore error
        }

        // Add balance_payment_method column if it doesn't exist
        try
        {
            string addBalancePaymentColumn = @"
            ALTER TABLE Bookings ADD COLUMN balance_payment_method TEXT;";
            using (var cmd = new SqliteCommand(addBalancePaymentColumn, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // Column already exists, ignore error
        }

        // Drop old Settings table to recreate with new schema
        string dropOldSettingsTable = "DROP TABLE IF EXISTS Settings";
        using (var cmd = new SqliteCommand(dropOldSettingsTable, connection))
        {
            cmd.ExecuteNonQuery();
        }

        // Create Settings table with only 2 types
        string createSettingsTable = @"
        CREATE TABLE IF NOT EXISTS Settings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            admin_id TEXT,
            type_1 TEXT,
            type_1_amount REAL,
            type_2 TEXT,
            type_2_amount REAL,
            advance_payment_enabled INTEGER DEFAULT 0,
            default_advance_percentage REAL DEFAULT 0,
            last_synced TEXT,
            grace_time_type_1 INTEGER DEFAULT 10,
            grace_time_type_2 INTEGER DEFAULT 10
        );";

        using (var cmd = new SqliteCommand(createSettingsTable, connection))
        {
            cmd.ExecuteNonQuery();
        }

        // Add grace time columns if they don't exist (migration)
        try
        {
            string addGraceTimeColumns = @"
            ALTER TABLE Settings ADD COLUMN grace_time_type_1 INTEGER DEFAULT 25;
            ALTER TABLE Settings ADD COLUMN grace_time_type_2 INTEGER DEFAULT 25;";
            using (var cmd = new SqliteCommand(addGraceTimeColumns, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // Columns already exist, ignore error
        }
        
        // Add extra_charges column to workers_summary if it doesn't exist
        try
        {
            string addExtraChargesColumn = @"
            ALTER TABLE workers_summary ADD COLUMN extra_charges REAL DEFAULT 0;";
            using (var cmd = new SqliteCommand(addExtraChargesColumn, connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // Column already exists, ignore error
        }

        // Create HourlyPricing table for hour-based pricing tiers
        string createHourlyPricingTable = @"
        CREATE TABLE IF NOT EXISTS HourlyPricing (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            admin_id TEXT,
            min_hours INTEGER,
            max_hours INTEGER,
            amount REAL,
            last_synced TEXT
        );";

        using (var cmd = new SqliteCommand(createHourlyPricingTable, connection))
        {
            cmd.ExecuteNonQuery();
        }

        // Create workers_summary table for tracking comprehensive worker session data
        string createWorkersSummaryTable = @"
        CREATE TABLE IF NOT EXISTS workers_summary (
            s_no INTEGER PRIMARY KEY AUTOINCREMENT,
            admin_id TEXT NOT NULL,
            worker_id TEXT NOT NULL,
            
            total_booking INTEGER DEFAULT 0,
            total_person INTEGER DEFAULT 0,
            
            sitting_booking_count INTEGER DEFAULT 0,
            sleeping_booking_count INTEGER DEFAULT 0,
            
            sitting_booking_total_amount REAL DEFAULT 0,
            sleeping_total_amount REAL DEFAULT 0,
            
            in_cash_collect REAL DEFAULT 0,
            in_upi_collect REAL DEFAULT 0,
            
            total_balance REAL DEFAULT 0,
            extra_charges REAL DEFAULT 0,
            
            login_time TEXT,
            created_time TEXT,
            closed_time TEXT,
            last_updated_timestamp TEXT,
            
            status TEXT DEFAULT 'active',
            is_synced INTEGER DEFAULT 0
        );";

        using (var cmd = new SqliteCommand(createWorkersSummaryTable, connection))
        {
            cmd.ExecuteNonQuery();
        }
    }

    // Online Sync
    public static void SaveOffline(Booking1 booking)
    {
        if (booking == null) return;

        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        string insert = @"
        INSERT OR REPLACE INTO Bookings
        (booking_id, worker_id, guest_name, phone_number, number_of_persons, booking_type, total_hours,
         booking_date, in_time, out_time, proof_type, proof_id, price_per_person, total_amount, paid_amount,
         balance_amount, payment_method, created_at, updated_at, status, IsSynced, room_number, booked_by, balance_payment_method)
        VALUES (@booking_id, @worker_id, @guest_name, @phone_number, @number_of_persons, @booking_type, @total_hours,
                @booking_date, @in_time, @out_time, @proof_type, @proof_id, @price_per_person, @total_amount, 
                @paid_amount, @balance_amount, @payment_method, @created_at, @updated_at, @status, 0, @room_number, @booked_by, @balance_payment_method);";

        using var cmd = new SqliteCommand(insert, connection);
        cmd.Parameters.AddWithValue("@booking_id", booking.booking_id);
        cmd.Parameters.AddWithValue("@worker_id", booking.worker_id ?? "");
        cmd.Parameters.AddWithValue("@guest_name", booking.guest_name ?? "");
        cmd.Parameters.AddWithValue("@phone_number", booking.phone_number ?? "");
        cmd.Parameters.AddWithValue("@number_of_persons", booking.number_of_persons);
        cmd.Parameters.AddWithValue("@booking_type", booking.booking_type ?? "");
        cmd.Parameters.AddWithValue("@total_hours", booking.total_hours);
        cmd.Parameters.AddWithValue("@booking_date", booking.booking_date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@in_time", booking.in_time.ToString());
        cmd.Parameters.AddWithValue("@out_time", booking.out_time?.ToString() ?? "");
        cmd.Parameters.AddWithValue("@proof_type", booking.proof_type ?? "");
        cmd.Parameters.AddWithValue("@proof_id", booking.proof_id ?? "");
        cmd.Parameters.AddWithValue("@price_per_person", booking.price_per_person);
        cmd.Parameters.AddWithValue("@total_amount", booking.total_amount);
        cmd.Parameters.AddWithValue("@paid_amount", booking.paid_amount);
        cmd.Parameters.AddWithValue("@balance_amount", booking.balance_amount);
        cmd.Parameters.AddWithValue("@payment_method", booking.payment_method ?? "Cash");
        cmd.Parameters.AddWithValue("@created_at", booking.created_at?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@updated_at", booking.updated_at?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@status",
            string.IsNullOrWhiteSpace(booking.status)
            ? "active"
            : booking.status.ToLower());
        cmd.Parameters.AddWithValue("@room_number", booking.room_number ?? "");
        
        // Get current worker name for booked_by
        string workerName = LocalStorage.GetItem("username") ?? "Unknown";
        cmd.Parameters.AddWithValue("@booked_by", workerName);
        cmd.Parameters.AddWithValue("@balance_payment_method", "");
        
        cmd.ExecuteNonQuery();

        // Update worker summary
        string adminId = LocalStorage.GetItem("adminId") ?? "";
        if (!string.IsNullOrEmpty(booking.worker_id) && !string.IsNullOrEmpty(adminId))
        {
            UpdateWorkerSummaryOnBooking(booking.worker_id, adminId, booking);
            Logger.Log($"Worker summary updated for worker {booking.worker_id}, admin {adminId}");
        }
        else
        {
            Logger.Log($"Skipped worker summary update: worker_id={booking.worker_id}, adminId={adminId}");
        }
    }

    // New method: Save booking with online-first approach
    public static async Task<bool> SaveBookingAsync(Booking1 booking, bool showMessages = true)
    {
        if (booking == null) return false;

        // Check if network is available
        bool isOnline = NetworkInterface.GetIsNetworkAvailable();

        if (isOnline)
        {
            try
            {
                // Try to save directly to API
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                
                var bookingData = new[]
                {
                    booking
                };
                
                var json = JsonConvert.SerializeObject(bookingData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(CreateBookingApiUrl, content);
                string responseBody = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    // Save to local database as synced (IsSynced = 1)
                    SaveOfflineAsSynced(booking);
                    
                    if (showMessages)
                    {
                        MessageBox.Show($"✅ Booking saved online successfully!\n\nAPI Response:\n{responseBody}", 
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return true;
                }
                else
                {
                    // API rejected, save offline
                    Logger.Log($"API rejected booking: {response.StatusCode} - {responseBody}");
                    SaveOffline(booking);
                    
                    if (showMessages)
                    {
                        MessageBox.Show($"❌ API Error: {response.StatusCode}\n\nResponse:\n{responseBody}\n\nBooking saved locally.\nWill sync when connection is restored.", 
                            "API Error - Saved Offline", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Network error, save offline
                Logger.Log($"Error saving booking online: {ex.Message}");
                SaveOffline(booking);
                
                if (showMessages)
                {
                    MessageBox.Show($"❌ Connection Error\n\nError: {ex.Message}\n\nBooking saved locally and will sync when connection is restored.", 
                        "Network Error - Saved Offline", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }
        }
        else
        {
            // No network, save offline
            SaveOffline(booking);
            
            if (showMessages)
            {
                MessageBox.Show("📴 No Internet Connection\n\nBooking saved locally and will sync when connection is restored.", 
                    "Offline Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return false;
        }
    }

    // Helper method to save booking as already synced
    private static void SaveOfflineAsSynced(Booking1 booking)
    {
        if (booking == null) return;

        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        string insert = @"
        INSERT OR REPLACE INTO Bookings
        (booking_id, worker_id, guest_name, phone_number, number_of_persons, booking_type, total_hours,
         booking_date, in_time, out_time, proof_type, proof_id, price_per_person, total_amount, paid_amount,
         balance_amount, payment_method, created_at, updated_at, status, IsSynced, room_number, booked_by, balance_payment_method)
        VALUES (@booking_id, @worker_id, @guest_name, @phone_number, @number_of_persons, @booking_type, @total_hours,
                @booking_date, @in_time, @out_time, @proof_type, @proof_id, @price_per_person, @total_amount, 
                @paid_amount, @balance_amount, @payment_method, @created_at, @updated_at, @status, 1, @room_number, @booked_by, @balance_payment_method);";

        using var cmd = new SqliteCommand(insert, connection);
        cmd.Parameters.AddWithValue("@booking_id", booking.booking_id);
        cmd.Parameters.AddWithValue("@worker_id", booking.worker_id ?? "");
        cmd.Parameters.AddWithValue("@guest_name", booking.guest_name ?? "");
        cmd.Parameters.AddWithValue("@phone_number", booking.phone_number ?? "");
        cmd.Parameters.AddWithValue("@number_of_persons", booking.number_of_persons);
        cmd.Parameters.AddWithValue("@booking_type", booking.booking_type ?? "");
        cmd.Parameters.AddWithValue("@total_hours", booking.total_hours);
        cmd.Parameters.AddWithValue("@booking_date", booking.booking_date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@in_time", booking.in_time.ToString());
        cmd.Parameters.AddWithValue("@out_time", booking.out_time?.ToString() ?? "");
        cmd.Parameters.AddWithValue("@proof_type", booking.proof_type ?? "");
        cmd.Parameters.AddWithValue("@proof_id", booking.proof_id ?? "");
        cmd.Parameters.AddWithValue("@price_per_person", booking.price_per_person);
        cmd.Parameters.AddWithValue("@total_amount", booking.total_amount);
        cmd.Parameters.AddWithValue("@paid_amount", booking.paid_amount);
        cmd.Parameters.AddWithValue("@balance_amount", booking.balance_amount);
        cmd.Parameters.AddWithValue("@payment_method", booking.payment_method ?? "Cash");
        cmd.Parameters.AddWithValue("@created_at", booking.created_at?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@updated_at", booking.updated_at?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@status",
            string.IsNullOrWhiteSpace(booking.status)
            ? "active"
            : booking.status.ToLower());
        cmd.Parameters.AddWithValue("@room_number", booking.room_number ?? "");
        
        // Get current worker name for booked_by
        string workerName = LocalStorage.GetItem("username") ?? "Unknown";
        cmd.Parameters.AddWithValue("@booked_by", workerName);
        
        // balance_payment_method is empty until checkout completes with balance payment
        cmd.Parameters.AddWithValue("@balance_payment_method", "");
        
        cmd.ExecuteNonQuery();

        // Update worker summary for online bookings too
        string adminId = LocalStorage.GetItem("adminId") ?? "";
        if (!string.IsNullOrEmpty(booking.worker_id) && !string.IsNullOrEmpty(adminId))
        {
            UpdateWorkerSummaryOnBooking(booking.worker_id, adminId, booking);
            Logger.Log($"Worker summary updated (online booking) for worker {booking.worker_id}, admin {adminId}");
        }
    }

    // Get count of pending bookings to sync (both new and updated)
    public static async Task<int> GetPendingSyncCountAsync()
    {
        return await Task.Run(() =>
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            // Count both IsSynced = 0 (new/offline bookings) and IsSynced = 2 (updated bookings)
            string countQuery = "SELECT COUNT(*) FROM Bookings WHERE IsSynced = 0 OR IsSynced = 2";
            using var cmd = new SqliteCommand(countQuery, connection);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        });
    }

    // Online Syncing https://railway-api-worker.artechnology.pro/api/Booking/create
    public static async Task<int> SyncAllOfflineBookingsAsync(string apiUrl = "https://railway-api-worker.artechnology.pro/api/Booking/create", bool showMessages = true)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        string select = "SELECT * FROM Bookings WHERE IsSynced = 0";
        var bookings = new List<Booking1>();

        using (var cmd = new SqliteCommand(select, connection))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                bookings.Add(new Booking1
                {
                    booking_id = reader["booking_id"]?.ToString() ?? string.Empty,
                    worker_id = reader["worker_id"]?.ToString() ?? string.Empty,
                    guest_name = reader["guest_name"]?.ToString() ?? string.Empty,
                    phone_number = reader["phone_number"]?.ToString() ?? string.Empty   ,
                    number_of_persons = Convert.ToInt32(reader["number_of_persons"]),
                    booking_type = reader["booking_type"]?.ToString() ?? string.Empty,
                    total_hours = Convert.ToInt32(reader["total_hours"]),
                    booking_date = DateTime.Parse(reader["booking_date"]?.ToString() ?? string.Empty) ,
                    in_time = TimeSpan.Parse(reader["in_time"]?.ToString() ?? string.Empty) ,
                    out_time = TimeSpan.TryParse(reader["out_time"]?.ToString(), out var outT) ? outT : TimeSpan.Zero,
                    proof_type = reader["proof_type"]?.ToString() ?? string.Empty,
                    proof_id = reader["proof_id"]?.ToString() ?? string.Empty,
                    price_per_person = Convert.ToDecimal(reader["price_per_person"]),
                    total_amount = Convert.ToDecimal(reader["total_amount"]),
                    paid_amount = Convert.ToDecimal(reader["paid_amount"]),
                    balance_amount = Convert.ToDecimal(reader["balance_amount"]),
                    payment_method = reader["payment_method"]?.ToString() ?? string.Empty,
                    status = reader["status"]?.ToString() ?? string.Empty,
                });
            }
        }

        if (bookings.Count == 0)
        {
            return 0;
        }

        try
        {
            int batchSize = 50;
            int totalBookings = bookings.Count;
            int totalBatches = (int)Math.Ceiling(totalBookings / (double)batchSize);

            using var client = new HttpClient();

            for (int i = 0; i < totalBatches; i++)
            {
                var batch = bookings.Skip(i * batchSize).Take(batchSize).ToList();
                var json = JsonConvert.SerializeObject(batch);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response;

                try
                {
                    response = await client.PostAsync(apiUrl, content);
                }
                catch (HttpRequestException ex)
{
    string errorDetails = $"❌ Internet error while uploading batch {i + 1}/{totalBatches}.\n\n" +
                          $"Error Message: {ex.Message}";

    if (ex.InnerException != null)
        errorDetails += $"\nInner Exception: {ex.InnerException.Message}";

    errorDetails += "\n\nSync paused — please reconnect to internet and try again.";

    MessageBox.Show(errorDetails, "Network Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    break;
}
                catch (TaskCanceledException)
                {
                    MessageBox.Show($"Request timed out while uploading batch {i + 1}/{totalBatches}. " +
                        "Sync paused — please check your connection.",
                        "Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }

                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // Mark this batch as synced
                    string ids = string.Join(",", batch.Select(b => $"'{b.booking_id}'"));
                    string update = $"UPDATE Bookings SET IsSynced = 1 WHERE booking_id IN ({ids})";

                    using var cmd = new SqliteCommand(update, connection);
                    cmd.ExecuteNonQuery();

                    Logger.Log($"Batch {i + 1}/{totalBatches} uploaded successfully ({batch.Count} bookings)");
                }
                else
                {
                    Logger.Log($"Server rejected batch {i + 1}/{totalBatches} - Status: {response.StatusCode}");
                    if (showMessages)
                    {
                        // MessageBox.Show($"❌ Server rejected batch {i + 1}/{totalBatches}\n\nStatus: {response.StatusCode}\nResponse: {responseBody}",
                        //     "Sync Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    break; // stop syncing further
                }

                if (i < totalBatches - 1)
                {
                    await Task.Delay(5000); // wait before next batch
                }
            }

            Logger.Log($"Synced offline bookings process completed");
            return totalBookings;
        }
        catch (Exception ex)
        {
            if (showMessages)
            {
                MessageBox.Show($"Unexpected error during sync",
                    "Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Logger.Log($"Error syncing offline bookings: {ex.Message}");
            return 0;
        }
    }

    // Sync Updated Bookings (IsSynced = 2) - Only completion/payment updates
    public static async Task<int> SyncUpdatedBookingsAsync(string apiUrl = "https://railway-api-worker.artechnology.pro/api/Booking/checkout", bool showMessages = true)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        // Select only the fields needed for update sync
        string select = "SELECT booking_id, out_time, status, payment_method FROM Bookings WHERE IsSynced = 2";
        var updatedBookings = new List<dynamic>();

        using (var cmd = new SqliteCommand(select, connection))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                updatedBookings.Add(new
                {
                    booking_id = reader["booking_id"]?.ToString(),
                    out_time = reader["out_time"]?.ToString(),
                    status = reader["status"]?.ToString(),
                    payment_method = reader["payment_method"]?.ToString()
                });
            }
        }

        if (updatedBookings.Count == 0)
        {
            return 0;
        }

        try
        {
            int totalBookings = updatedBookings.Count;
            int successCount = 0;
            int failCount = 0;

            using var client = new HttpClient();

            // Process each booking individually
            for (int i = 0; i < updatedBookings.Count; i++)
            {
                var booking = updatedBookings[i];
                string bookingId = booking.booking_id;
                string outTimeStr = booking.out_time ?? "";
                string status = booking.status ?? "Completed";
                string paymentMethod = booking.payment_method ?? "cash";
                
                // Parse out_time string to TimeSpan
                TimeSpan outTime = TimeSpan.TryParse(outTimeStr, out var parsedTime) ? parsedTime : DateTime.Now.TimeOfDay;
                
                // Create checkout request matching backend CheckoutRequest model
                var checkoutRequest = new
                {
                    booking_id = bookingId,
                    out_time = outTime,  // Send as TimeSpan, not string
                    status = status,
                    payment_method = paymentMethod
                };
                
                var json = JsonConvert.SerializeObject(checkoutRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response;

                try
                {
                    response = await client.PutAsync(apiUrl, content);  // Use PUT as backend expects
                }
                catch (HttpRequestException ex)
                {
                    Logger.Log($"Network error syncing booking {bookingId}: {ex.Message}");
                    failCount++;
                    continue; // Continue with next booking
                }
                catch (TaskCanceledException)
                {
                    Logger.Log($"Timeout syncing booking {bookingId}");
                    failCount++;
                    continue; // Continue with next booking
                }

                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // Mark this booking as update synced (IsSynced = 3)
                    string update = $"UPDATE Bookings SET IsSynced = 3 WHERE booking_id = '{bookingId}'";

                    using var cmd = new SqliteCommand(update, connection);
                    cmd.ExecuteNonQuery();

                    successCount++;
                    Logger.Log($"Successfully synced booking {bookingId} ({i + 1}/{totalBookings})");
                }
                else
                {
                    Logger.Log($"Server rejected booking {bookingId} - Status: {response.StatusCode}, Response: {responseBody}");
                    failCount++;
                }

                // Small delay between requests
                if (i < updatedBookings.Count - 1)
                {
                    await Task.Delay(500); // 0.5 second delay
                }
            }

            // Log final summary
            Logger.Log($"Update sync completed - Success: {successCount}, Failed: {failCount}");
            return successCount;
        }
        catch (Exception ex)
        {
            if (showMessages)
            {
                MessageBox.Show($"Unexpected error during update sync",
                    "Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Logger.Log($"Error syncing updated bookings: {ex.Message}");
            return 0;
        }
    }

    // Sync a single booking update immediately (for real-time online sync)
    private static async Task<bool> SyncSingleBookingUpdateAsync(string bookingId, TimeSpan outTime, string status, string paymentMethod)
    {
        string apiUrl = "https://railway-api-worker.artechnology.pro/api/Booking/checkout";
        
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            // Create checkout request
            var checkoutRequest = new
            {
                booking_id = bookingId,
                out_time = outTime,  // Send as TimeSpan, not string
                status = status,
                payment_method = paymentMethod
            };

            var json = JsonConvert.SerializeObject(checkoutRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.PutAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                // Mark as fully synced (IsSynced = 3)
                string update = "UPDATE Bookings SET IsSynced = 3 WHERE booking_id = @id";
                using var cmd = new SqliteCommand(update, connection);
                cmd.Parameters.AddWithValue("@id", bookingId);
                cmd.ExecuteNonQuery();

                Logger.Log($"✅ Booking {bookingId} update synced successfully to cloud");
                return true;
            }
            else
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                Logger.Log($"❌ Failed to sync booking {bookingId} update: {response.StatusCode} - {responseBody}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"❌ Error syncing booking {bookingId} update: {ex.Message}");
            return false;
        }
    }

    public static List<Booking1> GetBasicBookings()
    {
        var bookings = new List<Booking1>();

        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string query = @"
            SELECT booking_id, guest_name, phone_number, booking_type, booking_date, in_time, out_time, status, created_at,
                   total_amount, paid_amount, balance_amount, worker_id, number_of_persons, total_hours, price_per_person, 
                   COALESCE(room_number, '') as room_number,
                   COALESCE(booked_by, '') as booked_by,
                   COALESCE(closed_by, '') as closed_by
            FROM Bookings
            ORDER BY created_at DESC;";

            using var cmd = new SqliteCommand(query, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                bookings.Add(new Booking1
                {
                    booking_id = reader["booking_id"]?.ToString() ?? string.Empty,
                    worker_id = reader["worker_id"]?.ToString() ?? string.Empty,
                    guest_name = reader["guest_name"]?.ToString() ?? string.Empty,
                    phone_number = reader["phone_number"]?.ToString() ?? string.Empty,
                    booking_type = reader["booking_type"]?.ToString() ?? string.Empty,
                    booking_date = DateTime.TryParse(reader["booking_date"]?.ToString(), out var bDate) ? bDate : DateTime.Today,
                    number_of_persons = Convert.ToInt32(reader["number_of_persons"] ?? 0),
                    total_hours = Convert.ToInt32(reader["total_hours"] ?? 0),
                    price_per_person = Convert.ToDecimal(reader["price_per_person"] ?? 0),
                    total_amount = Convert.ToDecimal(reader["total_amount"] ?? 0),
                    paid_amount = Convert.ToDecimal(reader["paid_amount"] ?? 0),
                    balance_amount = Convert.ToDecimal(reader["balance_amount"] ?? 0),
                    in_time = TimeSpan.TryParse(reader["in_time"]?.ToString(), out var inT) ? inT : TimeSpan.Zero,
                    out_time = TimeSpan.TryParse(reader["out_time"]?.ToString(), out var outT) ? outT : TimeSpan.Zero,
                    status = reader["status"]?.ToString() ?? string.Empty,
                    room_number = reader["room_number"]?.ToString(),
                    booked_by = reader["booked_by"]?.ToString() ?? "",
                    closed_by = reader["closed_by"]?.ToString() ?? "",
                    created_at = reader["created_at"] != DBNull.Value && DateTime.TryParse(reader["created_at"]?.ToString(), out var createdAt) 
                        ? createdAt 
                        : (DateTime?)null
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        return bookings;
    }

    // Sync completed bookings from server for today and current worker
    public static async Task<string> SyncCompletedBookingsFromServerAsync(string workerId)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            // Get admin ID from LocalStorage
            string adminId = LocalStorage.GetItem("adminId") ?? "";

            // Prepare POST request with JSON body (server uses CURRENT_DATE)
            var requestData = new
            {
                admin_id = adminId,
                worker_id = workerId
            };

            var json = JsonConvert.SerializeObject(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(GetCompletedBookingsApiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                return $"❌ Server error: {response.StatusCode}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<CompletedBookingsResponse>(responseJson);

            if (result?.completedBookingIds == null || result.completedBookingIds.Length == 0)
            {
                return "✅ All bookings are up to date";
            }

            // Update local database
            int updatedCount = 0;
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            foreach (var booking in result.completedBookingIds)
            {
                // Check if booking exists
                string checkQuery = "SELECT status, out_time FROM Bookings WHERE booking_id = @id";
                using var checkCmd = new SqliteCommand(checkQuery, connection);
                checkCmd.Parameters.AddWithValue("@id", booking.booking_id);
                
                using var reader = checkCmd.ExecuteReader();
                if (reader.Read())
                {
                    string currentStatus = reader["status"]?.ToString() ?? "";
                    string currentOutTime = reader["out_time"]?.ToString() ?? "";
                    reader.Close();

                    // Update if status is active OR if out_time is different
                    if (currentStatus.ToLower() == "active" || currentOutTime != booking.out_time)
                    {
                        string updateQuery = @"
                            UPDATE Bookings 
                            SET status = 'completed', 
                                out_time = @out_time,
                                IsSynced = 3,
                                updated_at = @updated_at 
                            WHERE booking_id = @id";

                        using var updateCmd = new SqliteCommand(updateQuery, connection);
                        updateCmd.Parameters.AddWithValue("@id", booking.booking_id);
                        updateCmd.Parameters.AddWithValue("@out_time", booking.out_time ?? "00:00:00");
                        updateCmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        updateCmd.ExecuteNonQuery();
                        updatedCount++;
                        
                        Logger.Log($"Booking {booking.booking_id} synced from server - marked as completed with IsSynced=3");
                    }
                }
            }

            return updatedCount > 0 
                ? $"✅ Synced! {updatedCount} booking(s) marked as completed" 
                : "✅ All bookings are up to date";
        }
        catch (HttpRequestException)
        {
            return "❌ No internet connection";
        }
        catch (TaskCanceledException)
        {
            return "❌ Request timeout. Please try again";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            return $"❌ Sync failed: {ex.Message}";
        }
    }

    // Response model for completed bookings API
    private class CompletedBookingsResponse
    {
        public required CompletedBookingItem[] completedBookingIds { get; set; }
    }

    private class CompletedBookingItem
    {
        public required string booking_id { get; set; }
        public required string out_time { get; set; }
    }

    public static Booking1? GetBookingById(string bookingId)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        string query = @"
        SELECT * FROM Bookings WHERE booking_id = @id";

        using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@id", bookingId);
        
        using var reader = cmd.ExecuteReader();
        
        if (reader.Read())
        {
            return new Booking1
            {
                booking_id = reader["booking_id"]?.ToString() ?? string.Empty,
                worker_id = reader["worker_id"]?.ToString() ?? string.Empty,
                guest_name = reader["guest_name"]?.ToString() ?? string.Empty,
                phone_number = reader["phone_number"]?.ToString() ?? string.Empty,
                number_of_persons = Convert.ToInt32(reader["number_of_persons"] ?? 0),
                booking_type = reader["booking_type"]?.ToString() ?? string.Empty,
                total_hours = Convert.ToInt32(reader["total_hours"] ?? 0),
                booking_date = DateTime.TryParse(reader["booking_date"]?.ToString(), out var date) ? date : DateTime.Now,
                in_time = TimeSpan.TryParse(reader["in_time"]?.ToString(), out var inT) ? inT : TimeSpan.Zero,
                out_time = TimeSpan.TryParse(reader["out_time"]?.ToString(), out var outT) ? outT : null,
                proof_type = reader["proof_type"]?.ToString() ?? string.Empty,
                proof_id = reader["proof_id"]?.ToString() ?? string.Empty,
                price_per_person = Convert.ToDecimal(reader["price_per_person"] ?? 0),
                total_amount = Convert.ToDecimal(reader["total_amount"] ?? 0),
                paid_amount = Convert.ToDecimal(reader["paid_amount"] ?? 0),
                balance_amount = Convert.ToDecimal(reader["balance_amount"] ?? 0),
                payment_method = reader["payment_method"]?.ToString() ?? string.Empty,
                status = reader["status"]?.ToString() ?? string.Empty,
                IsSynced = Convert.ToInt32(reader["IsSynced"] ?? 0)
            };
        }

        return null;
    }

    // Method to fetch and display all booking data from local database for debugging
    public static void ShowAllBookingsData()
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string query = @"
            SELECT booking_id, guest_name, phone_number, booking_type, status, 
                   total_amount, paid_amount, balance_amount, payment_method,
                   in_time, out_time, total_hours, created_at, IsSynced
            FROM Bookings 
            ORDER BY created_at DESC";

            using var cmd = new SqliteCommand(query, connection);
            using var reader = cmd.ExecuteReader();

            var bookingData = "📋 LOCAL DATABASE BOOKINGS:\n" +
                            "================================================\n\n";

            int count = 0;
            while (reader.Read())
            {
                count++;
                bookingData += $"🔹 Booking #{count}\n";
                bookingData += $"   ID: {reader["booking_id"]}\n";
                bookingData += $"   Name: {reader["guest_name"]}\n";
                bookingData += $"   Phone: {reader["phone_number"]}\n";
                bookingData += $"   Type: {reader["booking_type"]}\n";
                bookingData += $"   Status: {reader["status"]}\n";
                bookingData += $"   Total Amount: ₹{reader["total_amount"]}\n";
                bookingData += $"   Paid Amount: ₹{reader["paid_amount"]}\n";
                bookingData += $"   Balance: ₹{reader["balance_amount"]}\n";
                bookingData += $"   Payment Method: {reader["payment_method"]}\n";
                bookingData += $"   In Time: {reader["in_time"]}\n";
                bookingData += $"   Out Time: {reader["out_time"]}\n";
                bookingData += $"   Total Hours: {reader["total_hours"]}\n";
                bookingData += $"   Created: {reader["created_at"]}\n";
                bookingData += $"   Synced: {(Convert.ToInt32(reader["IsSynced"]) == 1 ? "Yes" : "No")}\n";
                bookingData += "\n" + new string('-', 50) + "\n\n";
            }

            if (count == 0)
            {
                bookingData += "❌ No bookings found in local database.\n";
            }
            else
            {
                bookingData += $"📊 Total Bookings: {count}\n";
                bookingData += $"💾 Database Path: {DbPath}";
            }

            // MessageBox.Show(bookingData, "Local Database Data", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            // MessageBox.Show($"❌ Error reading database:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static async Task SyncSingleBookingAsync(string bookingId)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string select = "SELECT * FROM Bookings WHERE booking_id = @id";
            Booking1? booking = null;

            using (var cmd = new SqliteCommand(select, connection))
            {
                cmd.Parameters.AddWithValue("@id", bookingId);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        booking = new Booking1
                        {
                            booking_id = reader["booking_id"]?.ToString() ?? string.Empty,
                            worker_id = reader["worker_id"]?.ToString() ?? string.Empty,
                            guest_name = reader["guest_name"]?.ToString() ?? string.Empty,
                            phone_number = reader["phone_number"]?.ToString() ?? string.Empty,
                            number_of_persons = Convert.ToInt32(reader["number_of_persons"]) ,
                            booking_type = reader["booking_type"]?.ToString() ?? string.Empty,
                            total_hours = Convert.ToInt32(reader["total_hours"]),
                            booking_date = DateTime.Parse(reader["booking_date"].ToString() ?? string.Empty),
                            in_time = TimeSpan.Parse(reader["in_time"].ToString() ?? string.Empty),
                            out_time = TimeSpan.TryParse(reader["out_time"]?.ToString(), out var outT) ? outT : TimeSpan.Zero,
                            proof_type = reader["proof_type"]?.ToString() ?? string.Empty,
                            proof_id = reader["proof_id"]?.ToString() ?? string.Empty,
                            price_per_person = Convert.ToDecimal(reader["price_per_person"]),
                            total_amount = Convert.ToDecimal(reader["total_amount"]),
                            paid_amount = Convert.ToDecimal(reader["paid_amount"]),
                            balance_amount = Convert.ToDecimal(reader["balance_amount"]),
                            payment_method = reader["payment_method"]?.ToString() ?? string.Empty,
                            status = reader["status"]?.ToString() ?? string.Empty,
                        };
                    }
                }
            }

            if (booking == null)
            {
                MessageBox.Show($"No completed booking found for ID: {bookingId}", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 🔹 API endpoint
            string apiUrl = "https://railway-api-worker.artechnology.pro/api/Booking/online-book";

            // 🔹 Convert booking object to JSON
            string json = JsonConvert.SerializeObject(booking);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var response = await client.PostAsync(apiUrl, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // Mark as synced in local DB
                string update = "UPDATE Bookings SET IsSynced = 1 WHERE booking_id = @id";
                using var updateCmd = new SqliteCommand(update, connection);
                updateCmd.Parameters.AddWithValue("@id", bookingId);
                updateCmd.ExecuteNonQuery();

                MessageBox.Show("✅ Booking synced successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                
                // MessageBox.Show($"❌ Server rejected booking.\nStatus: {response.StatusCode}\nResponse: {responseBody}",
                //     "Sync Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            // MessageBox.Show($"❌ Error during sync:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    

    // Method to update booking with payment details and completion info
    public static async Task<string> CompleteBookingWithPaymentAsync(
    string bookingId,
    decimal balancePaymentAmount,
    decimal totalAmount,
    decimal extraCharges,
    string paymentMethod,
    TimeSpan outTime)
{
    try
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        await connection.OpenAsync();

        // 🔹 Step 1: Check if booking exists and get initial values BEFORE update
        string select = "SELECT status, total_amount, IsSynced, in_time, booking_date, paid_amount, worker_id, booking_type FROM Bookings WHERE booking_id = @id";
        TimeSpan inTime = TimeSpan.Zero;
        DateTime bookingDate = DateTime.Today;
        decimal initialPaidAmount = 0;
        decimal initialTotalAmount = 0;
        string? workerId = null;
        string? bookingType = null;
        
        using (var checkCmd = new SqliteCommand(select, connection))
        {
            checkCmd.Parameters.AddWithValue("@id", bookingId);
            using var reader = await checkCmd.ExecuteReaderAsync();

            if (!reader.Read())
            {
                return $"❌ No booking found with ID: {bookingId}";
            }

            string currentStatus = reader["status"]?.ToString()?.ToLower() ?? "";
            int isSynced = Convert.ToInt32(reader["IsSynced"]);
            
            // Store initial values before update
            initialPaidAmount = Convert.ToDecimal(reader["paid_amount"]);
            initialTotalAmount = Convert.ToDecimal(reader["total_amount"]);
            workerId = reader["worker_id"]?.ToString();
            bookingType = reader["booking_type"]?.ToString();
            
            // Parse booking_date
            if (DateTime.TryParse(reader["booking_date"]?.ToString(), out DateTime parsedDate))
            {
                bookingDate = parsedDate;
            }
            
            // Parse in_time
            if (TimeSpan.TryParse(reader["in_time"]?.ToString(), out TimeSpan parsedInTime))
            {
                inTime = parsedInTime;
            }

            if (currentStatus == "completed")
            {
                return $"⚠️ Booking ID {bookingId} is already completed.";
            }

            reader.Close();

            // 🔹 Step 2: Calculate actual total hours from in_time to out_time
            // Build full DateTime from booking_date + in_time
            DateTime inDateTime  = bookingDate.Date + inTime;
            DateTime outDateTime = bookingDate.Date + outTime;

            // If out time is earlier than in time, assume next-day checkout
            if (outTime < inTime)
            {
                outDateTime = outDateTime.AddDays(1);
            }

            // Calculate exact duration
            TimeSpan duration = outDateTime - inDateTime;

            // Total hours (decimal)
            double totalHoursExact = duration.TotalHours;

            // Round up minimum 1 hour
            int actualTotalHours = Math.Max(1, (int)Math.Ceiling(totalHoursExact));
            
            // Keep paid_amount as the initial payment only
            // balance_amount = total balance collected at closure (unpaid balance + extra charges)
            decimal finalPaidAmount = initialPaidAmount;  // Keep original paid amount
            decimal balanceAmount = balancePaymentAmount;  // Total balance collected at checkout
            
            // Save the full payment method name to balance_payment_method (same as payment_method)
            string balancePaymentMethod = paymentMethod;
            
            string updateQuery;

            if (isSynced == 0)
            {
                // Only update the booking details, don't touch IsSynced
                updateQuery = @"
                    UPDATE Bookings 
                    SET status = 'completed',
                        total_hours = @total_hours,
                        paid_amount = @paid_amount,
                        total_amount = @total_amount,
                        balance_amount = @balance_amount,
                        payment_method = @payment_method,
                        balance_payment_method = @balance_payment_method,
                        out_time = @out_time,
                        updated_at = @updated_at,
                        closed_by = @closed_by
                    WHERE booking_id = @booking_id";
            }
            else if (isSynced == 1)
            {
                // Update booking details and set IsSynced = 2
                updateQuery = @"
                    UPDATE Bookings 
                    SET status = 'completed',
                        total_hours = @total_hours,
                        paid_amount = @paid_amount,
                        total_amount = @total_amount,
                        balance_amount = @balance_amount,
                        payment_method = @payment_method,
                        balance_payment_method = @balance_payment_method,
                        out_time = @out_time,
                        updated_at = @updated_at,
                        IsSynced = 2,
                        closed_by = @closed_by
                    WHERE booking_id = @booking_id";
            }
            else
            {
                return $"⚠️ Invalid IsSynced value ({isSynced}) for booking {bookingId}.";
            }

            using (var updateCmd = new SqliteCommand(updateQuery, connection))
            {
                updateCmd.Parameters.AddWithValue("@booking_id", bookingId);
                updateCmd.Parameters.AddWithValue("@total_hours", actualTotalHours);
                updateCmd.Parameters.AddWithValue("@paid_amount", finalPaidAmount);
                updateCmd.Parameters.AddWithValue("@total_amount", totalAmount);
                updateCmd.Parameters.AddWithValue("@balance_amount", balanceAmount);
                updateCmd.Parameters.AddWithValue("@payment_method", paymentMethod);
                updateCmd.Parameters.AddWithValue("@balance_payment_method", balancePaymentMethod);
                
                // Get current worker name for closed_by
                string closedByWorker = LocalStorage.GetItem("username") ?? "Unknown";
                updateCmd.Parameters.AddWithValue("@closed_by", closedByWorker);
                updateCmd.Parameters.AddWithValue("@out_time", outTime.ToString());
                updateCmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await updateCmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    Logger.Log($"Booking {bookingId} completed with payment: {paymentMethod}, Total: ₹{totalAmount}, Balance: ₹{balanceAmount}, IsSynced: {isSynced}");
                    
                    // Update worker summary for the CLOSING worker (currently logged-in worker)
                    string closingWorkerId = LocalStorage.GetItem("workerId") ?? "";
                    string adminId = LocalStorage.GetItem("adminId") ?? "";
                    
                    if (!string.IsNullOrEmpty(closingWorkerId))
                    {
                        // The balancePaymentAmount parameter is the balance being paid NOW (extra charges)
                        decimal balancePaid = balancePaymentAmount;
                        
                        // Calculate extra charges (difference between final total and initial total)
                        decimal calculatedExtraCharges = totalAmount - initialTotalAmount;
                        
                        if (balancePaid > 0 || calculatedExtraCharges > 0)
                        {
                            // Update the CLOSING worker's summary (not the booking creator's)
                            int sNo = GetOrCreateActiveWorkerSummary(closingWorkerId, adminId);
                            if (sNo != -1)
                            {
                                bool isSitting = bookingType?.ToLower() == "sitting";
                                bool isSleeping = bookingType?.ToLower() == "sleeper" || bookingType?.ToLower() == "sleeping";
                                bool isCash = paymentMethod?.ToLower() == "cash";
                                bool isUpi = paymentMethod?.ToLower() == "upi" || paymentMethod?.ToLower() == "online";
                                            
                                string balanceUpdateQuery = @"
                                    UPDATE workers_summary
                                    SET
                                        sitting_booking_total_amount = sitting_booking_total_amount + @sitting_amount,
                                        sleeping_total_amount = sleeping_total_amount + @sleeping_amount,
                                        in_cash_collect = in_cash_collect + @cash_amount,
                                        in_upi_collect = in_upi_collect + @upi_amount,
                                        total_balance = total_balance - @balance_paid,
                                        extra_charges = extra_charges + @extra_charges,
                                        last_updated_timestamp = @updated_time,
                                        is_synced = 0
                                    WHERE s_no = @s_no";
                                
                                using (var balanceUpdateCmd = new SqliteCommand(balanceUpdateQuery, connection))
                                {
                                    // Add balance_amount to revenue (the amount paid at checkout by closing worker)
                                    balanceUpdateCmd.Parameters.AddWithValue("@sitting_amount", isSitting ? balancePaid : 0);
                                    balanceUpdateCmd.Parameters.AddWithValue("@sleeping_amount", isSleeping ? balancePaid : 0);
                                    balanceUpdateCmd.Parameters.AddWithValue("@cash_amount", isCash ? balancePaid : 0);
                                    balanceUpdateCmd.Parameters.AddWithValue("@upi_amount", isUpi ? balancePaid : 0);
                                    balanceUpdateCmd.Parameters.AddWithValue("@balance_paid", balancePaid);
                                    balanceUpdateCmd.Parameters.AddWithValue("@extra_charges", calculatedExtraCharges);
                                    balanceUpdateCmd.Parameters.AddWithValue("@updated_time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                                    balanceUpdateCmd.Parameters.AddWithValue("@s_no", sNo);
                                    
                                    await balanceUpdateCmd.ExecuteNonQueryAsync();
                                    Logger.Log($"Worker summary updated for closing worker {closingWorkerId} - Balance added: ₹{balancePaid}, Extra charges: ₹{calculatedExtraCharges}, Method: {paymentMethod}");
                                }
                            }
                        }
                    }
                    
                    // If online and booking was previously synced, immediately sync the update
                    if (isSynced == 1 && NetworkInterface.GetIsNetworkAvailable())
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await SyncSingleBookingUpdateAsync(bookingId, outTime, "completed", paymentMethod ?? "Cash");
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"Background sync failed for booking {bookingId}: {ex.Message}");
                            }
                        });
                    }
                    
                    return $"✅ Booking completed successfully!\n💰 Total: ₹{totalAmount}\n💳 Payment: {paymentMethod}\n⏰ Out Time: {outTime:hh\\:mm}";
                }
                else
                {
                    return $"⚠️ Could not complete booking for ID: {bookingId}";
                }
            }
        }
    }
    catch (Exception ex)
    {
        Logger.LogError(ex);
        return $"❌ Error completing booking: {ex.Message}";
    }
}

public static void AfterSavedOffline(Booking1 booking)
    {
        if (booking == null) return;

        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        string insert = @"
            INSERT OR REPLACE INTO Bookings
            (booking_id, worker_id, guest_name, phone_number, number_of_persons, booking_type, total_hours,
            booking_date, in_time, out_time, proof_type, proof_id, price_per_person, total_amount, paid_amount,
            balance_amount, payment_method, created_at, updated_at, status, IsSynced)
            VALUES (@booking_id, @worker_id, @guest_name, @phone_number, @number_of_persons, @booking_type, @total_hours,
            @booking_date, @in_time, @out_time, @proof_type, @proof_id, @price_per_person, @total_amount, 
            @paid_amount, @balance_amount, @payment_method, @created_at, @updated_at, @status, 1);";

        using var cmd = new SqliteCommand(insert, connection);
        cmd.Parameters.AddWithValue("@booking_id", booking.booking_id);
        cmd.Parameters.AddWithValue("@worker_id", booking.worker_id ?? "");
        cmd.Parameters.AddWithValue("@guest_name", booking.guest_name ?? "");
        cmd.Parameters.AddWithValue("@phone_number", booking.phone_number ?? "");
        cmd.Parameters.AddWithValue("@number_of_persons", booking.number_of_persons);
        cmd.Parameters.AddWithValue("@booking_type", booking.booking_type ?? "");
        cmd.Parameters.AddWithValue("@total_hours", booking.total_hours);
        cmd.Parameters.AddWithValue("@booking_date", booking.booking_date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@in_time", booking.in_time.ToString());
        cmd.Parameters.AddWithValue("@out_time", booking.out_time?.ToString() ?? "");
        cmd.Parameters.AddWithValue("@proof_type", booking.proof_type ?? "");
        cmd.Parameters.AddWithValue("@proof_id", booking.proof_id ?? "");
        cmd.Parameters.AddWithValue("@price_per_person", booking.price_per_person);
        cmd.Parameters.AddWithValue("@total_amount", booking.total_amount);
        cmd.Parameters.AddWithValue("@paid_amount", booking.paid_amount);
        cmd.Parameters.AddWithValue("@balance_amount", booking.balance_amount);
        cmd.Parameters.AddWithValue("@payment_method", booking.payment_method ?? "Cash");
        cmd.Parameters.AddWithValue("@created_at", booking.created_at?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@updated_at", booking.updated_at?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@status",
            string.IsNullOrWhiteSpace(booking.status)
            ? "active"
            : booking.status.ToLower());

        cmd.ExecuteNonQuery();
        Logger.Log($"Synced booking saved: {booking.booking_id}");
    }

    // Method to mark booking as completed
    public static async Task<string> MarkBookingAsCompletedAsync(string bookingId)
{
    try
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        await connection.OpenAsync();

            // 🔹 Step 1: Check if booking exists and get current status + IsSync
            string select = "SELECT status, IsSynced FROM Bookings WHERE booking_id = @id";
        using (var checkCmd = new SqliteCommand(select, connection))
        {
            checkCmd.Parameters.AddWithValue("@id", bookingId);
            using var reader = await checkCmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return $"❌ No booking found with ID: {bookingId}";
            }

            string status = reader["status"].ToString()?.ToLower() ?? "";
            int isSync = Convert.ToInt32(reader["IsSynced"]);

            if (status == "completed")
            {
                return $"⚠️ Booking ID {bookingId} is already marked as completed.";
            }

            // 🔹 Step 2: Build the update query dynamically
            string update;
            if (isSync == 1)
                update = "UPDATE Bookings SET status = 'completed', IsSynced = 2 WHERE booking_id = @id";
            else
                update = "UPDATE Bookings SET status = 'completed' WHERE booking_id = @id";

            using (var updateCmd = new SqliteCommand(update, connection))
            {
                updateCmd.Parameters.AddWithValue("@id", bookingId);
                int rowsAffected = await updateCmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    Logger.Log($"Booking {bookingId} marked as completed");
                    return $"✅ Booking ID {bookingId} marked as completed successfully!";
                }
                else
                {
                    return $"⚠️ Could not update booking status for ID: {bookingId}.";
                }
            }
        }
    }
    catch (Exception ex)
    {
        Logger.LogError(ex);
        return $"❌ Error while updating booking status: {ex.Message}";
    }
}

    public static async Task<string> CompleteBookingWithOvertimeAsync(
        string bookingId,
        TimeSpan outTime,
        decimal overtimeCharges,
        decimal finalBalance,
        string paymentMethod)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            await connection.OpenAsync();

            // Check if booking exists and get current status + IsSynced
            string select = "SELECT status, IsSynced FROM Bookings WHERE booking_id = @id";
            using (var checkCmd = new SqliteCommand(select, connection))
            {
                checkCmd.Parameters.AddWithValue("@id", bookingId);
                using var reader = await checkCmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return $"❌ No booking found with ID: {bookingId}";
                }

                string status = reader["status"].ToString()?.ToLower() ?? "";
                int isSync = Convert.ToInt32(reader["IsSynced"]);

                if (status == "completed")
                {
                    return $"⚠️ Booking ID {bookingId} is already marked as completed.";
                }

                // Update booking with out_time, balance_amount, payment_method, and status
                string update;
                if (isSync == 1)
                {
                    update = @"UPDATE Bookings SET 
                              out_time = @outTime, 
                              balance_amount = @balance, 
                              payment_method = @payment,
                              status = 'completed', 
                              IsSynced = 2 
                              WHERE booking_id = @id";
                }
                else
                {
                    update = @"UPDATE Bookings SET 
                              out_time = @outTime, 
                              balance_amount = @balance, 
                              payment_method = @payment,
                              status = 'completed' 
                              WHERE booking_id = @id";
                }

                using (var updateCmd = new SqliteCommand(update, connection))
                {
                    updateCmd.Parameters.AddWithValue("@id", bookingId);
                    updateCmd.Parameters.AddWithValue("@outTime", outTime.ToString());
                    updateCmd.Parameters.AddWithValue("@balance", finalBalance);
                    updateCmd.Parameters.AddWithValue("@payment", paymentMethod);
                    
                    int rowsAffected = await updateCmd.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        Logger.Log($"Booking {bookingId} completed with overtime charges: ₹{overtimeCharges}");
                        return $"✅ Booking ID {bookingId} completed successfully!";
                    }
                    else
                    {
                        return $"⚠️ Could not update booking for ID: {bookingId}.";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            return $"❌ Error completing booking: {ex.Message}";
        }
    }

    public static async Task<bool> FetchAndSaveWorkerSettingsAsync(string adminId, string apiUrl = "https://railway-api-worker.artechnology.pro/api/Settings/hall-types")
    {
        try
        {
            using var client = new HttpClient();
            
            // Make API call to get hall types (booking types) settings
            string fullUrl = $"{apiUrl}/{adminId}";
            
            //Console.WriteLine("\n=== FETCHING WORKER SETTINGS ===");
            //Console.WriteLine($"Admin ID: {adminId}");
            //Console.WriteLine($"API URL: {fullUrl}");
            
            var response = await client.GetAsync(fullUrl);
            
            //Console.WriteLine($"Response Status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                //Console.WriteLine($"ERROR: Failed to fetch settings");
                //Console.WriteLine($"Error Body: {errorBody}");
                
                MessageBox.Show($"Failed to fetch hall types settings.\n\n" +
                    $"Status Code: {response.StatusCode}\n" +
                    $"Reason: {response.ReasonPhrase}\n\n" +
                    $"API URL: {fullUrl}\n\n" +
                    $"Response: {errorBody}", 
                    "API Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            
            //Console.WriteLine("\n=== RAW SETTINGS RESPONSE ===");
            //Console.WriteLine(jsonResponse);
            
            // Show raw JSON response in MessageBox
            // MessageBox.Show($"📥 Raw API Response:\n\n{jsonResponse}", 
            //     "API Data Received", MessageBoxButton.OK, MessageBoxImage.Information);
            
            var hallTypesResponse = JsonConvert.DeserializeObject<HallTypesResponse>(jsonResponse);

            if (hallTypesResponse == null)
            {
                MessageBox.Show($"Invalid response from server.\n\n" +
                    $"Response received:\n{jsonResponse}", 
                    "Deserialization Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            //Console.WriteLine("\n=== PARSED SETTINGS DATA ===");
            //Console.WriteLine($"Type 1: {hallTypesResponse.Type1 ?? "NULL"}");
            //Console.WriteLine($"Type 1 Amount: ₹{hallTypesResponse.Type1Amount?.ToString() ?? "NULL"}");
            //Console.WriteLine($"Type 2: {hallTypesResponse.Type2 ?? "NULL"}");
            //Console.WriteLine($"Grace Amount: ₹{hallTypesResponse.GraceAmount?.ToString() ?? "NULL"}");
            //Console.WriteLine($"Grace Amount Type 2: ₹{hallTypesResponse.GraceAmountType2?.ToString() ?? "NULL"}");
            //Console.WriteLine($"Advance Payment Enabled: {hallTypesResponse.AdvancePaymentEnabled}");
            //Console.WriteLine($"Advance Payment %: {hallTypesResponse.AdvancePayment?.ToString() ?? "NULL"}");

            // Save to local database (save types + advance settings)
            SaveSettings(adminId, hallTypesResponse);
            
            //Console.WriteLine("\nSettings saved to local database successfully!");
            //Console.WriteLine("====================================\n");

            // Build types info display
            var typesInfo = new List<string>();
            if (!string.IsNullOrEmpty(hallTypesResponse.Type1) && hallTypesResponse.Type1Amount.HasValue)
                typesInfo.Add($"• {hallTypesResponse.Type1}: ₹{hallTypesResponse.Type1Amount.Value}");
            if (!string.IsNullOrEmpty(hallTypesResponse.Type2))
            {
                if (hallTypesResponse.GraceAmount.HasValue)
                    typesInfo.Add($"• {hallTypesResponse.Type2}: ₹{hallTypesResponse.GraceAmount.Value} (Grace Amount)");
                if (hallTypesResponse.GraceAmountType2.HasValue)
                    typesInfo.Add($"  - Grace Amount Type 2: ₹{hallTypesResponse.GraceAmountType2.Value}");
            }
            
            string typesDisplay = typesInfo.Count > 0 ? string.Join("\n", typesInfo) : "No types configured";
            string advanceDisplay = hallTypesResponse.AdvancePayment.HasValue 
                ? $"{hallTypesResponse.AdvancePayment.Value}%" 
                : "Not Set";
            
            // MessageBox.Show($"✅ Settings Loaded Successfully!\n\n" +
            //     $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            //     $"Booking Types ({typesInfo.Count}):\n{typesDisplay}\n\n" +
            //     $"Advance Payment: {(hallTypesResponse.AdvancePaymentEnabled ? "Enabled" : "Disabled")}\n" +
            //     $"Default Advance: {advanceDisplay}", 
            //     "Settings Loaded", MessageBoxButton.OK, MessageBoxImage.Information);

            return true;
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show($"❌ Network Error\n\n" +
                $"Message: {ex.Message}\n\n" +
                $"Details: {ex.InnerException?.Message ?? "No additional details"}\n\n" +
                $"Please check your internet connection and ensure the API is running.", 
                "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            Logger.LogError(ex);
            return false;
        }
        catch (JsonException ex)
        {
            MessageBox.Show($"❌ JSON Parse Error\n\n" +
                $"Message: {ex.Message}\n\n" +
                $"The API response format doesn't match the expected structure.\n" +
                $"Check the API documentation or logs for details.", 
                "Data Format Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.LogError(ex);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"❌ Unexpected Error\n\n" +
                $"Error Type: {ex.GetType().Name}\n" +
                $"Message: {ex.Message}\n\n" +
                $"Stack Trace:\n{ex.StackTrace}", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.LogError(ex);
            return false;
        }
    }

    private static void SaveSettings(string adminId, HallTypesResponse response)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        // Clear existing settings
        string deleteQuery = "DELETE FROM Settings";
        using (var deleteCmd = new SqliteCommand(deleteQuery, connection))
        {
            deleteCmd.ExecuteNonQuery();
        }

        // Prepare insert query (store 2 types, advance settings, and grace times)
        string insertQuery = @"
            INSERT INTO Settings (admin_id, type_1, type_1_amount, type_2, type_2_amount, 
                                 advance_payment_enabled, default_advance_percentage, 
                                 grace_time_type_1, grace_time_type_2, last_synced)
            VALUES (@admin_id, @type_1, @type_1_amount, @type_2, @type_2_amount, 
                   @advance_payment_enabled, @default_advance_percentage, 
                   @grace_time_type_1, @grace_time_type_2, @last_synced)";

        using var cmd = new SqliteCommand(insertQuery, connection);
        cmd.Parameters.AddWithValue("@admin_id", adminId);

        // Add type_1 and type_1_amount
        cmd.Parameters.AddWithValue("@type_1", response.Type1 ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@type_1_amount", response.Type1Amount ?? (object)DBNull.Value);

        // Add type_2 and grace_amount (as type_2_amount)
        cmd.Parameters.AddWithValue("@type_2", response.Type2 ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@type_2_amount", response.GraceAmount ?? (object)DBNull.Value);

        cmd.Parameters.AddWithValue("@advance_payment_enabled", response.AdvancePaymentEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@default_advance_percentage", response.AdvancePayment ?? (object)DBNull.Value);
        
        // Add grace time parameters (use API values or default to 10 minutes)
        cmd.Parameters.AddWithValue("@grace_time_type_1", response.GraceTimeType1 ?? 10);
        cmd.Parameters.AddWithValue("@grace_time_type_2", response.GraceTimeType2 ?? 10);
        
        cmd.Parameters.AddWithValue("@last_synced", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();

        int type1Exists = !string.IsNullOrEmpty(response.Type1) ? 1 : 0;
        int type2Exists = !string.IsNullOrEmpty(response.Type2) ? 1 : 0;
        int totalTypes = type1Exists + type2Exists;
        
        Logger.Log($"Settings saved for admin_id: {adminId} with {totalTypes} hall types, advance_enabled={response.AdvancePaymentEnabled}, default_adv_pct={response.AdvancePayment ?? 0}");
    }

    private static void DeleteExpiredSettings()
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            // Calculate the cutoff time (8 hours ago)
            DateTime cutoffTime = DateTime.Now.AddHours(-8);
            string cutoffTimeStr = cutoffTime.ToString("yyyy-MM-dd HH:mm:ss");

            string deleteQuery = "DELETE FROM Settings WHERE last_synced < @cutoff_time";
            using var cmd = new SqliteCommand(deleteQuery, connection);
            cmd.Parameters.AddWithValue("@cutoff_time", cutoffTimeStr);

            int deletedRows = cmd.ExecuteNonQuery();
            if (deletedRows > 0)
            {
                Logger.Log($"Deleted {deletedRows} expired settings (older than 8 hours)");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    // Fetch and save printer details from API
    public static async Task<bool> FetchAndSavePrinterDetailsAsync(string adminId, string apiUrl = "https://railway-api-worker.artechnology.pro/api/Settings/printer-details")
    {
        try
        {
            using var client = new HttpClient();
            
            string fullUrl = $"{apiUrl}/{adminId}";
            
            //Console.WriteLine("\n=== FETCHING PRINTER DETAILS ===");
            //Console.WriteLine($"Admin ID: {adminId}");
            //Console.WriteLine($"API URL: {fullUrl}");
            
            var response = await client.GetAsync(fullUrl);
            
            //Console.WriteLine($"Response Status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Log($"Failed to fetch printer details: {response.StatusCode}");
                //Console.WriteLine("ERROR: Failed to fetch printer details");
                return false;
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            
            //Console.WriteLine("\n=== RAW PRINTER DETAILS RESPONSE ===");
            //Console.WriteLine(jsonResponse);
            
            var printerDetails = JsonConvert.DeserializeObject<PrinterDetailsResponse>(jsonResponse);

            if (printerDetails == null)
            {
                Logger.Log("Invalid printer details response from server");
                //Console.WriteLine("ERROR: Invalid printer details response");
                return false;
            }

            //Console.WriteLine("\n=== PARSED PRINTER DETAILS ===");
            //Console.WriteLine($"Heading 1: {printerDetails.Heading1 ?? "NULL"}");
            //Console.WriteLine($"Heading 2: {printerDetails.Heading2 ?? "NULL"}");
            //Console.WriteLine($"Info 1: {printerDetails.Info1 ?? "NULL"}");
            //Console.WriteLine($"Info 2: {printerDetails.Info2 ?? "NULL"}");
            //Console.WriteLine($"Note: {printerDetails.Note ?? "NULL"}");
            //Console.WriteLine($"Hall Name: {printerDetails.HallName ?? "NULL"}");
            //Console.WriteLine($"Logo URL: {printerDetails.LogoUrl ?? "NULL"}");

            // Save to LocalStorage (valid for 8 hours)
            LocalStorage.SetItem("printerHeading1", printerDetails.Heading1 ?? "", TimeSpan.FromHours(8));
            LocalStorage.SetItem("printerHeading2", printerDetails.Heading2 ?? "", TimeSpan.FromHours(8));
            LocalStorage.SetItem("printerInfo1", printerDetails.Info1 ?? "", TimeSpan.FromHours(8));
            LocalStorage.SetItem("printerInfo2", printerDetails.Info2 ?? "", TimeSpan.FromHours(8));
            LocalStorage.SetItem("printerNote", printerDetails.Note ?? "", TimeSpan.FromHours(8));
            LocalStorage.SetItem("hallName", printerDetails.HallName ?? "", TimeSpan.FromHours(8));
            LocalStorage.SetItem("printerLogoUrl", printerDetails.LogoUrl ?? "", TimeSpan.FromHours(8));

            //Console.WriteLine("\nPrinter details saved to LocalStorage successfully!");
            //Console.WriteLine("====================================\n");

            Logger.Log($"Printer details saved for hall: {printerDetails.HallName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            //Console.WriteLine($"ERROR: Exception while fetching printer details - {ex.Message}");
            return false;
        }
    }

    // Fetch Type2 (sleeping) details from API
    public static async Task<List<Type2Detail>> FetchType2DetailsAsync(string adminId, string apiUrl = "https://railway-api-worker.artechnology.pro/api/Settings/sleeping-details")
    {
        try
        {
            using var client = new HttpClient();
            
            string fullUrl = $"{apiUrl}/{adminId}";
            
            //Console.WriteLine("\n=== FETCHING HOURLY PRICING (Type2 Details) ===");
            //Console.WriteLine($"Admin ID: {adminId}");
            //Console.WriteLine($"API URL: {fullUrl}");
            
            var response = await client.GetAsync(fullUrl);
            
            //Console.WriteLine($"Response Status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Log($"Failed to fetch Type2 details: {response.StatusCode}");
                //Console.WriteLine("ERROR: Failed to fetch hourly pricing details");
                return new List<Type2Detail>();
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            
            //Console.WriteLine("\n=== RAW HOURLY PRICING RESPONSE ===");
            //Console.WriteLine(jsonResponse);
            
            var type2Details = JsonConvert.DeserializeObject<List<Type2Detail>>(jsonResponse);

            if (type2Details == null || type2Details.Count == 0)
            {
                Logger.Log("No Type2 details found");
                //Console.WriteLine("No hourly pricing tiers found");
                return new List<Type2Detail>();
            }

            //Console.WriteLine("\n=== PARSED HOURLY PRICING TIERS ===");
            //Console.WriteLine($"Total Tiers: {type2Details.Count}");
            
            //foreach (var tier in type2Details)
            //{
            //    Console.WriteLine($"\nTier ID: {tier.Id}");
            //    Console.WriteLine($"  Min Hours: {tier.MinDuration ?? 0}");
            //    Console.WriteLine($"  Max Hours: {tier.MaxDuration ?? 0}");
            //    Console.WriteLine($"  Amount: ₹{tier.Amount ?? 0}");
            //}
            
            //Console.WriteLine("\nHourly pricing fetched successfully!");
            //Console.WriteLine("====================================\n");

            Logger.Log($"Fetched {type2Details.Count} Type2 pricing tiers");
            return type2Details;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            //Console.WriteLine($"ERROR: Exception while fetching hourly pricing - {ex.Message}");
            return new List<Type2Detail>();
        }
    }

    // Save hourly pricing tiers to local database
    public static void SaveHourlyPricingTiers(string adminId, List<Type2Detail> tiers)
    {
        if (tiers == null || tiers.Count == 0)
        {
            //Console.WriteLine("No hourly pricing tiers to save.");
            return;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            // Delete old pricing for this admin
            string deleteQuery = "DELETE FROM HourlyPricing WHERE admin_id = @admin_id";
            using (var deleteCmd = new SqliteCommand(deleteQuery, connection))
            {
                deleteCmd.Parameters.AddWithValue("@admin_id", adminId);
                deleteCmd.ExecuteNonQuery();
            }

            //Console.WriteLine("\n=== SAVING HOURLY PRICING TO DATABASE ===");
            
            // Insert new pricing tiers
            string insertQuery = @"
                INSERT INTO HourlyPricing (admin_id, min_hours, max_hours, amount, last_synced)
                VALUES (@admin_id, @min_hours, @max_hours, @amount, @last_synced)";

            foreach (var tier in tiers)
            {
                using var insertCmd = new SqliteCommand(insertQuery, connection);
                insertCmd.Parameters.AddWithValue("@admin_id", adminId);
                insertCmd.Parameters.AddWithValue("@min_hours", tier.MinDuration ?? 0);
                insertCmd.Parameters.AddWithValue("@max_hours", tier.MaxDuration ?? 0);
                insertCmd.Parameters.AddWithValue("@amount", tier.Amount ?? 0);
                insertCmd.Parameters.AddWithValue("@last_synced", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                insertCmd.ExecuteNonQuery();

                //Console.WriteLine($"Saved: {tier.MinDuration}-{tier.MaxDuration} hours = ₹{tier.Amount}");
            }

            //Console.WriteLine($"\n✅ {tiers.Count} hourly pricing tiers saved to local database");
            //Console.WriteLine("====================================\n");
            
            Logger.Log($"Saved {tiers.Count} hourly pricing tiers to database for admin_id: {adminId}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            //Console.WriteLine($"ERROR: Failed to save hourly pricing - {ex.Message}");
        }
    }

    // Get printer details from LocalStorage
    public static (string heading1, string heading2, string info1, string info2, string note, string hallName, string logoUrl) GetPrinterDetails()
    {
        return (
            LocalStorage.GetItem("printerHeading1") ?? "Railway Booking",
            LocalStorage.GetItem("printerHeading2") ?? "",
            LocalStorage.GetItem("printerInfo1") ?? "",
            LocalStorage.GetItem("printerInfo2") ?? "",
            LocalStorage.GetItem("printerNote") ?? "Thank you for your visit!",
            LocalStorage.GetItem("hallName") ?? "Railway Hall",
            LocalStorage.GetItem("printerLogoUrl") ?? ""
        );
    }

    public static Settings? GetSettings()
    {
        try
        {
            // First, delete any settings older than 8 hours
            DeleteExpiredSettings();

            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string query = "SELECT * FROM Settings ORDER BY id DESC LIMIT 1";
            using var cmd = new SqliteCommand(query, connection);
            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                Logger.Log($"GetSettings: Found settings record - type_1={reader["type_1"]}, type_2={reader["type_2"]}");
                
                var settings = new Settings
                {
                    Id = Convert.ToInt32(reader["id"]),
                    AdminId = reader["admin_id"]?.ToString() ?? "",
                    Type1 = reader["type_1"]?.ToString(),
                    Type1Amount = reader["type_1_amount"] != DBNull.Value ? Convert.ToDecimal(reader["type_1_amount"]) : null,
                    Type2 = reader["type_2"]?.ToString(),
                    Type2Amount = reader["type_2_amount"] != DBNull.Value ? Convert.ToDecimal(reader["type_2_amount"]) : null,
                    Type3 = null,
                    Type3Amount = null,
                    Type4 = null,
                    Type4Amount = null,
                    AdvancePaymentEnabled = reader["advance_payment_enabled"] != DBNull.Value && Convert.ToInt32(reader["advance_payment_enabled"]) == 1,
                    DefaultAdvancePercentage = reader["default_advance_percentage"] != DBNull.Value ? Convert.ToDecimal(reader["default_advance_percentage"]) : 0m,
                    LastSynced = DateTime.TryParse(reader["last_synced"]?.ToString(), out var date) 
                        ? date : DateTime.MinValue,
                    GraceTimeType1 = reader["grace_time_type_1"] != DBNull.Value ? Convert.ToInt32(reader["grace_time_type_1"]) : 25,
                    GraceTimeType2 = reader["grace_time_type_2"] != DBNull.Value ? Convert.ToInt32(reader["grace_time_type_2"]) : 25
                };

                // Double-check if this setting is still valid (within 8 hours)
                if ((DateTime.Now - settings.LastSynced).TotalHours >= 8)
                {
                    Logger.Log("GetSettings: Settings expired (older than 8 hours), returning null");
                    return null;
                }

                Logger.Log($"GetSettings: Returning settings - Type1={settings.Type1}, Type2={settings.Type2}");
                return settings;
            }

            Logger.Log("GetSettings: No settings found in database");
            return null;

            
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            return null;
        }
    }

    // Alias for GetSettings - for backward compatibility
    public static Settings? GetWorkerSettings()
    {
        return GetSettings();
    }

    public static List<BookingType> GetBookingTypes()
    {
        var types = new List<BookingType>();

        try
        {
            Logger.Log("GetBookingTypes: Fetching settings...");
            var settings = GetSettings();
            
            if (settings == null)
            {
                Logger.Log("GetBookingTypes: Settings is null - no settings found in database");
                return types;
            }

            Logger.Log($"GetBookingTypes: Settings found - Type1={settings.Type1}, Type1Amount={settings.Type1Amount}, Type2={settings.Type2}, Type2Amount={settings.Type2Amount}");

            if (!string.IsNullOrEmpty(settings.Type1) && settings.Type1Amount.HasValue)
            {
                types.Add(new BookingType { Type = settings.Type1, Amount = settings.Type1Amount.Value });
                Logger.Log($"Added Type1: {settings.Type1} = ₹{settings.Type1Amount.Value}");
            }

            if (!string.IsNullOrEmpty(settings.Type2) && settings.Type2Amount.HasValue)
            {
                types.Add(new BookingType { Type = settings.Type2, Amount = settings.Type2Amount.Value });
                Logger.Log($"Added Type2: {settings.Type2} = ₹{settings.Type2Amount.Value}");
            }

            Logger.Log($"GetBookingTypes: Returning {types.Count} booking types");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            Logger.Log($"GetBookingTypes: Error - {ex.Message}");
        }

        return types;
    }

    public static List<HourlyPricingTier> GetHourlyPricingTiers()
    {
        var tiers = new List<HourlyPricingTier>();

        try
        {
            // Get current admin_id from LocalStorage
            string currentAdminId = LocalStorage.GetItem("adminId") ?? "";
            
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            // Only get tiers for the current admin_id
            string query = "SELECT * FROM HourlyPricing WHERE admin_id = @admin_id ORDER BY min_hours";
            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@admin_id", currentAdminId);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                tiers.Add(new HourlyPricingTier
                {
                    Id = Convert.ToInt32(reader["id"]),
                    AdminId = reader["admin_id"]?.ToString() ?? "",
                    MinHours = Convert.ToInt32(reader["min_hours"]),
                    MaxHours = Convert.ToInt32(reader["max_hours"]),
                    Amount = Convert.ToDecimal(reader["amount"])
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        return tiers;
    }

    public static decimal GetBookingTypeAmount(string typeName)
    {
        try
        {
            var types = GetBookingTypes();
            var matchingType = types.FirstOrDefault(t => t.Type.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            return matchingType?.Amount ?? 0;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            return 0;
        }
    }

    public static async Task<bool> SyncSingleBookingToServer(Booking1 booking)
    {
        if (booking == null) return false;

        // Check if network is available
        bool isOnline = NetworkInterface.GetIsNetworkAvailable();

        if (!isOnline)
        {
            Logger.Log("No internet connection available for syncing.");
            return false;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            
            var bookingData = new[] { booking };
            var json = JsonConvert.SerializeObject(bookingData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(CreateBookingApiUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                // Update IsSynced to 1 in local database
                if (!string.IsNullOrEmpty(booking.booking_id))
                {
                    UpdateBookingSyncStatus(booking.booking_id, 1);
                    Logger.Log($"Booking {booking.booking_id} synced successfully to server.");
                }
                return true;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // 409 Conflict - booking already exists on server
                // Mark as synced locally
                if (!string.IsNullOrEmpty(booking.booking_id))
                {
                    UpdateBookingSyncStatus(booking.booking_id, 1);
                    Logger.Log($"Booking {booking.booking_id} already exists on server. Marked as synced locally.");
                }
                return true;
            }
            else
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                Logger.Log($"Failed to sync booking {booking.booking_id}: {response.StatusCode} - {responseBody}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error syncing booking {booking.booking_id}: {ex.Message}");
            Logger.LogError(ex);
            return false;
        }
    }

    /// <summary>
    /// Updates the IsSynced status of a booking in local database
    /// </summary>
    private static void UpdateBookingSyncStatus(string bookingId, int isSynced)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string updateQuery = @"
                UPDATE Bookings 
                SET IsSynced = @IsSynced, 
                    updated_at = @UpdatedAt 
                WHERE booking_id = @BookingId";

            using var cmd = new SqliteCommand(updateQuery, connection);
            cmd.Parameters.AddWithValue("@IsSynced", isSynced);
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@BookingId", bookingId);

            int rowsAffected = cmd.ExecuteNonQuery();
            Logger.Log($"Updated IsSynced status for booking {bookingId}. Rows affected: {rowsAffected}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    // ===== Worker Summary Update Methods =====

    /// <summary>
    /// Initialize or get active worker summary session
    /// </summary>
    public static int GetOrCreateActiveWorkerSummary(string workerId, string adminId)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            // Check if active session exists
            string selectQuery = "SELECT s_no FROM workers_summary WHERE worker_id = @worker_id AND admin_id = @admin_id AND status = 'active' LIMIT 1";
            
            using (var selectCmd = new SqliteCommand(selectQuery, connection))
            {
                selectCmd.Parameters.AddWithValue("@worker_id", workerId);
                selectCmd.Parameters.AddWithValue("@admin_id", adminId);
                
                var result = selectCmd.ExecuteScalar();
                if (result != null)
                {
                    return Convert.ToInt32(result);
                }
            }

            // Create new active session
            string insertQuery = @"
                INSERT INTO workers_summary (
                    admin_id, worker_id,
                    total_booking, total_person,
                    sitting_booking_count, sleeping_booking_count,
                    sitting_booking_total_amount, sleeping_total_amount,
                    in_cash_collect, in_upi_collect,
                    total_balance,
                    login_time, created_time, last_updated_timestamp,
                    status, is_synced
                ) VALUES (
                    @admin_id, @worker_id,
                    0, 0,
                    0, 0,
                    0, 0,
                    0, 0,
                    0,
                    @login_time, @created_time, @updated_time,
                    'active', 0
                )";

            using (var insertCmd = new SqliteCommand(insertQuery, connection))
            {
                var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                insertCmd.Parameters.AddWithValue("@admin_id", adminId);
                insertCmd.Parameters.AddWithValue("@worker_id", workerId);
                insertCmd.Parameters.AddWithValue("@login_time", now);
                insertCmd.Parameters.AddWithValue("@created_time", now);
                insertCmd.Parameters.AddWithValue("@updated_time", now);
                insertCmd.ExecuteNonQuery();
            }

            // Get the newly created s_no
            string getIdQuery = "SELECT last_insert_rowid()";
            using (var getIdCmd = new SqliteCommand(getIdQuery, connection))
            {
                return Convert.ToInt32(getIdCmd.ExecuteScalar());
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            return -1;
        }
    }

    /// <summary>
    /// Update worker summary when a booking is created
    /// </summary>
    public static void UpdateWorkerSummaryOnBooking(string workerId, string adminId, Booking1 booking)
    {
        try
        {
            int sNo = GetOrCreateActiveWorkerSummary(workerId, adminId);
            if (sNo == -1) return;

            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            // Determine booking type
            bool isSitting = booking.booking_type?.ToLower() == "sitting";
            bool isSleeping = booking.booking_type?.ToLower() == "sleeper";

            // Determine payment method
            bool isCash = booking.payment_method?.ToLower() == "cash";
            bool isUpi = booking.payment_method?.ToLower() == "upi" || booking.payment_method?.ToLower() == "online";

            string updateQuery = @"
                UPDATE workers_summary
                SET
                    total_booking = total_booking + 1,
                    total_person = total_person + @persons,
                    sitting_booking_count = sitting_booking_count + @sitting_count,
                    sleeping_booking_count = sleeping_booking_count + @sleeping_count,
                    sitting_booking_total_amount = sitting_booking_total_amount + @sitting_amount,
                    sleeping_total_amount = sleeping_total_amount + @sleeping_amount,
                    in_cash_collect = in_cash_collect + @cash_amount,
                    in_upi_collect = in_upi_collect + @upi_amount,
                    total_balance = total_balance + @balance_amount,
                    last_updated_timestamp = @updated_time,
                    is_synced = 0
                WHERE s_no = @s_no";

            using var updateCmd = new SqliteCommand(updateQuery, connection);
            updateCmd.Parameters.AddWithValue("@persons", booking.number_of_persons);
            updateCmd.Parameters.AddWithValue("@sitting_count", isSitting ? 1 : 0);
            updateCmd.Parameters.AddWithValue("@sleeping_count", isSleeping ? 1 : 0);
            // Add paid_amount to revenue (not total_amount)
            updateCmd.Parameters.AddWithValue("@sitting_amount", isSitting ? booking.paid_amount : 0);
            updateCmd.Parameters.AddWithValue("@sleeping_amount", isSleeping ? booking.paid_amount : 0);
            updateCmd.Parameters.AddWithValue("@cash_amount", isCash ? booking.paid_amount : 0);
            updateCmd.Parameters.AddWithValue("@upi_amount", isUpi ? booking.paid_amount : 0);
            updateCmd.Parameters.AddWithValue("@balance_amount", booking.balance_amount);
            updateCmd.Parameters.AddWithValue("@updated_time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            updateCmd.Parameters.AddWithValue("@s_no", sNo);

            updateCmd.ExecuteNonQuery();
            Logger.Log($"Worker summary updated: s_no={sNo}, booking_id={booking.booking_id}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    /// <summary>
    /// Update worker summary when balance payment is made
    /// </summary>
    public static void UpdateWorkerSummaryOnBalancePayment(string workerId, string adminId, decimal paidAmount, string paymentMethod, decimal remainingBalance)
    {
        try
        {
            int sNo = GetOrCreateActiveWorkerSummary(workerId, adminId);
            if (sNo == -1) return;

            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            bool isCash = paymentMethod?.ToLower() == "cash";
            bool isUpi = paymentMethod?.ToLower() == "upi" || paymentMethod?.ToLower() == "online";

            string updateQuery = @"
                UPDATE workers_summary
                SET
                    in_cash_collect = in_cash_collect + @cash_amount,
                    in_upi_collect = in_upi_collect + @upi_amount,
                    total_balance = @remaining_balance,
                    last_updated_timestamp = @updated_time,
                    is_synced = 0
                WHERE s_no = @s_no";

            using var updateCmd = new SqliteCommand(updateQuery, connection);
            updateCmd.Parameters.AddWithValue("@cash_amount", isCash ? paidAmount : 0);
            updateCmd.Parameters.AddWithValue("@upi_amount", isUpi ? paidAmount : 0);
            updateCmd.Parameters.AddWithValue("@remaining_balance", remainingBalance);
            updateCmd.Parameters.AddWithValue("@updated_time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            updateCmd.Parameters.AddWithValue("@s_no", sNo);

            updateCmd.ExecuteNonQuery();
            Logger.Log($"Worker summary balance payment updated: s_no={sNo}, paid={paidAmount}, method={paymentMethod}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    /// <summary>
    /// Close active worker session (mark as completed)
    /// </summary>
    public static bool CloseWorkerSession(string workerId, string adminId)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string updateQuery = @"
                UPDATE workers_summary
                SET
                    status = 'completed',
                    total_balance = 0,
                    closed_time = @closed_time,
                    last_updated_timestamp = @updated_time,
                    is_synced = 0
                WHERE worker_id = @worker_id 
                  AND admin_id = @admin_id 
                  AND status = 'active'";

            using var updateCmd = new SqliteCommand(updateQuery, connection);
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            updateCmd.Parameters.AddWithValue("@closed_time", now);
            updateCmd.Parameters.AddWithValue("@updated_time", now);
            updateCmd.Parameters.AddWithValue("@worker_id", workerId);
            updateCmd.Parameters.AddWithValue("@admin_id", adminId);

            int rowsAffected = updateCmd.ExecuteNonQuery();
            Logger.Log($"Worker session closed: worker={workerId}, rows={rowsAffected}");
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            return false;
        }
    }

    /// <summary>
    /// Get active worker summary data
    /// </summary>
    public static WorkerSummaryRecord? GetActiveWorkerSummary(string workerId, string adminId)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string query = @"
                SELECT * FROM workers_summary 
                WHERE worker_id = @worker_id 
                  AND admin_id = @admin_id 
                  AND status = 'active' 
                LIMIT 1";

            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@worker_id", workerId);
            cmd.Parameters.AddWithValue("@admin_id", adminId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return MapWorkerSummaryFromReader(reader);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        return null;
    }

    /// <summary>
    /// Get ANY active worker summary for the admin (used when worker logs out/in but session still active)
    /// </summary>
    public static WorkerSummaryRecord? GetAnyActiveWorkerSummary(string adminId)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string query = @"
                SELECT * FROM workers_summary 
                WHERE admin_id = @admin_id 
                  AND status = 'active' 
                ORDER BY login_time DESC
                LIMIT 1";

            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@admin_id", adminId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return MapWorkerSummaryFromReader(reader);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        return null;
    }

    /// <summary>
    /// Update worker balance - DEPRECATED, kept for backward compatibility
    /// </summary>
    [Obsolete("Use UpdateWorkerSummaryOnBooking instead")]
    public static async Task<bool> UpdateWorkerBalanceAsync(string workerId, string adminId, decimal balanceAmount)
    {
        // Check if network is available
        bool isOnline = NetworkInterface.GetIsNetworkAvailable();

        if (isOnline)
        {
            try
            {
                // Try to send directly to API
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

                var balanceData = new
                {
                    worker_id = workerId,
                    admin_id = adminId,
                    amount = balanceAmount
                };

                var json = JsonConvert.SerializeObject(balanceData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PutAsync("https://railway-api-worker.artechnology.pro/api/Booking/update-worker-balance", content);

                if (response.IsSuccessStatusCode)
                {
                    Logger.Log($"Worker balance updated online: Worker={workerId}, Amount={balanceAmount}");
                    return true;
                }
                else
                {
                    // API rejected, save offline
                    string responseBody = await response.Content.ReadAsStringAsync();
                    Logger.Log($"API rejected balance update: {response.StatusCode} - {responseBody}");
                    SaveWorkerBalanceOffline(workerId, adminId, balanceAmount);
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Network error, save offline
                Logger.Log($"Error updating balance online: {ex.Message}");
                SaveWorkerBalanceOffline(workerId, adminId, balanceAmount);
                return false;
            }
        }
        else
        {
            // No network, save offline
            SaveWorkerBalanceOffline(workerId, adminId, balanceAmount);
            return false;
        }
    }

    /// <summary>
    /// Save worker balance update to local database (offline)
    /// </summary>
    private static void SaveWorkerBalanceOffline(string workerId, string adminId, decimal balanceAmount)
    {
        try
        {
            Logger.Log($"SaveWorkerBalanceOffline called: Worker={workerId}, Admin={adminId}, Amount={balanceAmount}");
            Logger.Log($"Database path: {DbPath}");
            
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();
            Logger.Log("Database connection opened successfully");

            // Check if record exists for this worker and admin
            string selectQuery = "SELECT balance_amount, is_synced FROM worker_balance WHERE worker_id = @worker_id AND admin_id = @admin_id";
            
            using (var selectCmd = new SqliteCommand(selectQuery, connection))
            {
                selectCmd.Parameters.AddWithValue("@worker_id", workerId);
                selectCmd.Parameters.AddWithValue("@admin_id", adminId);
                
                using var reader = selectCmd.ExecuteReader();
                
                if (reader.Read())
                {
                    // Record exists
                    decimal currentBalance = Convert.ToDecimal(reader["balance_amount"]);
                    int isSynced = Convert.ToInt32(reader["is_synced"]);
                    Logger.Log($"Found existing record: Current Balance={currentBalance}, IsSynced={isSynced}");
                    reader.Close();

                    decimal newBalance;
                    if (isSynced == 0)
                    {
                        // Not synced yet, add to existing balance
                        newBalance = currentBalance + balanceAmount;
                        Logger.Log($"Worker balance updated offline (adding): Worker={workerId}, Old={currentBalance}, New={newBalance}");
                    }
                    else
                    {
                        // Was synced, reset and add new balance
                        newBalance = balanceAmount;
                        Logger.Log($"Worker balance updated offline (reset from synced): Worker={workerId}, Amount={newBalance}");
                    }

                    // Update existing record
                    string updateQuery = @"
                        UPDATE worker_balance 
                        SET balance_amount = @balance_amount, 
                            is_synced = 0, 
                            created_at = @created_at 
                        WHERE worker_id = @worker_id AND admin_id = @admin_id";

                    using var updateCmd = new SqliteCommand(updateQuery, connection);
                    updateCmd.Parameters.AddWithValue("@balance_amount", newBalance);
                    updateCmd.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    updateCmd.Parameters.AddWithValue("@worker_id", workerId);
                    updateCmd.Parameters.AddWithValue("@admin_id", adminId);
                    int rowsUpdated = updateCmd.ExecuteNonQuery();
                    Logger.Log($"Updated {rowsUpdated} row(s) with new balance: {newBalance}");
                }
                else
                {
                    // Record doesn't exist, insert new one
                    reader.Close();
                    
                    string insertQuery = @"
                        INSERT INTO worker_balance (admin_id, worker_id, balance_amount, is_synced, created_at)
                        VALUES (@admin_id, @worker_id, @balance_amount, 0, @created_at)";

                    using var insertCmd = new SqliteCommand(insertQuery, connection);
                    insertCmd.Parameters.AddWithValue("@admin_id", adminId);
                    insertCmd.Parameters.AddWithValue("@worker_id", workerId);
                    insertCmd.Parameters.AddWithValue("@balance_amount", balanceAmount);
                    insertCmd.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    int rowsInserted = insertCmd.ExecuteNonQuery();
                    Logger.Log($"Inserted {rowsInserted} new row(s)");
                    Logger.Log($"Worker balance saved offline (new record): Worker={workerId}, Amount={balanceAmount}");
                }
            }
            
            Logger.Log("SaveWorkerBalanceOffline completed successfully");
        }
        catch (Exception ex)
        {
            Logger.Log($"EXCEPTION in SaveWorkerBalanceOffline: {ex.Message}");
            Logger.LogError(ex);
        }
    }

    /// <summary>
    /// Sync pending worker summaries to server
    /// </summary>
    public static async Task<int> SyncWorkerSummariesAsync()
    {
        int successCount = 0;

        try
        {
            // Get all unsynced completed summaries
            var pendingSummaries = GetPendingWorkerSummaries();

            if (pendingSummaries.Count == 0)
            {
                Logger.Log("No pending worker summaries to sync");
                return 0;
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            foreach (var summary in pendingSummaries)
            {
                try
                {
                    var summaryData = new
                    {
                        admin_id = summary.AdminId,
                        worker_id = summary.WorkerId,
                        total_booking = summary.TotalBooking,
                        total_person = summary.TotalPerson,
                        sitting_booking_count = summary.SittingBookingCount,
                        sleeping_booking_count = summary.SleepingBookingCount,
                        sitting_booking_total_amount = summary.SittingBookingTotalAmount,
                        sleeping_total_amount = summary.SleepingTotalAmount,
                        in_cash_collect = summary.InCashCollect,
                        in_upi_collect = summary.InUpiCollect,
                        total_balance = summary.TotalBalance,
                        login_time = summary.LoginTime,
                        created_time = summary.CreatedTime,
                        closed_time = summary.ClosedTime,
                        status = summary.Status,
                        is_synced = 0
                    };

                    var json = JsonConvert.SerializeObject(summaryData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://railway-api-worker.artechnology.pro/api/worker-summary/sync", content);

                    if (response.IsSuccessStatusCode)
                    {
                        // Mark as synced
                        MarkWorkerSummaryAsSynced(summary.SNo);
                        successCount++;
                        Logger.Log($"Synced worker summary: s_no={summary.SNo}, Worker={summary.WorkerId}, Bookings={summary.TotalBooking}");
                    }
                    else
                    {
                        string errorBody = await response.Content.ReadAsStringAsync();
                        Logger.Log($"Failed to sync worker summary s_no={summary.SNo}: {response.StatusCode} - {errorBody}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error syncing worker summary s_no={summary.SNo}: {ex.Message}");
                }
            }

            Logger.Log($"Worker summary sync completed: {successCount}/{pendingSummaries.Count} successful");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        return successCount;
    }

    /// <summary>
    /// Get all pending (unsynced) worker summaries
    /// </summary>
    private static List<WorkerSummaryRecord> GetPendingWorkerSummaries()
    {
        var summaries = new List<WorkerSummaryRecord>();

        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string query = "SELECT * FROM workers_summary WHERE status = 'completed' AND is_synced = 0";

            using var cmd = new SqliteCommand(query, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                summaries.Add(MapWorkerSummaryFromReader(reader));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        return summaries;
    }

    /// <summary>
    /// Map database reader to WorkerSummaryRecord
    /// </summary>
    private static WorkerSummaryRecord MapWorkerSummaryFromReader(SqliteDataReader reader)
    {
        return new WorkerSummaryRecord
        {
            SNo = Convert.ToInt32(reader["s_no"]),
            AdminId = reader["admin_id"]?.ToString() ?? "",
            WorkerId = reader["worker_id"]?.ToString() ?? "",
            TotalBooking = reader["total_booking"] != DBNull.Value ? Convert.ToInt32(reader["total_booking"]) : 0,
            TotalPerson = reader["total_person"] != DBNull.Value ? Convert.ToInt32(reader["total_person"]) : 0,
            SittingBookingCount = reader["sitting_booking_count"] != DBNull.Value ? Convert.ToInt32(reader["sitting_booking_count"]) : 0,
            SleepingBookingCount = reader["sleeping_booking_count"] != DBNull.Value ? Convert.ToInt32(reader["sleeping_booking_count"]) : 0,
            SittingBookingTotalAmount = reader["sitting_booking_total_amount"] != DBNull.Value ? Convert.ToDecimal(reader["sitting_booking_total_amount"]) : 0,
            SleepingTotalAmount = reader["sleeping_total_amount"] != DBNull.Value ? Convert.ToDecimal(reader["sleeping_total_amount"]) : 0,
            InCashCollect = reader["in_cash_collect"] != DBNull.Value ? Convert.ToDecimal(reader["in_cash_collect"]) : 0,
            InUpiCollect = reader["in_upi_collect"] != DBNull.Value ? Convert.ToDecimal(reader["in_upi_collect"]) : 0,
            TotalBalance = reader["total_balance"] != DBNull.Value ? Convert.ToDecimal(reader["total_balance"]) : 0,
            ExtraCharges = reader["extra_charges"] != DBNull.Value ? Convert.ToDecimal(reader["extra_charges"]) : 0,
            LoginTime = reader["login_time"]?.ToString() ?? "",
            CreatedTime = reader["created_time"]?.ToString() ?? "",
            ClosedTime = reader["closed_time"]?.ToString(),
            LastUpdatedTimestamp = reader["last_updated_timestamp"]?.ToString(),
            Status = reader["status"]?.ToString() ?? "active",
            IsSynced = reader["is_synced"] != DBNull.Value ? Convert.ToInt32(reader["is_synced"]) : 0
        };
    }

    /// <summary>
    /// Mark a worker summary record as synced
    /// </summary>
    private static void MarkWorkerSummaryAsSynced(int sNo)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string update = "UPDATE workers_summary SET is_synced = 1 WHERE s_no = @s_no";

            using var cmd = new SqliteCommand(update, connection);
            cmd.Parameters.AddWithValue("@s_no", sNo);

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    /// <summary>
    /// Get aggregated worker summary for a specific worker and date range
    /// </summary>
    public static WorkerSummaryRecord? GetAggregatedWorkerSummary(string workerId, DateTime fromDate, DateTime toDate)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string query = @"
                SELECT 
                    @worker_id as worker_id,
                    '' as admin_id,
                    SUM(total_booking) as total_booking,
                    SUM(total_person) as total_person,
                    SUM(sitting_booking_count) as sitting_booking_count,
                    SUM(sleeping_booking_count) as sleeping_booking_count,
                    SUM(sitting_booking_total_amount) as sitting_booking_total_amount,
                    SUM(sleeping_total_amount) as sleeping_total_amount,
                    SUM(in_cash_collect) as in_cash_collect,
                    SUM(in_upi_collect) as in_upi_collect,
                    SUM(total_balance) as total_balance,
                    MIN(login_time) as login_time,
                    MIN(created_time) as created_time,
                    MAX(closed_time) as closed_time,
                    MAX(last_updated_timestamp) as last_updated_timestamp,
                    'completed' as status,
                    0 as s_no,
                    0 as is_synced
                FROM workers_summary
                WHERE worker_id = @worker_id
                  AND datetime(created_time) >= datetime(@from_date)
                  AND datetime(created_time) <= datetime(@to_date)";

            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@worker_id", workerId);
            cmd.Parameters.AddWithValue("@from_date", fromDate.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to_date", toDate.ToString("yyyy-MM-dd HH:mm:ss"));

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                // Check if there's actually data
                if (reader["total_booking"] == DBNull.Value || Convert.ToInt32(reader["total_booking"]) == 0)
                {
                    return null;
                }
                return MapWorkerSummaryFromReader(reader);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        return null;
    }
}

// Worker Summary Record Model
public class WorkerSummaryRecord
{
    public int SNo { get; set; }
    public string AdminId { get; set; } = string.Empty;
    public string WorkerId { get; set; } = string.Empty;
    
    public int TotalBooking { get; set; }
    public int TotalPerson { get; set; }
    
    public int SittingBookingCount { get; set; }
    public int SleepingBookingCount { get; set; }
    
    public decimal SittingBookingTotalAmount { get; set; }
    public decimal SleepingTotalAmount { get; set; }
    
    public decimal InCashCollect { get; set; }
    public decimal InUpiCollect { get; set; }
    
    public decimal TotalBalance { get; set; }
    public decimal ExtraCharges { get; set; }
    
    public string LoginTime { get; set; } = string.Empty;
    public string CreatedTime { get; set; } = string.Empty;
    public string? ClosedTime { get; set; }
    public string? LastUpdatedTimestamp { get; set; }
    
    public string Status { get; set; } = "active";
    public int IsSynced { get; set; }
}
