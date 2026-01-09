using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UserModule.Models
{
    public class BookingType
    {
        public string Type { get; set; } = "";
        public decimal Amount { get; set; }
    }

    // Hourly Pricing Tier Model
    public class HourlyPricingTier
    {
        public int Id { get; set; }
        public string AdminId { get; set; } = "";
        public int MinHours { get; set; }
        public int MaxHours { get; set; }
        public decimal Amount { get; set; }
    }

    // API Response Model (matches backend /api/Settings/hall-types/{adminId})
    public class HallTypesResponse
    {
        [JsonProperty("type_1")]
        public string? Type1 { get; set; }
        
        [JsonProperty("type_1_amount")]
        public decimal? Type1Amount { get; set; }
        
        [JsonProperty("type_2")]
        public string? Type2 { get; set; }
        
        [JsonProperty("grace_amount")]
        public decimal? GraceAmount { get; set; }
        
        [JsonProperty("advance_payment_enabled")]
        public bool AdvancePaymentEnabled { get; set; }
        
        [JsonProperty("advance_payment")]
        public decimal? AdvancePayment { get; set; }
        
        [JsonProperty("grace_amount_type_2")]
        public decimal? GraceAmountType2 { get; set; }
    }
    
    // API Response Model for Type2 Details (matches /api/Settings/sleeping-details/{adminId})
    public class Type2Detail
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        
        [JsonProperty("min_duration")]
        public int? MinDuration { get; set; }
        
        [JsonProperty("max_duration")]
        public int? MaxDuration { get; set; }
        
        [JsonProperty("amount")]
        public decimal? Amount { get; set; }
    }
    
    // API Response Model for Printer Details (matches /api/Settings/printer-details/{adminId})
    public class PrinterDetailsResponse
    {
        [JsonProperty("heading1")]
        public string? Heading1 { get; set; }
        
        [JsonProperty("heading2")]
        public string? Heading2 { get; set; }
        
        [JsonProperty("info1")]
        public string? Info1 { get; set; }
        
        [JsonProperty("info2")]
        public string? Info2 { get; set; }
        
        [JsonProperty("note")]
        public string? Note { get; set; }
        
        [JsonProperty("hall_name")]
        public string? HallName { get; set; }
        
        [JsonProperty("logo_url")]
        public string? LogoUrl { get; set; }
    }

    // Database Model for Settings
    public class Settings
    {
        public int Id { get; set; }
        public string AdminId { get; set; } = "";
        public string? Type1 { get; set; }
        public decimal? Type1Amount { get; set; }
        public string? Type2 { get; set; }
        public decimal? Type2Amount { get; set; }
        public string? Type3 { get; set; }
        public decimal? Type3Amount { get; set; }
        public string? Type4 { get; set; }
        public decimal? Type4Amount { get; set; }
        public bool AdvancePaymentEnabled { get; set; }
    public decimal DefaultAdvancePercentage { get; set; }
        public DateTime LastSynced { get; set; }
    }
}
