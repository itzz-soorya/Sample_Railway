# Worker Summary Mapping System

## Overview
The Worker Summary Mapping System automatically tracks and aggregates booking statistics for each worker session. It maintains real-time counts and financial data from booking creation through completion and balance payment.

---

## Database Schema

### workers_summary Table
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

### Field Descriptions

| Column | Type | Purpose |
|--------|------|---------|
| `s_no` | INTEGER | Primary key (auto-increment) |
| `admin_id` | TEXT | Hall/Admin ID this session belongs to |
| `worker_id` | TEXT | Worker ID for this session |
| `total_booking` | INTEGER | Count of all bookings in this session |
| `total_person` | INTEGER | Sum of all persons across bookings |
| `sitting_booking_count` | INTEGER | Count of "sitting" type bookings |
| `sleeping_booking_count` | INTEGER | Count of "sleeper" type bookings |
| `sitting_booking_total_amount` | REAL | Total revenue from sitting bookings |
| `sleeping_total_amount` | REAL | Total revenue from sleeping bookings |
| `in_cash_collect` | REAL | Total cash collected (initial + balance) |
| `in_upi_collect` | REAL | Total UPI/online collected (initial + balance) |
| `total_balance` | REAL | Outstanding balance across all bookings |
| `login_time` | TEXT | When worker logged in |
| `created_time` | TEXT | When session record was created |
| `closed_time` | TEXT | When session was closed (NULL if active) |
| `last_updated_timestamp` | TEXT | Last update time |
| `status` | TEXT | "active" or "completed" |
| `is_synced` | INTEGER | 0 = not synced, 1 = synced to server |

---

## Mapping Logic

### 1. Session Creation (Login)
**Trigger**: Worker logs in  
**Method**: `GetOrCreateActiveWorkerSummary(workerId, adminId)`  
**Action**: 
- Checks if an active session exists for worker
- If not, creates new record with:
  - `status = 'active'`
  - `login_time = current timestamp`
  - All counters initialized to 0

### 2. Booking Creation
**Trigger**: New booking saved via `SaveOffline()` or `SaveBookingAsync()`  
**Method**: `UpdateWorkerSummaryOnBooking(workerId, adminId, booking)`  
**Action**:
```
1. Increment total_booking by 1
2. Add booking.number_of_persons to total_person

3. IF booking.booking_type = "sitting":
   - Increment sitting_booking_count by 1
   - Add booking.total_amount to sitting_booking_total_amount
   
4. ELSE IF booking.booking_type = "sleeper":
   - Increment sleeping_booking_count by 1
   - Add booking.total_amount to sleeping_total_amount

5. IF booking.payment_method = "cash":
   - Add booking.paid_amount to in_cash_collect
   
6. ELSE IF booking.payment_method = "upi" OR "online":
   - Add booking.paid_amount to in_upi_collect

7. Add booking.balance_amount to total_balance
8. Set last_updated_timestamp = current time
9. Set is_synced = 0 (mark for sync)
```

### 3. Balance Payment (Booking Completion)
**Trigger**: Booking completed with balance payment via `CompleteBookingWithPaymentAsync()`  
**Method**: Internal worker summary update logic  
**Action**:
```
1. Calculate balance_paid = paidAmount - initialPaid

2. IF balance_paid > 0:
   a. Get booking_type from booking record
   
   b. IF booking_type = "sitting":
      - Add balance_paid to sitting_booking_total_amount
   
   c. ELSE IF booking_type = "sleeper":
      - Add balance_paid to sleeping_total_amount
   
   d. IF payment_method = "cash":
      - Add balance_paid to in_cash_collect
   
   e. ELSE IF payment_method = "upi" OR "online":
      - Add balance_paid to in_upi_collect
   
   f. Subtract balance_paid from total_balance
   
   g. Set last_updated_timestamp = current time
   h. Set is_synced = 0
```

### 4. Session Closure
**Trigger**: Admin closes balance (API returns balance=0)  
**Method**: `CloseWorkerSession(workerId, adminId)` called from `Dashboard.FetchWorkerBalanceAsync()`  
**Action**:
```
1. Find active session (status='active')
2. Update record:
   - status = 'completed'
   - closed_time = current timestamp
   - last_updated_timestamp = current time
   - is_synced = 0
3. Ready for sync to server
```

