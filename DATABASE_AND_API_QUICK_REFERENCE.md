# Quick Reference - Database Tables & API Endpoints

## 📊 Database Tables Summary

### OfflineBookingStorage.cs Database (`offline_bookings.db`)

```
┌─────────────────────────────────────────────────────────────┐
│                    BOOKINGS TABLE                            │
├─────────────────────────────────────────────────────────────┤
│ booking_id (PK) │ worker_id │ guest_name │ phone_number     │
│ number_of_persons │ booking_type │ total_hours              │
│ booking_date │ in_time │ out_time │ proof_type │ proof_id   │
│ price_per_person │ total_amount │ paid_amount              │
│ balance_amount │ payment_method │ created_at │ updated_at  │
│ status │ IsSynced (0/1/2) │ room_number │ booked_by        │
│ closed_by │ balance_payment_method                         │
└─────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────┐
│        SETTINGS TABLE                │
├──────────────────────────────────────┤
│ id (PK) │ admin_id │ type_1         │
│ type_1_amount │ type_2 │ type_2_amount │
│ advance_payment_enabled              │
│ default_advance_percentage           │
│ last_synced                          │
└──────────────────────────────────────┘

┌────────────────────────────────────────┐
│     HOURLY PRICING TABLE               │
├────────────────────────────────────────┤
│ id (PK) │ admin_id │ min_hours       │
│ max_hours │ amount │ last_synced     │
└────────────────────────────────────────┘

┌──────────────────────────────────────────────────┐
│        WORKERS_SUMMARY TABLE                     │
├──────────────────────────────────────────────────┤
│ s_no (PK) │ admin_id │ worker_id                │
│ total_booking │ total_person │ sitting_booking_count │
│ sleeping_booking_count │ sitting_total_amount   │
│ sleeping_total_amount │ in_cash_collect        │
│ in_upi_collect │ total_balance │ login_time   │
│ created_time │ closed_time │ last_updated_timestamp │
│ status │ is_synced                            │
└──────────────────────────────────────────────────┘
```

### BookingDatabase.cs Database (`bookings.db`)

```
┌──────────────────────────────────────────┐
│          LEGACY BOOKINGS TABLE           │
├──────────────────────────────────────────┤
│ BookingId (PK) │ Name │ PhoneNo         │
│ SeatType │ StartTime │ EndTime         │
│ PaymentType │ Status                   │
└──────────────────────────────────────────┘
```

---

## 🌐 API Endpoints Architecture

```
╔════════════════════════════════════════════════════════════════╗
║     BASE URL: https://railway-api-worker.artechnology.pro     ║
╚════════════════════════════════════════════════════════════════╝

┌─────────────────────────────────────────────────────────────────┐
│ 🔐 AUTHENTICATION                                               │
├─────────────────────────────────────────────────────────────────┤
│ POST /api/Login/check                                           │
│ ├─ Input: { username, password }                              │
│ └─ Output: { worker_id, admin_id }                            │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ 📋 BOOKING OPERATIONS                                           │
├─────────────────────────────────────────────────────────────────┤
│ POST /api/Booking/create                                        │
│ ├─ Input: Booking1[] (batch)                                   │
│ ├─ Method: SaveBookingAsync() or SyncAllOfflineBookingsAsync() │
│ └─ Used For: Create/sync new bookings                          │
│                                                                 │
│ PUT /api/Booking/checkout                                       │
│ ├─ Input: { booking_id, out_time, status, payment_method }    │
│ ├─ Method: SyncUpdatedBookingsAsync()                          │
│ └─ Used For: Complete booking & update payment                │
│                                                                 │
│ POST /api/Booking/online-book                                   │
│ ├─ Input: Booking1 (completed booking)                         │
│ └─ Used For: Submit offline completed bookings                │
│                                                                 │
│ GET /api/Booking/completed-today                                │
│ └─ Used For: Fetch day's completed bookings                    │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ ⚙️ SETTINGS MANAGEMENT                                          │
├─────────────────────────────────────────────────────────────────┤
│ GET /api/Settings/hall-types/{adminId}                          │
│ ├─ Response Model: HallTypesResponse                           │
│ └─ Stores: Settings table                                      │
│                                                                 │
│ GET /api/Settings/sleeping-details/{adminId}                    │
│ ├─ Response Model: Type2Detail[]                               │
│ └─ Stores: HourlyPricing table                                 │
│                                                                 │
│ GET /api/Settings/printer-details/{adminId}                     │
│ ├─ Response Model: PrinterDetailsResponse                      │
│ └─ Used For: Receipt printing configuration                    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🔄 Data Sync Flow Diagram

```
                           ┌─────────────────┐
                           │   USER LOGIN    │
                           └────────┬────────┘
                                    │
                    POST /Login/check (username, password)
                                    │
                                    ▼
                    ┌──────────────────────────────┐
                    │  Validate & Get Credentials  │
                    │  ├─ worker_id               │
                    │  └─ admin_id                │
                    └────────┬─────────────────────┘
                             │
                             │ Save to LocalStorage (8h)
                             │
                    ┌────────▼──────────┐
                    │ Fetch Settings    │
                    │ ├─ Hall Types     │
                    │ ├─ Pricing Tiers  │
                    │ └─ Printer Config │
                    └────────┬──────────┘
                             │
                    ┌────────▼──────────────┐
                    │ Init Worker Session  │
                    │ (workers_summary)    │
                    └────────┬──────────────┘
                             │
                             ▼
                    ┌─────────────────────┐
                    │  DASHBOARD READY    │
                    └─────────────────────┘


                   ┌──────────────────────────┐
                   │   CREATE NEW BOOKING     │
                   └────────┬─────────────────┘
                            │
                   ┌────────▼──────────┐
                   │ Check Internet?   │
                   └────────┬──────────┘
                     ┌──────┴──────┐
                     │             │
                  YES│             │NO
                     ▼             ▼
              ┌─────────────┐ ┌──────────────┐
              │ POST to API │ │ Save Offline │
              │   /create   │ │ (IsSynced=0) │
              └──────┬──────┘ └──────────────┘
                     │
              ┌──────┴──────┐
              │             │
          SUCCESS│     │FAILURE
              ▼      ▼
         ┌─────────────────┐
         │ IsSynced = 1    │ ← Retry on next sync
         └─────────────────┘


              ┌─────────────────────┐
              │ SYNC ALL OFFLINE    │
              │ (IsSynced = 0)      │
              └──────────┬──────────┘
                         │
           ┌─────────────▼──────────────┐
           │ Batch Process              │
           │ (50 bookings per batch)    │
           │ ├─ POST to /api/Booking    │
           │ ├─ Update IsSynced = 1     │
           │ ├─ 5 sec delay             │
           │ └─ Repeat until done       │
           └────────────────────────────┘


         ┌──────────────────────────┐
         │ MARK BOOKING COMPLETED   │
         └────────┬─────────────────┘
                  │
         ┌────────▼───────┐
         │ Check IsSynced │
         └────────┬───────┘
            ┌─────┴─────┐
            │           │
         0 │           │ 1
           ▼           ▼
      ┌────────┐  ┌──────────┐
      │Stay 0  │  │Set to 2  │
      └────────┘  └────┬─────┘
                       │
                  (if online)
                       │
                 PUT /Booking/checkout
                       │
                       ▼
                   Update API
