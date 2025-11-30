// BookingClient/Form1.cs
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BookingClient
{
    public partial class Form1 : Form
    {
        private TextBox _txtClientId = null!;
        private TextBox _txtServerIp = null!;
        private TextBox _txtUserId = null!;
        private TextBox _txtPassword = null!;
        private ComboBox _cbRoom = null!;
        private ComboBox _cbSlot = null!;
        private Button _btnConnect = null!;
        private Button _btnRequest = null!;
        private Button _btnRelease = null!;
        private TextBox _txtLog = null!;
        private Label _lblStatus = null!;

        private TcpClient? _client;
        private NetworkStream? _stream;
        private bool _connected = false;
        private string? _currentUserId;
        private string? _currentUserType;
        public Form1()
        {
            InitializeComponent();
            SetupUi();
        }

        private void SetupUi()
        {
            this.Text = "Client - Room Booking";
            this.Width = 620;
            this.Height = 430;
            this.StartPosition = FormStartPosition.CenterScreen;

            Label lblId = new Label { Text = "User ID:", Left = 10, Top = 10, Width = 70 };
            this.Controls.Add(lblId);
            _txtUserId = new TextBox { Left = 80, Top = 8, Width = 100, Text = "sv001" };
            this.Controls.Add(_txtUserId);

            Label lblPwd = new Label { Text = "Password:", Left = 190, Top = 10, Width = 70 };
            this.Controls.Add(lblPwd);
            _txtPassword = new TextBox { Left = 260, Top = 8, Width = 100, UseSystemPasswordChar = true, Text = "123456" };
            this.Controls.Add(_txtPassword);

            Label lblIp = new Label { Text = "Server IP:", Left = 370, Top = 10, Width = 70 };
            this.Controls.Add(lblIp);
            _txtServerIp = new TextBox { Left = 440, Top = 8, Width = 110, Text = "127.0.0.1" };
            this.Controls.Add(_txtServerIp);

            _btnConnect = new Button { Text = "Connect", Left = 410, Top = 6, Width = 80 };
            _btnConnect.Click += BtnConnect_Click;
            this.Controls.Add(_btnConnect);

            var btnLogin = new Button { Text = "Login", Left = 100, Top = 38, Width = 80 };
            btnLogin.Click += BtnLogin_Click;
            this.Controls.Add(btnLogin);

            Label lblRoom = new Label { Text = "Room:", Left = 10, Top = 40, Width = 50 };
            this.Controls.Add(lblRoom);
            _cbRoom = new ComboBox { Left = 70, Top = 38, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _cbRoom.Items.AddRange(new object[]
            {
                "A08","A16","A24","A32",
                "A21","A22","A23",
                "A24-A25","A31","A32-A33","A34-A35"
            });
            _cbRoom.SelectedIndex = 0;
            this.Controls.Add(_cbRoom);

            Label lblSlot = new Label { Text = "Ca:", Left = 210, Top = 40, Width = 40 };
            this.Controls.Add(lblSlot);
            _cbSlot = new ComboBox { Left = 250, Top = 38, Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
            // 14 ca: hiển thị "1".."14" nhưng gửi S1..S14
            for (int i = 1; i <= 14; i++)
            {
                _cbSlot.Items.Add(i.ToString());
            }
            _cbSlot.SelectedIndex = 0;
            this.Controls.Add(_cbSlot);

            _btnRequest = new Button { Text = "REQUEST", Left = 320, Top = 36, Width = 90, Enabled = false };
            _btnRequest.Click += BtnRequest_Click;
            this.Controls.Add(_btnRequest);

            _btnRelease = new Button { Text = "RELEASE", Left = 420, Top = 36, Width = 90, Enabled = false };
            _btnRelease.Click += BtnRelease_Click;
            this.Controls.Add(_btnRelease);

            _lblStatus = new Label
            {
                Left = 10,
                Top = 70,
                Width = 580,
                Height = 25,
                Text = "Status: IDLE (not requested)",
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(_lblStatus);

            _txtLog = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Left = 10,
                Top = 100,
                Width = 580,
                Height = 280
            };
            this.Controls.Add(_txtLog);
        }

        private async void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_connected) return;

            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_txtServerIp.Text.Trim(), 5000);
                _stream = _client.GetStream();
                _connected = true;
                Log("[CLIENT] Connected to server");
                SetStatus("CONNECTED", "Connected to server", System.Drawing.Color.LightGreen);

                // đọc data từ server
                _ = Task.Run(ReadLoopAsync);
            }
            catch (Exception ex)
            {
                Log("[ERROR] " + ex.Message);
                SetStatus("ERROR", ex.Message, System.Drawing.Color.LightCoral);
            }
        }

        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            if (!_connected || _stream == null)
            {
                SetStatus("ERROR", "Not connected to server", System.Drawing.Color.LightCoral);
                return;
            }

            var userId = _txtUserId.Text.Trim();
            var password = _txtPassword.Text;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrEmpty(password))
            {
                SetStatus("ERROR", "UserId / Password không được để trống", System.Drawing.Color.LightCoral);
                return;
            }

            var msg = $"LOGIN|{userId}|{password}\n";
            var data = Encoding.UTF8.GetBytes(msg);
            await _stream.WriteAsync(data, 0, data.Length);
            Log("[CLIENT] Sent " + msg.Trim());
            SetStatus("LOGINING", "Đang gửi yêu cầu đăng nhập...", System.Drawing.Color.LightYellow);

            // chờ server trả INFO|LOGIN_OK hoặc LOGIN_FAIL trong ReadLoopAsync
        }

        private async Task ReadLoopAsync()
        {
            if (_stream == null) return;

            var buffer = new byte[4096];
            var sb = new StringBuilder();

            try
            {
                while (true)
                {
                    int bytes = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytes == 0) break;

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, bytes));

                    while (true)
                    {
                        var text = sb.ToString();
                        var idx = text.IndexOf('\n');
                        if (idx < 0) break;

                        var line = text[..idx].Trim();
                        sb.Remove(0, idx + 1);

                        if (line.Length > 0)
                        {
                            HandleServerMessage(line);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("[ERROR] ReadLoop: " + ex.Message);
            }
            finally
            {
                Log("[CLIENT] Disconnected from server");
                _connected = false;
                SetStatus("DISCONNECTED", "Disconnected from server", System.Drawing.Color.LightGray);
                this.Text = "Client - Disconnected";
            }
        }

        private void HandleServerMessage(string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(HandleServerMessage), msg);
                return;
            }

            Log("[SERVER] " + msg);

            var parts = msg.Split('|');

            switch (parts[0])
            {
                case "GRANT":
                    // GRANT|room|slot
                    if (parts.Length >= 3)
                    {
                        string room = parts[1];
                        string slot = parts[2];
                        this.Text = $"Client - GRANTED {room}-{slot}";
                        SetStatus("GRANTED", $"You are GRANTED {room}-{slot}", System.Drawing.Color.LightGreen);
                    }
                    break;

                case "QUEUED":
                    // QUEUED|room|slot|pos
                    if (parts.Length >= 4)
                    {
                        var room = parts[1];
                        var slot = parts[2];
                        var posText = parts[3];
                        this.Text = $"Client - QUEUED pos {posText}";
                        SetStatus("QUEUED", $"Waiting for {room}-{slot} at position {posText}", System.Drawing.Color.Khaki);
                    }
                    break;

                case "INFO":
                    if (parts.Length >= 2)
                    {
                        var infoType = parts[1];
                        if (infoType == "LOGIN_OK" && parts.Length >= 3)
                        {
                            var userType = parts[2];
                            _currentUserId = _txtUserId.Text.Trim();
                            _currentUserType = userType;

                            this.Text = $"Client - Logged in as {_currentUserId} ({userType})";
                            SetStatus("LOGIN_OK", $"Logged in as {_currentUserId} ({userType})", System.Drawing.Color.LightGreen);

                            _btnRequest.Enabled = true;
                            _btnRelease.Enabled = true;
                        }
                        else if (infoType == "LOGIN_FAIL" && parts.Length >= 3)
                        {
                            var reason = parts[2];
                            _currentUserId = null;
                            _currentUserType = null;

                            this.Text = "Client - Login failed";
                            SetStatus("LOGIN_FAIL", reason, System.Drawing.Color.LightCoral);

                            _btnRequest.Enabled = false;
                            _btnRelease.Enabled = false;
                        }
                        else if (infoType == "ERROR")
                        {
                            // INFO|ERROR|message...
                            string msgErr = parts.Length >= 3 ? parts[2] : "Unknown error";
                            this.Text = "Client - ERROR";
                            SetStatus("ERROR", msgErr, System.Drawing.Color.LightCoral);
                        }
                        else if (infoType == "ALREADY_HOLDER" && parts.Length >= 4)
                        {
                            // INFO|ALREADY_HOLDER|room|slot
                            var room = parts[2];
                            var slot = parts[3];
                            this.Text = $"Client - GRANTED {room}-{slot}";
                            SetStatus("GRANTED", $"Already holder of {room}-{slot}", System.Drawing.Color.LightGreen);
                        }
                        else if (infoType == "ALREADY_QUEUED" && parts.Length >= 5)
                        {
                            // INFO|ALREADY_QUEUED|room|slot|pos
                            var room = parts[2];
                            var slot = parts[3];
                            var posText = parts[4];
                            this.Text = $"Client - QUEUED pos {posText}";
                            SetStatus("QUEUED", $"Already queued for {room}-{slot} at position {posText}", System.Drawing.Color.Khaki);
                        }
                        else if (infoType == "CANCELLED" && parts.Length >= 4)
                        {
                            // INFO|CANCELLED|room|slot
                            this.Text = "Client - IDLE (cancelled)";
                            SetStatus("IDLE", "Request cancelled", System.Drawing.Color.LightGray);
                        }
                        else if (infoType == "RELEASEED" && parts.Length >= 4)
                        {
                            // typo safety – but trong server là RELEASED
                            this.Text = "Client - IDLE (released)";
                            SetStatus("IDLE", "Released", System.Drawing.Color.LightGray);
                        }
                        else if (infoType == "RELEASED" && parts.Length >= 4)
                        {
                            // INFO|RELEASED|room|slot
                            this.Text = "Client - IDLE (released)";
                            SetStatus("IDLE", "Released", System.Drawing.Color.LightGray);
                        }
                    }
                    break;
            }
        }

        private async void BtnRequest_Click(object? sender, EventArgs e)
        {
            if (!_connected || _stream == null)
            {
                Log("Not connected");
                SetStatus("ERROR", "Not connected", System.Drawing.Color.LightCoral);
                return;
            }

            if (string.IsNullOrEmpty(_currentUserId))
            {
                SetStatus("ERROR", "Bạn cần đăng nhập trước khi REQUEST", System.Drawing.Color.LightCoral);
                return;
            }

            var clientId = _currentUserId;  // user đã login
            var room = _cbRoom.SelectedItem?.ToString() ?? "A08";
            var slotNumber = _cbSlot.SelectedItem?.ToString() ?? "1";
            var slotId = "S" + slotNumber;

            var msg = $"REQUEST|{clientId}|{room}|{slotId}\n";
            var data = Encoding.UTF8.GetBytes(msg);
            await _stream.WriteAsync(data, 0, data.Length);
            Log("[CLIENT] Sent " + msg.Trim());
            SetStatus("WAITING", $"REQUEST sent for {room}-{slotId}", System.Drawing.Color.LightYellow);
        }

        private async void BtnRelease_Click(object? sender, EventArgs e)
        {
            if (!_connected || _stream == null)
            {
                Log("Not connected");
                SetStatus("ERROR", "Not connected", System.Drawing.Color.LightCoral);
                return;
            }

            if (string.IsNullOrEmpty(_currentUserId))
            {
                SetStatus("ERROR", "Bạn cần đăng nhập trước khi RELEASE", System.Drawing.Color.LightCoral);
                return;
            }

            var clientId = _currentUserId;  // user đã login
            var room = _cbRoom.SelectedItem?.ToString() ?? "A08";
            var slotNumber = _cbSlot.SelectedItem?.ToString() ?? "1";
            var slotId = "S" + slotNumber;

            var msg = $"RELEASE|{clientId}|{room}|{slotId}\n";
            var data = Encoding.UTF8.GetBytes(msg);
            await _stream.WriteAsync(data, 0, data.Length);
            Log("[CLIENT] Sent " + msg.Trim());
        }

        private void Log(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), text);
                return;
            }

            _txtLog.AppendText(text + Environment.NewLine);
            _txtLog.SelectionStart = _txtLog.Text.Length;
            _txtLog.ScrollToCaret();
        }

        private void SetStatus(string shortStatus, string detail, System.Drawing.Color backColor)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, string, System.Drawing.Color>(SetStatus), shortStatus, detail, backColor);
                return;
            }

            _lblStatus.Text = $"Status: {shortStatus} - {detail}";
            _lblStatus.BackColor = backColor;
        }
    }
}