### 5. Server Synchronization
**Trigger**: Manual refresh button click on Dashboard  
**Method**: `SyncWorkerSummariesAsync()`  
**Action**:
```
1. Find all completed, unsynced sessions:
   - WHERE status='completed' AND is_synced=0

2. For each session:
   - POST to /api/worker-summary/sync
   - If success: Update is_synced = 1
   - If failure: Log error and continue

3. Return count of successfully synced sessions
```

---

## Example Scenario

### Scenario: Worker creates 3 bookings and completes balance

#### Initial State (Login)
```
workers_summary:
├─ s_no: 1
├─ worker_id: "WORKER001"
├─ admin_id: "HALL001"
├─ total_booking: 0
├─ total_person: 0
├─ sitting_booking_count: 0
├─ sleeping_booking_count: 0
├─ sitting_booking_total_amount: 0.00
├─ sleeping_total_amount: 0.00
├─ in_cash_collect: 0.00
├─ in_upi_collect: 0.00
├─ total_balance: 0.00
└─ status: "active"
```

#### Step 1: Create Sitting Booking #1
```
Booking Details:
- booking_type: "sitting"
- number_of_persons: 2
- total_amount: 500.00
- paid_amount: 500.00
- balance_amount: 0.00
- payment_method: "cash"

Updated Summary:
├─ total_booking: 1 ✓
├─ total_person: 2 ✓
├─ sitting_booking_count: 1 ✓
├─ sitting_booking_total_amount: 500.00 ✓
├─ in_cash_collect: 500.00 ✓
└─ total_balance: 0.00
```

#### Step 2: Create Sleeper Booking #2
```
Booking Details:
- booking_type: "sleeper"
- number_of_persons: 1
- total_amount: 800.00
- paid_amount: 400.00 (50% advance)
- balance_amount: 400.00
- payment_method: "upi"

Updated Summary:
├─ total_booking: 2 ✓
├─ total_person: 3 ✓
├─ sleeping_booking_count: 1 ✓
├─ sleeping_total_amount: 800.00 ✓
├─ in_upi_collect: 400.00 ✓
└─ total_balance: 400.00 ✓
```

#### Step 3: Create Sitting Booking #3
```
Booking Details:
- booking_type: "sitting"
- number_of_persons: 3
- total_amount: 750.00
- paid_amount: 500.00
- balance_amount: 250.00
- payment_method: "cash"

Updated Summary:
├─ total_booking: 3 ✓
├─ total_person: 6 ✓
├─ sitting_booking_count: 2 ✓
├─ sitting_booking_total_amount: 1250.00 ✓
├─ in_cash_collect: 1000.00 ✓
└─ total_balance: 650.00 ✓
```

#### Step 4: Complete Booking #2 with Balance Payment
```
Completion Details:
- booking_id: Booking #2
- initial_paid: 400.00
- final_paid: 800.00
- balance_paid: 400.00
- payment_method: "cash"
- booking_type: "sleeper"

Updated Summary:
├─ sleeping_total_amount: 800.00 (no change, already counted)
├─ in_cash_collect: 1400.00 ✓ (1000 + 400)
├─ in_upi_collect: 400.00 (no change)
└─ total_balance: 250.00 ✓ (650 - 400)
```

#### Step 5: Complete Booking #3 with Balance Payment
```
Completion Details:
- booking_id: Booking #3
- balance_paid: 250.00
- payment_method: "upi"
- booking_type: "sitting"

Updated Summary:
├─ sitting_booking_total_amount: 1250.00 (no change)
├─ in_cash_collect: 1400.00 (no change)
├─ in_upi_collect: 650.00 ✓ (400 + 250)
└─ total_balance: 0.00 ✓ (250 - 250)
```

#### Step 6: Admin Closes Balance
```
API Response:
- balance: 0.00

Action Taken:
- CloseWorkerSession() called
- status: "completed"
- closed_time: "2024-01-29 18:00:00"
- is_synced: 0
```

#### Final State
```
workers_summary:
├─ s_no: 1
├─ worker_id: "WORKER001"
├─ admin_id: "HALL001"
├─ total_booking: 3
├─ total_person: 6
├─ sitting_booking_count: 2
├─ sleeping_booking_count: 1
├─ sitting_booking_total_amount: 1250.00
├─ sleeping_total_amount: 800.00
├─ in_cash_collect: 1400.00
├─ in_upi_collect: 650.00
├─ total_balance: 0.00
├─ status: "completed"
├─ closed_time: "2024-01-29 18:00:00"
└─ is_synced: 0 (ready to sync)
```

