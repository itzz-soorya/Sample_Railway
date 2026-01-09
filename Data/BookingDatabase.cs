using System;
using System.Data.SQLite;
using System.IO;

namespace UserModule.Data
{
    public static class BookingDatabase
    {
        // Store database in AppData\Local instead of Program Files to avoid permission issues
        private static string appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Railax", "Data");
        private static string dbPath = Path.Combine(appDataFolder, "bookings.db");
        private static string connectionString = $"Data Source={dbPath};Version=3;";

        static BookingDatabase()
        {
            // Ensure the directory exists
            Directory.CreateDirectory(appDataFolder);
            InitializeDatabase();
        }

        private static void InitializeDatabase()
        {
            if (!File.Exists(dbPath))
            {
                SQLiteConnection.CreateFile(dbPath);
            }

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS Bookings (
                        BookingId TEXT PRIMARY KEY,
                        Name TEXT,
                        PhoneNo TEXT,
                        SeatType TEXT,
                        StartTime TEXT,
                        EndTime TEXT,
                        PaymentType TEXT,
                        Status TEXT
                    );";

                using (var command = new SQLiteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public static SQLiteConnection GetConnection()
        {
            return new SQLiteConnection(connectionString);
        }
    }
}