```

---

## 📑 Key Models & Structures

### Booking1 Model Fields
```
Core Info:       booking_id, worker_id, guest_name, phone_number
Occupancy:       number_of_persons, booking_type (Sitting/Sleeping)
Room:            room_number
Duration:        total_hours, booking_date, in_time, out_time
ID/Proof:        proof_type, proof_id
Pricing:         price_per_person, total_amount, paid_amount, balance_amount
Payment:         payment_method, balance_payment
Status:          status (active/completed), IsSynced (0/1/2)
Audit:           created_at, updated_at, booked_by, closed_by
```

### HallTypesResponse Fields
```
Sitting Type:    Type1 (name), Type1Amount (price)
Sleeping Type:   Type2 (name), GraceAmount, GraceAmountType2
Advance:         AdvancePaymentEnabled, AdvancePayment (percentage)
```

### Type2Detail Fields
```
Duration Tier:   MinDuration, MaxDuration (in hours)
Pricing:         Amount (per hour range)
```

### PrinterDetailsResponse Fields
```
Headers:         Heading1, Heading2
Info:            Info1, Info2, Note
Branding:        HallName, LogoUrl
```

---

## 🔑 LocalStorage Keys (8-hour expiry)

| Key | Type | Usage |
|-----|------|-------|
| `workerId` | string | Identify current worker |
| `adminId` | string | Identify hall/admin |
| `username` | string | Display login name |
| `workerName` | string | Populate `booked_by` field |

---

## ⏱️ IsSynced Status Values

| Value | Meaning | Action |
|-------|---------|--------|
| **0** | Never synced | POST to /api/Booking/create |
| **1** | Synced on server | No action needed |
| **2** | Updated locally after sync | PUT to /api/Booking/checkout |

---

## 📍 File Locations

| File | Location | Purpose |
|------|----------|---------|
| `BookingDatabase.cs` | `Data/` | Legacy SQLite DB |
| `OfflineBookingStorage.cs` | `Storage/` | Offline-first storage |
| `BookingService.cs` | `Services/` | Service layer (legacy) |
| `Booking.cs` | `Models/` | Legacy model |
| `Booking1.cs` | `Models/` | Current model |
| `WorkerSettings.cs` | `Models/` | Settings models |

---

## 🚀 Main Entry Points

| Class | Method | Trigger |
|-------|--------|---------|
| `Login.xaml.cs` | `Login_Click()` | User login |
| `Dashboard.xaml.cs` | `LoadBookings()` | Dashboard init |
| `OfflineBookingStorage` | `SaveBookingAsync()` | Create booking |
| `OfflineBookingStorage` | `SyncAllOfflineBookingsAsync()` | Manual/auto sync |
| `OfflineBookingStorage` | `CompleteBookingWithPaymentAsync()` | Checkout |
