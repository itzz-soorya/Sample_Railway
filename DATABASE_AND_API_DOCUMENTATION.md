# Railway Booking System - Database & API Documentation

## Overview
This document provides a comprehensive reference for all database structures, API endpoints, and their interactions in the Railway Booking System.

---

## Table of Contents
1. [Database Files](#database-files)
2. [Database Schema](#database-schema)
3. [API Endpoints](#api-endpoints)
4. [Models](#models)
5. [Data Flow](#data-flow)

---

## Database Files

### 1. **BookingDatabase.cs** (`Data/BookingDatabase.cs`)
- **Purpose**: Static class managing SQLite database connection and initialization
- **Database Location**: `AppData\Local\Railax\Data\bookings.db`
- **Connection String**: `Data Source={dbPath};Version=3;`
- **Key Methods**:
  - `GetConnection()` - Returns a new SQLiteConnection instance
  - `InitializeDatabase()` - Creates tables if they don't exist

**Tables Created**:
```sql
CREATE TABLE IF NOT EXISTS Bookings (
    BookingId TEXT PRIMARY KEY,
    Name TEXT,
    PhoneNo TEXT,
    SeatType TEXT,
    StartTime TEXT,
    EndTime TEXT,
    PaymentType TEXT,
    Status TEXT
);
```

---

### 2. **OfflineBookingStorage.cs** (`Storage/OfflineBookingStorage.cs`)
- **Purpose**: Comprehensive static class for offline-first booking storage with online sync
- **Database Location**: `AppData\Local\Railax\offline_bookings.db`
- **Type**: SQLite with Microsoft.Data.Sqlite
- **Key Responsibility**: Handles offline bookings, syncing to API, and settings management

**Database Tables Created**:

#### a. **Bookings Table**
```sql
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
);
```

**IsSynced Status Values**:
- `0` = Not synced (new/offline bookings)
- `1` = Synced to server
- `2` = Updated locally (completion/payment changes pending sync)

#### b. **Settings Table**
```sql
CREATE TABLE IF NOT EXISTS Settings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    admin_id TEXT,
    type_1 TEXT,
    type_1_amount REAL,
    type_2 TEXT,
    type_2_amount REAL,
    advance_payment_enabled INTEGER DEFAULT 0,
    default_advance_percentage REAL DEFAULT 0,
    last_synced TEXT
);
```

#### c. **HourlyPricing Table**
```sql
CREATE TABLE IF NOT EXISTS HourlyPricing (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    admin_id TEXT,
    min_hours INTEGER,
    max_hours INTEGER,
    amount REAL,
    last_synced TEXT
);
```

#### d. **workers_summary Table**
```sql
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
    login_time TEXT,
    created_time TEXT,
    closed_time TEXT,
    last_updated_timestamp TEXT,
    status TEXT DEFAULT 'active',
    is_synced INTEGER DEFAULT 0
);
```

---

## Database Schema

### Booking Model - `Booking1.cs`
```csharp
public class Booking1
{
    public string? booking_id { get; set; }                // PRIMARY KEY
    public string? worker_id { get; set; }
    public string? guest_name { get; set; }
    public string? phone_number { get; set; }
    public int number_of_persons { get; set; }
    public string? booking_type { get; set; }              // Sitting or Sleeping
    public string? room_number { get; set; }              // For Sleeper bookings
    public int total_hours { get; set; }
    public DateTime booking_date { get; set; }
    public TimeSpan in_time { get; set; }
    public TimeSpan? out_time { get; set; }               // Nullable initially
    public string? proof_type { get; set; }               // ID type (Aadhaar, etc)
    public string? proof_id { get; set; }                 // ID number
    public decimal price_per_person { get; set; }
    public decimal total_amount { get; set; }
    public decimal paid_amount { get; set; }
    public decimal balance_amount { get; set; }
    public string? payment_method { get; set; }           // Cash, UPI, etc
    public DateTime? created_at { get; set; }             // Auto-filled
    public DateTime? updated_at { get; set; }             // Auto-filled
    public string? status { get; set; }                   // Active, Completed, Cancelled
    public int IsSynced { get; set; } = 0;               // 0, 1, or 2
    public string? booked_by { get; set; }               // Worker who created it
    public string? closed_by { get; set; }               // Worker who completed it
    public string? balance_payment { get; set; }         // Balance payment method
}
```

### Worker Settings Models - `WorkerSettings.cs`

#### HallTypesResponse
```csharp
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
```

#### Type2Detail (Hourly Pricing Tiers)
```csharp
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
```

#### PrinterDetailsResponse
```csharp
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
```

---

## API Endpoints

### Base URL
```
https://railway-api-worker.artechnology.pro
```

### 1. **Authentication**

#### POST /api/Login/check
- **Purpose**: Authenticate user and retrieve worker/admin credentials
- **Request Body**:
  ```json
  {
    "username": "string",
    "password": "string"
  }
  ```
- **Response**:
  ```json
  {
    "worker_id": "string",
    "admin_id": "string"
  }
  ```
- **Implementation**: `Login.xaml.cs` - Line 184
- **Stored Values** (8-hour expiry):
  - `workerId`
  - `adminId`
  - `username`

---

### 2. **Booking Management**

#### POST /api/Booking/create
- **Purpose**: Create new booking (online-first or sync offline bookings)
- **Request Body**: Array of `Booking1` objects
  ```json
  [
    {
      "booking_id": "unique-id",
      "worker_id": "worker-123",
      "guest_name": "John Doe",
      "phone_number": "9876543210",
      "number_of_persons": 2,
      "booking_type": "Sitting or Sleeping",
      "total_hours": 4,
      "booking_date": "2024-01-29",
      "in_time": "10:30:00",
      "out_time": null,
      "proof_type": "Aadhaar",
      "proof_id": "XXXX1234",
      "price_per_person": 100.00,
      "total_amount": 200.00,
      "paid_amount": 200.00,
      "balance_amount": 0.00,
      "payment_method": "Cash",
      "status": "active",
      "room_number": "101"
    }
  ]
  ```
- **Response**: Success/Failure status
- **Implementation**: `OfflineBookingStorage.cs`
  - Single booking: `SaveBookingAsync()` - Line 268
  - Batch sync: `SyncAllOfflineBookingsAsync()` - Line 408

#### PUT /api/Booking/checkout
- **Purpose**: Update booking completion (checkout) and payment
- **Request Body**:
  ```json
  {
    "booking_id": "unique-id",
    "out_time": "14:30:00",
    "status": "completed",
    "payment_method": "UPI"
  }
  ```
- **Implementation**: `SyncUpdatedBookingsAsync()` - Line 537
- **Notes**: Syncs bookings with `IsSynced = 2` (locally updated)

#### POST /api/Booking/online-book
- **Purpose**: Submit completed booking to API
- **Request Body**: Single `Booking1` object
- **Implementation**: `OfflineBookingStorage.cs` - Line 1029
- **Status**: Marks booking as synced in local DB

#### GET /api/Booking/completed-today
- **Request**: POST with admin credentials
- **Purpose**: Fetch completed bookings for the day
- **Implementation**: `GetCompletedBookingsApiUrl` - Line 23
- **Used in**: Dashboard data sync

---

### 3. **Settings Management**

#### GET /api/Settings/hall-types/{adminId}
- **Purpose**: Fetch hall types and pricing settings
- **Response Model**: `HallTypesResponse`
- **Stores In**: Settings table
- **Implementation**: `FetchAndSaveWorkerSettingsAsync()` - Line 1417
- **Called During**: Login process

#### GET /api/Settings/sleeping-details/{adminId}
- **Purpose**: Fetch hourly pricing tiers for sleeping bookings
- **Response Model**: Array of `Type2Detail`
- **Stores In**: HourlyPricing table
- **Implementation**: `FetchType2DetailsAsync()` - Line 1682
- **Called During**: Login process

#### GET /api/Settings/printer-details/{adminId}
- **Purpose**: Fetch printer configuration for receipt printing
- **Response Model**: `PrinterDetailsResponse`
- **Implementation**: `FetchAndSavePrinterDetailsAsync()` - Line 1612
- **Called During**: Login process

#### GET /api/Settings/worker-balance/{workerId}/{adminId}
- **Purpose**: Fetch current worker's balance amount from server
- **Response Model**:
  ```json
  {
    "balance": 0.00
  }
  ```
- **Implementation**: `Dashboard.xaml.cs` - `FetchWorkerBalanceAsync()` - Line 171
- **Called During**: Dashboard refresh
- **Side Effects**: When balance = 0, closes active worker session and resets booking count

---

### 4. **Worker Summary Management**

#### POST /api/worker-summary/sync
- **Purpose**: Sync completed worker session summaries to server
- **Request Body**: Worker summary object
  ```json
  {
    "admin_id": "admin-123",
    "worker_id": "worker-456",
    "total_booking": 15,
    "total_person": 35,
    "sitting_booking_count": 8,
    "sleeping_booking_count": 7,
    "sitting_booking_total_amount": 4500.00,
    "sleeping_total_amount": 8700.00,
    "in_cash_collect": 8200.00,
    "in_upi_collect": 5000.00,
    "total_balance": 1250.00,
    "login_time": "2024-01-29 08:00:00",
    "created_time": "2024-01-29 08:00:15",
    "closed_time": "2024-01-29 18:30:00",
    "status": "completed",
    "is_synced": 0
  }
  ```
- **Implementation**: `SyncWorkerSummariesAsync()` - Line 2457
- **Called During**: Manual refresh button click on Dashboard
- **Sync Condition**: Only syncs summaries with `status='completed'` and `is_synced=0`
- **After Sync**: Marks local record with `is_synced=1`

---

## Data Flow

### 1. **Login Flow**
```
User Login → API /Login/check 
  ↓
Validate Credentials → Get worker_id, admin_id
  ↓
Save to LocalStorage (8-hour expiry)
  ↓
Fetch Settings from API
  ├── /Settings/hall-types/{adminId}
  ├── /Settings/sleeping-details/{adminId}
  └── /Settings/printer-details/{adminId}
  ↓
Initialize Worker Session (workers_summary table)
  ↓
Dashboard Ready
```

### 2. **Create Booking Flow (Online-First)**
```
User Creates Booking → SaveBookingAsync()
  ↓
Network Check?
  ├─ YES → POST /api/Booking/create
  │   ├─ Success → Save to DB with IsSynced=1
  │   └─ Failure → Save to DB with IsSynced=0
  └─ NO → Save to DB with IsSynced=0
```

### 3. **Sync Offline Bookings Flow**
```
Sync Triggered → SyncAllOfflineBookingsAsync()
  ↓
Fetch IsSynced=0 bookings from DB
  ↓
Batch Process (50 bookings/batch)
  ├─ For each batch:
  │   ├─ POST /api/Booking/create
  │   ├─ Success → Update IsSynced=1
  │   └─ Failure → Log & break
  └─ Wait 5 seconds between batches
```

### 4. **Complete Booking Flow**
```
User Marks Booking as Complete
  ↓
CompleteBookingWithPaymentAsync()
  ↓
Update Booking in DB:
├─ If IsSynced=0 → Keep IsSynced=0
├─ If IsSynced=1 → Set IsSynced=2
  ↓
Update Worker Summary:
├─ Calculate balance paid (paidAmount - initialPaid)
├─ Update sitting_booking_total_amount or sleeping_total_amount
├─ Update in_cash_collect or in_upi_collect based on payment_method
└─ Subtract balance from total_balance
  ↓
Background Sync (if online & IsSynced was 1)
  ├─ PUT /api/Booking/checkout
  └─ Update completed in DB
```

### 5. **Worker Summary Tracking Flow**
```
Worker Login → GetOrCreateActiveWorkerSummary()
  ↓
Create New Booking → SaveOffline() or SaveBookingAsync()
  ↓
UpdateWorkerSummaryOnBooking() called:
├─ Increment total_booking by 1
├─ Add number_of_persons to total_person
├─ If booking_type = "sitting":
│   ├─ Increment sitting_booking_count
│   └─ Add total_amount to sitting_booking_total_amount
├─ If booking_type = "sleeper":
│   ├─ Increment sleeping_booking_count
│   └─ Add total_amount to sleeping_total_amount
├─ If payment_method = "cash":
│   └─ Add paid_amount to in_cash_collect
├─ If payment_method = "upi" or "online":
│   └─ Add paid_amount to in_upi_collect
└─ Add balance_amount to total_balance
  ↓
Complete Booking with Balance Payment → CompleteBookingWithPaymentAsync()
  ↓
Update Worker Summary:
├─ Add balance payment to sitting/sleeping totals based on booking_type
├─ Add balance payment to cash/upi based on payment_method
└─ Subtract paid balance from total_balance
  ↓
Admin Closes Balance (API returns balance=0)
  ↓
FetchWorkerBalanceAsync() detects balance=0
  ↓
CloseWorkerSession():
├─ Set status = 'completed'
├─ Set closed_time = current timestamp
├─ Mark is_synced = 0
└─ Ready for sync to server
  ↓
Next Booking Creates New Active Session
```

---

## Key Methods Reference

### OfflineBookingStorage Methods

| Method | Purpose | Returns |
|--------|---------|---------|
| `SaveBookingAsync(booking, showMessages)` | Save booking with online-first approach | bool |
| `SyncAllOfflineBookingsAsync(apiUrl, showMessages)` | Sync all pending bookings (IsSynced=0) | int (count) |
| `SyncUpdatedBookingsAsync(apiUrl, showMessages)` | Sync booking updates (IsSynced=2) | int (count) |
| `GetBookingById(bookingId)` | Retrieve booking from DB | Booking1 |
| `CompleteBookingWithPaymentAsync(...)` | Mark booking as completed with payment | string (status) |
| `FetchAndSaveWorkerSettingsAsync(adminId, apiUrl)` | Fetch and cache settings | bool |
| `FetchType2DetailsAsync(adminId, apiUrl)` | Fetch hourly pricing tiers | List<Type2Detail> |
| `FetchAndSavePrinterDetailsAsync(adminId, apiUrl)` | Fetch printer config | bool |
| `GetOrCreateActiveWorkerSummary(workerId, adminId)` | Get or create active worker session | int (s_no) |
| `UpdateWorkerSummaryOnBooking(workerId, adminId, booking)` | Update summary when booking created | void |
| `UpdateWorkerSummaryOnBalancePayment(...)` | Update summary when balance paid | void |
| `CloseWorkerSession(workerId, adminId)` | Close active worker session | bool |
| `GetActiveWorkerSummary(workerId, adminId)` | Get current active session data | WorkerSummaryRecord? |
| `SyncWorkerSummariesAsync()` | Sync completed sessions to server | int (count) |

---

## Error Handling

### Connection Errors
- Network unavailable → Save offline, display "No internet" message
- API timeout (10s) → Save offline, display "Connection Error"
- Server rejection (non-200) → Log error, retry on next sync

### Validation
- Booking ID must be unique
- Out time cannot be earlier than in time
- Completed bookings cannot be modified
- Total hours calculated as `Math.Ceiling((outTime - inTime).TotalHours)` minimum 1

### Logging
- All operations logged via `Logger.Log()` and `Logger.LogError()`
- Log location: `Logs/Logger.cs`

---

## Local Storage Keys

Managed by `LocalStorage` class:

| Key | Expiry | Usage |
|-----|--------|-------|
| `workerId` | 8 hours | Worker identification |
| `adminId` | 8 hours | Hall/Admin identification |
| `username` | 8 hours | Display username |
| `workerName` | 8 hours | Worker name (booked_by field) |

---

## Database File Locations

| Database | Location |
|----------|----------|
| Legacy Bookings DB | `AppData\Local\Railax\Data\bookings.db` |
| Offline Bookings DB | `AppData\Local\Railax\offline_bookings.db` |
| Logs | `Logs/` directory |

---

## Notes

1. **Offline-First Architecture**: All bookings can be created offline and synced when connection is available
2. **Batch Processing**: Large sync operations are batched (50 bookings per batch) with 5-second delays
3. **IsSynced States**: Enable tracking of booking sync status:
   - 0 = Never synced
   - 1 = Currently synced (server version exists)
   - 2 = Locally modified after syncing
4. **Auto-population**: `created_at`, `updated_at`, `booked_by` fields auto-filled on save
5. **Session Tracking**: Worker sessions tracked in `workers_summary` table for reporting
6. **Worker Session**: Initialized on login, tracks total bookings, earnings, and session duration
