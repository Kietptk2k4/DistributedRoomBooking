// BookingServer/ServerState.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using BC = BCrypt.Net.BCrypt;

namespace BookingServer;

// Trạng thái 1 slot (phòng + ca)
class SlotState
{
    public bool IsBusy { get; set; }
    public string? CurrentHolderClientId { get; set; }   // ai đang giữ slot này

    // NEW: Booking tương ứng với slot này (nếu đã tạo record Booking)
    public Guid? CurrentBookingId { get; set; }

    public Queue<(string clientId, NetworkStream stream)> WaitingQueue { get; } = new();
}

// Dùng để hiển thị lên DataGridView trên UI server
public class SlotSummary
{
    public string Date { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string SlotId { get; set; } = "";   // "S1" .. "S14"
    public string Status { get; set; } = "";   // "FREE" / "BUSY"
    public string Holder { get; set; } = "";   // Client đang giữ (nếu có)
    public int QueueLength { get; set; }       // Số client đang chờ
}

class ServerState
{
    // ===== CẤU HÌNH PHÒNG + CA =====
    private static readonly string[] Rooms =
    {
        "A08","A16","A24","A32",
        "A21","A22","A23",
        "A24-A25","A31","A32-A33","A34-A35"
    };

    private const int SlotCount = 14; // ca 1..14

    // SlotId sẽ là "S1".."S14"
    private static string GetSlotId(int index) => $"S{index}";

    // ===== STATE THEO NGÀY =====
    // dateKey = "yyyy-MM-dd" -> (room::slot) -> SlotState
    private readonly Dictionary<string, Dictionary<string, SlotState>> _slotsByDate = new();
    private readonly object _lock = new();

    private string _currentDateKey = DateTime.Today.ToString("yyyy-MM-dd");
      // ===== DATA MÔ HÌNH THỰC TẾ (ROOMS / USERS / BOOKINGS) =====

    // Thông tin phòng (RoomInfo) key theo RoomId (A08, A16,...)
    private readonly Dictionary<string, RoomInfo> _rooms = new();

    // Thông tin người dùng (UserInfo) key theo UserId (sv001, gv001, admin,...)
    private readonly Dictionary<string, UserInfo> _users = new();

    // Danh sách booking "thực tế" – sẽ dùng ở các milestone sau
    private readonly List<Booking> _bookings = new();

    // Expose read-only cho UI/logic khác (dùng ở milestone sau)
    public IReadOnlyDictionary<string, RoomInfo> RoomsInfo => _rooms;
    public IReadOnlyDictionary<string, UserInfo> UsersInfo => _users;
    public IReadOnlyList<Booking> Bookings => _bookings;

    // Constructor: seed dữ liệu demo
    public ServerState()
    {
        InitDemoData();
    }

    // Cập nhật ngày hiện tại từ UI server
    public void SetCurrentDate(DateTime date, TextWriter log)
    {
        var key = date.ToString("yyyy-MM-dd");
        lock (_lock)
        {
            _currentDateKey = key;
            EnsureDateInitialized(key, log);
        }
    }

