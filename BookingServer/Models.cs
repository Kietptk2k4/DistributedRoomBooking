// BookingServer/Models.cs
using System;

namespace BookingServer
{
    public class RoomInfo
    {
        public string RoomId { get; set; } = "";      // A08
        public string Building { get; set; } = "";    // CS1 - Tòa A
        public int Capacity { get; set; }             // 60
        public bool HasProjector { get; set; }
        public bool HasPC { get; set; }
        public bool HasAirConditioner { get; set; }
        public bool HasMic { get; set; }
        public string Status { get; set; } = "ACTIVE"; // ACTIVE / UNDER_MAINTENANCE / DISABLED
    }

    /// Thông tin người dùng: sinh viên / giảng viên / staff.
    /// Hiện tại vẫn dùng UserId thay cho ClientId, nhưng sau này
    /// có thể map 1-1 (client C1 = sv001, C2 = gv001, ...).
    public class UserInfo
    {
        public string UserId { get; set; } = "";      // dùng thay cho ClientId thuần
        public string UserType { get; set; } = "";    // Student / Lecturer / Staff
        public string FullName { get; set; } = "";

        // Sinh viên
        public string StudentId { get; set; } = "";
        public string Class { get; set; } = "";
        public string Department { get; set; } = "";

        // Giảng viên
        public string LecturerId { get; set; } = "";
        public string Faculty { get; set; } = "";

        // Thông tin liên hệ chung
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }
    public class Booking
    {
        public Guid BookingId { get; set; }
        public string UserId { get; set; } = "";
        public string RoomId { get; set; } = "";
        public string Date { get; set; } = "";        // yyyy-MM-dd
        public string SlotStartId { get; set; } = ""; // S1
        public string SlotEndId { get; set; } = "";   // S1 (range sau)
        public string Purpose { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // REQUESTED / APPROVED / REJECTED / CANCELLED / COMPLETED / NO_SHOW
        public string Status { get; set; } = "APPROVED";
    }
}
