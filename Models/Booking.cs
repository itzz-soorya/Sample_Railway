using System;
using System.Collections.Generic;
using System.Linq;

namespace UserModule.Models
{
    public class Booking
    {
        public string BookingId { get; set; } = Guid.NewGuid().ToString();
        public string? Name { get; set; }
        public string? PhoneNo { get; set; }
        public string? SeatType { get; set; }
        public string? RoomNumber { get; set; }
        public int NumberOfPersons { get; set; }
        public int TotalHours { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double PaidAmount { get; set; }
        public string? PaymentType { get; set; }
        public string? Status { get; set; }
        public string? IdType { get; set; }
        public string? IdNumber { get; set; }
        public double PricePerPerson { get; set; }
        public double AdvanceAmount { get; set; }
        public double BalanceAmount { get; set; }

        public bool IsMatch { get; set; } = false;


        // ================================
        // MAIN BOOKINGS
        // ================================
        private static readonly List<Booking> _bookings = new();
        public static void StoreBooking(Booking booking)
        {
            _bookings.Add(booking);
        }

        public static List<Booking> GetAllBookings() => _bookings;

        public static Booking? GetBookingById(string bookingId)
            => _bookings.FirstOrDefault(b => b.BookingId == bookingId);

        // ================================
        // SUBMIT BOOKINGS (Separate List)
        // ================================
        private static readonly List<Booking> _submitBookings = new();

        public static void StoreSubmitBooking(Booking booking)
        {
            // prevent duplicates
            if (!_submitBookings.Any(b => b.BookingId == booking.BookingId))
                _submitBookings.Add(booking);
        }

        public static List<Booking> GetAllSubmitBookings() => _submitBookings;

        public static Booking? GetSubmitBookingById(string bookingId)
            => _submitBookings.FirstOrDefault(b => b.BookingId == bookingId);
    }
}