    /// Seed dữ liệu demo cho Rooms / Users.
    private void InitDemoData()
    {
        // Seed phòng từ mảng Rooms có sẵn
        foreach (var roomId in Rooms)
        {
            if (_rooms.ContainsKey(roomId)) continue;

            _rooms[roomId] = new RoomInfo
            {
                RoomId = roomId,
                Building = "CS1 - Tòa A",           // demo, sau này có thể tách theo cơ sở
                Capacity = 60,                      // demo
                HasProjector = true,                // giả sử phòng nào cũng có máy chiếu
                HasPC = roomId.StartsWith("A2", StringComparison.OrdinalIgnoreCase),
                HasAirConditioner = true,
                HasMic = roomId.StartsWith("A3", StringComparison.OrdinalIgnoreCase),
                Status = "ACTIVE"
            };
        }

        // Seed một vài user demo (Student / Lecturer / Staff)
        if (_users.Count == 0)
        {
            _users["sv001"] = new UserInfo
            {
                UserId = "sv001",
                UserType = "Student",
                FullName = "Nguyễn Văn A",
                StudentId = "N21DCCN001",
                Class = "D21CQCN01-N",
                Department = "CNTT",
                Email = "sv001@example.com",
                Phone = "0900000001",
                PasswordHash = BC.HashPassword("sv123"),
                IsActive = true
            };

            _users["sv002"] = new UserInfo
            {
                UserId = "sv002",
                UserType = "Student",
                FullName = "Trần Thị B",
                StudentId = "N21DCCN002",
                Class = "D21CQCN02-N",
                Department = "CNTT",
                Email = "sv002@example.com",
                Phone = "0900000002",
                PasswordHash = BC.HashPassword("sv123"),
                IsActive = true
            };

            _users["gv001"] = new UserInfo
            {
                UserId = "gv001",
                UserType = "Lecturer",
                FullName = "Thầy C",
                LecturerId = "GV001",
                Faculty = "Khoa CNTT",
                Email = "gv001@example.com",
                Phone = "0900000003",
                PasswordHash = BC.HashPassword("gv123"),
                IsActive = true
            };

            _users["admin"] = new UserInfo
            {
                UserId = "admin",
                UserType = "Staff",                  // dùng như Admin hệ thống
                FullName = "Phòng Đào Tạo",
                Department = "Phòng Đào Tạo",
                Email = "admin@example.com",
                Phone = "0900000004",
                PasswordHash = BC.HashPassword("admin123"),
                IsActive = true
            };
        }

        // _bookings: hiện tại để rỗng,
        // sẽ được thêm record khi GRANT/RELEASE ở Milestone 3.
    }

    private void EnsureDateInitialized(string dateKey, TextWriter log)
    {
        if (_slotsByDate.ContainsKey(dateKey)) return;

        var dict = new Dictionary<string, SlotState>();
        foreach (var room in Rooms)
        {
            for (int i = 1; i <= SlotCount; i++)
            {
                var slotId = GetSlotId(i);
                var key = MakeKey(room, slotId);
                dict[key] = new SlotState
                {
                    IsBusy = false,
                    CurrentHolderClientId = null
                };
            }
        }

        _slotsByDate[dateKey] = dict;
        log.WriteLine($"[INIT] Created slot state for date {dateKey}");
    }

    private Dictionary<string, SlotState> GetCurrentDateSlots(TextWriter log)
    {
        lock (_lock)
        {
            EnsureDateInitialized(_currentDateKey, log);
            return _slotsByDate[_currentDateKey];
        }
    }

    private string MakeKey(string roomId, string slotId) => $"{roomId}::{slotId}";

    // Lấy danh sách summary cho tất cả slot của ngày hiện tại -> hiển thị lên grid
    public List<SlotSummary> GetAllSlotSummaries()
    {
        var result = new List<SlotSummary>();

        lock (_lock)
        {
            if (!_slotsByDate.TryGetValue(_currentDateKey, out var dict))
                return result;

            foreach (var kvp in dict)
            {
                var key = kvp.Key;
                var slot = kvp.Value;

                var parts = key.Split(new[] { "::" }, StringSplitOptions.None);
                var roomId = parts[0];
                var slotId = parts.Length > 1 ? parts[1] : "?";

                result.Add(new SlotSummary
                {
                    Date = _currentDateKey,
                    RoomId = roomId,
                    SlotId = slotId,
                    Status = slot.IsBusy ? "BUSY" : "FREE",
                    Holder = slot.CurrentHolderClientId ?? "",
                    QueueLength = slot.WaitingQueue.Count
                });
            }
        }

        return result;
    }

    // Lấy queue cụ thể cho 1 (room, slot) của ngày hiện tại -> hiển thị chi tiết hàng đợi
    public List<string> GetQueueClients(string roomId, string slotId)
    {
        var key = MakeKey(roomId, slotId);
        lock (_lock)
        {
            if (!_slotsByDate.TryGetValue(_currentDateKey, out var dict))
                return new List<string>();

            if (!dict.TryGetValue(key, out var slot))
                return new List<string>();

            return slot.WaitingQueue.Select(q => q.clientId).ToList();
        }
    }