---

## API Integration

### POST /api/worker-summary/sync
**Request Body**:
```json
{
  "admin_id": "HALL001",
  "worker_id": "WORKER001",
  "total_booking": 3,
  "total_person": 6,
  "sitting_booking_count": 2,
  "sleeping_booking_count": 1,
  "sitting_booking_total_amount": 1250.00,
  "sleeping_total_amount": 800.00,
  "in_cash_collect": 1400.00,
  "in_upi_collect": 650.00,
  "total_balance": 0.00,
  "login_time": "2024-01-29 08:00:00",
  "created_time": "2024-01-29 08:00:15",
  "closed_time": "2024-01-29 18:00:00",
  "status": "completed",
  "is_synced": 0
}
```

**Expected Response**: Success/Failure indicator

**After Success**: Local record updated with `is_synced = 1`

---

## Implementation Files

### Core Implementation
- **OfflineBookingStorage.cs** (`Storage/`)
  - Lines 34-209: `workers_summary` table creation
  - Lines 2050-2107: `GetOrCreateActiveWorkerSummary()`
  - Lines 2139-2188: `UpdateWorkerSummaryOnBooking()`
  - Lines 2197-2232: `UpdateWorkerSummaryOnBalancePayment()`
  - Lines 2240-2268: `CloseWorkerSession()`
  - Lines 2457-2520: `SyncWorkerSummariesAsync()`
  - Line 261: SaveOffline() calls UpdateWorkerSummaryOnBooking()
  - Line 1207: CompleteBookingWithPaymentAsync() includes balance tracking

### UI Integration
- **Dashboard.xaml.cs** (`Views/`)
  - Lines 171-228: `FetchWorkerBalanceAsync()` - Closes session when balance=0
  - Line 811: RefreshButton_Click calls `SyncWorkerSummariesAsync()`

### Data Models
- **WorkerSummaryRecord** (defined at end of `OfflineBookingStorage.cs`)
  - Properties map directly to `workers_summary` table columns

---

## Testing Checklist

### Test Case 1: Basic Booking Flow
- [ ] Worker login creates active session
- [ ] Create sitting booking with cash payment
- [ ] Verify total_booking incremented
- [ ] Verify sitting_booking_count incremented
- [ ] Verify in_cash_collect updated
- [ ] Verify sitting_booking_total_amount updated

### Test Case 2: Mixed Booking Types
- [ ] Create sitting booking
- [ ] Create sleeper booking
- [ ] Verify both counters increment independently
- [ ] Verify amounts segregated correctly

### Test Case 3: Balance Payment
- [ ] Create booking with 50% advance
- [ ] Complete booking with balance payment
- [ ] Verify cash/UPI totals updated correctly
- [ ] Verify total_balance decremented
- [ ] Verify booking type totals remain unchanged

### Test Case 4: Session Closure
- [ ] Admin marks balance as closed (balance=0)
- [ ] Verify session status changed to "completed"
- [ ] Verify closed_time set
- [ ] Verify is_synced = 0

### Test Case 5: Server Sync
- [ ] Close worker session
- [ ] Click refresh button
- [ ] Verify POST to /api/worker-summary/sync
- [ ] Verify is_synced updated to 1

---

## Troubleshooting

### Issue: Worker summary not updating
**Check**:
1. Is worker_id and admin_id set in LocalStorage?
2. Is GetOrCreateActiveWorkerSummary() returning valid s_no?
3. Check Logs/ folder for error messages

### Issue: Balance not decreasing
**Check**:
1. Is CompleteBookingWithPaymentAsync() being called?
2. Is balance_paid calculation correct?
3. Check if booking has valid booking_type

### Issue: Session not closing
**Check**:
1. Is API returning balance=0?
2. Is FetchWorkerBalanceAsync() running?
3. Check if CloseWorkerSession() is being called

### Issue: Sync failing
**Check**:
1. Is network available?
2. Is API endpoint `/api/worker-summary/sync` correct?
3. Check server logs for rejection reasons

---

## Future Enhancements

1. **Real-time Dashboard Widget**: Display current session stats on Dashboard
2. **Historical Reports**: Query past sessions with date range filters
3. **Multiple Payment Methods**: Support for card, wallet, etc.
4. **Session Pause/Resume**: Allow workers to take breaks within a session
5. **Automatic Session Closure**: Close sessions after X hours of inactivity
