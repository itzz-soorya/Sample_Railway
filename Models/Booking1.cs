using System;

namespace UserModule.Models
{
    public class Booking1
    {
        public string? booking_id { get; set; }               // Primary key
        public string? worker_id { get; set; }
        public string? guest_name { get; set; }
        public string? phone_number { get; set; }
        public int number_of_persons { get; set; }
        public string? booking_type { get; set; }
        public string? room_number { get; set; }              // Room number for Sleeper bookings
        public int total_hours { get; set; }
        public DateTime booking_date { get; set; }
        public TimeSpan in_time { get; set; }
        public TimeSpan? out_time { get; set; }              // Nullable (may not be known yet)
        public string? proof_type { get; set; }
        public string? proof_id { get; set; }
        public decimal price_per_person { get; set; }
        public decimal total_amount { get; set; }
        public decimal paid_amount { get; set; }
        public decimal balance_amount { get; set; }
        public string? payment_method { get; set; }
        public DateTime? created_at { get; set; }            // Auto-filled when saving locally
        public DateTime? updated_at { get; set; }            // Auto-filled when saving locally
        public string? status { get; set; }                   // Active / Pending / Cancelled etc.
        public int IsSynced { get; set; } = 0;               // 0 = not synced, 1 = synced
        public string? booked_by { get; set; }               // Worker who created the booking
        public string? closed_by { get; set; }               // Worker who closed/completed the booking
        public string? balance_payment_payment { get; set; } // Balance payment method
    }
}