    // Chuyển "S3" -> index 3
    private int ParseSlotIndex(string slotId)
    {
        if (slotId.StartsWith("S", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(slotId.Substring(1), out int idx))
                return idx;
        }
        return -1;
    }

    // Tính giờ kết thúc ca (dùng cho mode RealTime)
    // Giả sử ca 1: 07:00-08:00, ca 2: 08:00-09:00, ... ca 14: 20:00-21:00
    private DateTime GetSlotEndTime(string dateKey, string slotId)
    {
        var date = DateTime.Parse(dateKey); // yyyy-MM-dd
        int idx = ParseSlotIndex(slotId);
        if (idx <= 0) idx = 1;
        var start = date.Date.AddHours(7 + (idx - 1)); // ca1 = 7h
        var end = start.AddHours(1);
        return end;
    }

    // Kiểm tra cùng client có đang giữ slot trùng ca ở phòng khác hay không
    private bool HasCrossRoomConflict(string clientId, string dateKey, string roomIdNew, string slotIdNew,
        out string conflictedRoom)
    {
        conflictedRoom = "";
        if (!_slotsByDate.TryGetValue(dateKey, out var dict)) return false;

        int idxNew = ParseSlotIndex(slotIdNew);
        if (idxNew <= 0) return false;

        foreach (var kvp in dict)
        {
            var key = kvp.Key;
            var slot = kvp.Value;

            if (slot.CurrentHolderClientId != clientId) continue;

            var parts = key.Split(new[] { "::" }, StringSplitOptions.None);
            var roomId = parts[0];
            var slotId = parts.Length > 1 ? parts[1] : "?";

            if (roomId == roomIdNew) continue; // cùng phòng thì cho phép (đang xin ca khác trong cùng phòng)

            int idx = ParseSlotIndex(slotId);
            if (idx == idxNew)
            {
                conflictedRoom = roomId;
                return true;
            }
        }

        return false;
    }

