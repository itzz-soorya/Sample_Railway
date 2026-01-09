using System;
using System.Collections.Generic;
using System.Data.SQLite;
using UserModule.Models;
using UserModule.Data;

namespace UserModule.Services
{
    public class BookingService
    {
        public void AddBooking(Booking booking)
        {
            using (var connection = BookingDatabase.GetConnection())
            {
                connection.Open();
                string insertQuery = @"INSERT INTO Bookings 
                    (BookingId, Name, PhoneNo, SeatType, StartTime, EndTime, PaymentType, Status) 
                    VALUES (@BookingId, @Name, @PhoneNo, @SeatType, @StartTime, @EndTime, @PaymentType, @Status)";

                using (var command = new SQLiteCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@BookingId", booking.BookingId);
                    command.Parameters.AddWithValue("@Name", booking.Name);
                    command.Parameters.AddWithValue("@PhoneNo", booking.PhoneNo);
                    command.Parameters.AddWithValue("@SeatType", booking.SeatType);
                    command.Parameters.AddWithValue(
                        "@StartTime",
                        booking.StartTime.HasValue ? booking.StartTime.Value.ToString("O") : (object)DBNull.Value
                    );
                    command.Parameters.AddWithValue(
                        "@EndTime",
                        booking.EndTime.HasValue ? booking.EndTime.Value.ToString("O") : (object)DBNull.Value
                    );
                    command.Parameters.AddWithValue("@PaymentType", booking.PaymentType);
                    command.Parameters.AddWithValue("@Status", booking.Status);

                    command.ExecuteNonQuery();
                }
            }
        }

        public List<Booking> GetBookings()
        {
            var bookings = new List<Booking>();
            using (var connection = BookingDatabase.GetConnection())
            {
                connection.Open();
                string selectQuery = "SELECT * FROM Bookings";
                using (var command = new SQLiteCommand(selectQuery, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        bookings.Add(new Booking
                        {
                            BookingId = reader["BookingId"]?.ToString() ?? string.Empty,
                            Name = reader["Name"]?.ToString(),
                            PhoneNo = reader["PhoneNo"]?.ToString(),
                            SeatType = reader["SeatType"]?.ToString(),
                            StartTime = DateTime.TryParse(reader["StartTime"]?.ToString(), out var start) ? start : DateTime.Now,
                            EndTime = DateTime.TryParse(reader["EndTime"]?.ToString(), out var end) ? end : (DateTime?)null,
                            PaymentType = reader["PaymentType"]?.ToString(),
                            Status = reader["Status"]?.ToString()
                        });
                    }
                }
            }
            return bookings;
        }
    }
}