    // REQUEST: cover
    // - Case cơ bản
    // - Tranh chấp
    // - REQUEST trùng lặp (ALREADY_HOLDER / ALREADY_QUEUED)
    // - Không cho giữ 2 phòng khác nhau cùng ca (theo clientId) trong cùng ngày
    // - Không cho đặt ca đã qua (RealTime)
    public void HandleRequest(string clientId, string roomId, string slotId, NetworkStream stream, TextWriter log)
    {
        lock (_lock)
        {
            EnsureDateInitialized(_currentDateKey, log);

            var dict = _slotsByDate[_currentDateKey];
            var key = MakeKey(roomId, slotId);

            if (!dict.TryGetValue(key, out var slot))
            {
                log.WriteLine($"[WARN] REQUEST invalid slot {roomId}-{slotId} by {clientId}");
                Send(stream, "INFO|ERROR|Invalid room/slot\n");
                return;
            }

            // 1) Chặn ca đã qua (mode RealTime đơn giản)
            var now = DateTime.Now;
            var endTime = GetSlotEndTime(_currentDateKey, slotId);
            if (endTime <= now)
            {
                log.WriteLine($"[WARN] REQUEST past slot {roomId}-{slotId} on date {_currentDateKey} by {clientId}");
                Send(stream, "INFO|ERROR|Slot already in the past\n");
                return;
            }

            // 2) Chặn giữ 2 phòng khác nhau cùng ca trong cùng ngày
            if (HasCrossRoomConflict(clientId, _currentDateKey, roomId, slotId, out var conflictedRoom))
            {
                log.WriteLine($"[WARN] REQUEST cross-room conflict: {clientId} already holds {conflictedRoom}-{slotId} on {_currentDateKey}");
                Send(stream, "INFO|ERROR|User already booked another room in that time range\n");
                return;
            }

            // 3) Nếu client đã là holder -> không cấp lại, chỉ báo INFO
            if (slot.CurrentHolderClientId == clientId)
            {
                log.WriteLine($"[INFO] REQUEST from holder {clientId} on {roomId}-{slotId} -> already granted");
                Send(stream, $"INFO|ALREADY_HOLDER|{roomId}|{slotId}\n");
                return;
            }

            // 4) Nếu client đã trong queue -> không enqueue thêm, chỉ báo INFO + pos
            int pos = 1;
            bool alreadyQueued = false;
            foreach (var w in slot.WaitingQueue)
            {
                if (w.clientId == clientId)
                {
                    alreadyQueued = true;
                    break;
                }
                pos++;
            }

            if (alreadyQueued)
            {
                log.WriteLine($"[INFO] REQUEST duplicate from {clientId} on {roomId}-{slotId} -> already queued at pos {pos}");
                Send(stream, $"INFO|ALREADY_QUEUED|{roomId}|{slotId}|{pos}\n");
                return;
            }

            // 5) Slot đang rảnh -> cấp quyền ngay
            if (!slot.IsBusy && string.IsNullOrEmpty(slot.CurrentHolderClientId))
            {
                slot.IsBusy = true;
                slot.CurrentHolderClientId = clientId;

                log.WriteLine($"[GRANT] {clientId} -> {roomId}-{slotId} on date {_currentDateKey}");
                Send(stream, $"GRANT|{roomId}|{slotId}\n");
            }
            else
            {
                // Slot đang bận -> cho vào queue (FIFO)
                slot.WaitingQueue.Enqueue((clientId, stream));
                var newPos = slot.WaitingQueue.Count;
                log.WriteLine($"[QUEUE] {clientId} -> {roomId}-{slotId} on date {_currentDateKey} (pos {newPos})");
                Send(stream, $"QUEUED|{roomId}|{slotId}|{newPos}\n");
            }
        }
    }

    /// <summary>
    /// RELEASE được gọi khi client bấm Hủy:
    /// - Nếu đang giữ quyền -> giải phóng / cấp cho queue tiếp theo.
    /// - Nếu đang trong queue -> xóa khỏi queue.
    /// - Nếu không liên quan -> báo lỗi.
    /// </summary>
    public void HandleRelease(string clientId, string roomId, string slotId, NetworkStream? replyStream, TextWriter log)
    {
        lock (_lock)
        {
            EnsureDateInitialized(_currentDateKey, log);
            var dict = _slotsByDate[_currentDateKey];
            var key = MakeKey(roomId, slotId);

            if (!dict.TryGetValue(key, out var slot))
            {
                log.WriteLine($"[WARN] RELEASE invalid slot {roomId}-{slotId} by {clientId}");
                if (replyStream != null)
                {
                    Send(replyStream, "INFO|ERROR|Invalid room/slot\n");
                }
                return;
            }

            // Trường hợp client đang là holder -> RELEASE hợp lệ
            if (slot.CurrentHolderClientId == clientId)
            {
                log.WriteLine($"[RELEASE] {clientId} -> {roomId}-{slotId} on {_currentDateKey}");

                // Thông báo cho client là đã RELEASE xong
                if (replyStream != null)
                {
                    Send(replyStream, $"INFO|RELEASED|{roomId}|{slotId}\n");
                }

                if (slot.WaitingQueue.Count == 0)
                {
                    slot.IsBusy = false;
                    slot.CurrentHolderClientId = null;
                    log.WriteLine($"[SLOT] {roomId}-{slotId} on {_currentDateKey} -> FREE");
                }
                else
                {
                    var (nextClientId, nextStream) = slot.WaitingQueue.Dequeue();
                    slot.IsBusy = true;
                    slot.CurrentHolderClientId = nextClientId;
                    log.WriteLine($"[GRANT] {nextClientId} (from queue) -> {roomId}-{slotId} on {_currentDateKey}");
                    Send(nextStream, $"GRANT|{roomId}|{slotId}\n");
                }

                return;
            }

            // Không phải holder -> thử xóa client khỏi queue (client đang chờ nhưng bấm Hủy)
            int removed = RemoveFromQueue(slot, clientId);
            if (removed > 0)
            {
                log.WriteLine($"[CANCEL] {clientId} removed from queue of {roomId}-{slotId} on {_currentDateKey} (entries {removed})");
                if (replyStream != null)
                {
                    Send(replyStream, $"INFO|CANCELLED|{roomId}|{slotId}\n");
                }
            }
            else
            {
                // Không giữ quyền, không nằm trong queue -> từ chối
                log.WriteLine($"[WARN] RELEASE from non-holder/non-queued {clientId} on {roomId}-{slotId} on {_currentDateKey}");
                if (replyStream != null)
                {
                    Send(replyStream, "INFO|ERROR|Not holder or queued\n");
                }
            }
        }
    }

    /// <summary>
    /// Được gọi khi 1 client mất kết nối:
    /// - Nếu đang là holder ở slot nào -> auto RELEASE slot đó.
    /// - Nếu đang trong queue ở slot nào -> loại khỏi queue.
    /// </summary>
    public void HandleDisconnect(string clientId, TextWriter log)
    {
        lock (_lock)
        {
            foreach (var dateEntry in _slotsByDate)
            {
                var dateKey = dateEntry.Key;
                var dict = dateEntry.Value;

                foreach (var kvp in dict)
                {
                    var key = kvp.Key;
                    var slot = kvp.Value;

                    var parts = key.Split(new[] { "::" }, StringSplitOptions.None);
                    var roomId = parts[0];
                    var slotId = parts.Length > 1 ? parts[1] : "?";

                    // Xóa khỏi queue nếu có
                    int removedFromQueue = RemoveFromQueue(slot, clientId);
                    if (removedFromQueue > 0)
                    {
                        log.WriteLine($"[DISCONNECT] Removed {clientId} from queue of {roomId}-{slotId} on {dateKey} (removed {removedFromQueue})");
                    }

                    // Nếu đang là holder -> auto release
                    if (slot.CurrentHolderClientId == clientId)
                    {
                        log.WriteLine($"[DISCONNECT] Auto release {clientId} from {roomId}-{slotId} on {dateKey}");

                        if (slot.WaitingQueue.Count == 0)
                        {
                            slot.IsBusy = false;
                            slot.CurrentHolderClientId = null;
                            log.WriteLine($"[SLOT] {roomId}-{slotId} on {dateKey} -> FREE (disconnect)");
                        }
                        else
                        {
                            var (nextClientId, nextStream) = slot.WaitingQueue.Dequeue();
                            slot.IsBusy = true;
                            slot.CurrentHolderClientId = nextClientId;
                            log.WriteLine($"[GRANT] {nextClientId} (from queue, after disconnect) -> {roomId}-{slotId} on {dateKey}");
                            Send(nextStream, $"GRANT|{roomId}|{slotId}\n");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Xóa tất cả entry của clientId khỏi queue. Trả về số entry đã xóa.
    /// </summary>
    private int RemoveFromQueue(SlotState slot, string clientId)
    {
        if (slot.WaitingQueue.Count == 0) return 0;

        int removed = 0;
        var newQueue = new Queue<(string clientId, NetworkStream stream)>();

        while (slot.WaitingQueue.Count > 0)
        {
            var item = slot.WaitingQueue.Dequeue();
            if (item.clientId == clientId)
            {
                removed++;
                // stream sẽ bị đóng ở nơi khác (disconnect), ở đây chỉ bỏ khỏi queue
            }
            else
            {
                newQueue.Enqueue(item);
            }
        }

        // Gán lại queue mới
        while (newQueue.Count > 0)
        {
            slot.WaitingQueue.Enqueue(newQueue.Dequeue());
        }

        return removed;
    }

    private void Send(NetworkStream stream, string msg)
    {
        var data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);
    }
}
