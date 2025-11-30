// BookingServer/Form1.cs
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BookingServer;

public partial class Form1 : Form
{
    private Button _btnStart = null!;
    private DataGridView _gridSlots = null!;
    private Label _lblQueueTitle = null!;
    private ListBox _lstQueue = null!;
    private TextBox _txtLog = null!;
    private DateTimePicker _dtDate = null!;   // CHỌN NGÀY

    private TcpListener? _listener;
    private bool _running = false;
    private readonly ServerState _state = new();

    public Form1()
    {
        InitializeComponent();
        SetupUi();
        // mặc định dùng ngày hôm nay
        _state.SetCurrentDate(DateTime.Today, new UiLogger(this));
        RefreshSlotsSafe();
    }

    private void SetupUi()
    {
        this.Text = "Server - Centralized Mutual Exclusion";
        this.Width = 900;
        this.Height = 580;
        this.StartPosition = FormStartPosition.CenterScreen;

        _btnStart = new Button
        {
            Text = "Start Server",
            Left = 10,
            Top = 10,
            Width = 120,
            Height = 30
        };
        _btnStart.Click += BtnStart_Click;
        this.Controls.Add(_btnStart);

        // Date picker chọn ngày
        _dtDate = new DateTimePicker
        {
            Left = 150,
            Top = 10,
            Width = 200,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd"
        };
        _dtDate.ValueChanged += DtDate_ValueChanged;
        this.Controls.Add(_dtDate);

        // Bảng tổng quan slot
        _gridSlots = new DataGridView
        {
            Left = 10,
            Top = 50,
            Width = 400,
            Height = 480,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _gridSlots.SelectionChanged += GridSlots_SelectionChanged;
        this.Controls.Add(_gridSlots);

        _lblQueueTitle = new Label
        {
            Left = 420,
            Top = 50,
            Width = 450,
            Height = 20,
            Text = "Queue for: (select a room/slot)"
        };
        this.Controls.Add(_lblQueueTitle);

        _lstQueue = new ListBox
        {
            Left = 420,
            Top = 75,
            Width = 450,
            Height = 150
        };
        this.Controls.Add(_lstQueue);

        _txtLog = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Left = 420,
            Top = 360,
            Width = 450,
            Height = 290
        };
        this.Controls.Add(_txtLog);

        //Thêm UI quản lý user (tab User Management)
        var grpUser = new GroupBox
        {
            Text = "User Management (Admin on Server)",
            Left = 420,
            Top = 240,
            Width = 450,
            Height = 150
        };
        this.Controls.Add(grpUser);

        var lblUid = new Label { Left = 10, Top = 25, Width = 60, Text = "UserId:" };
        var txtUid = new TextBox { Left = 80, Top = 22, Width = 100 };

        var lblName = new Label { Left = 190, Top = 25, Width = 60, Text = "Name:" };
        var txtName = new TextBox { Left = 250, Top = 22, Width = 180 };

        var lblType = new Label { Left = 10, Top = 55, Width = 60, Text = "Type:" };
        var cbType = new ComboBox
        {
            Left = 80,
            Top = 52,
            Width = 100,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cbType.Items.AddRange(new object[] { "Student", "Lecturer", "Staff" });
        cbType.SelectedIndex = 0;

        var lblPwd = new Label { Left = 190, Top = 55, Width = 60, Text = "Password:" };
        var txtPwd = new TextBox { Left = 250, Top = 52, Width = 180 };

        var btnCreateUser = new Button
        {
            Left = 80,
            Top = 85,
            Width = 120,
            Text = "Create / Add User"
        };
        btnCreateUser.Click += (s, e) =>
        {
            var user = new UserInfo
            {
                UserId = txtUid.Text.Trim(),
                FullName = txtName.Text.Trim(),
                UserType = cbType.SelectedItem?.ToString() ?? "Student"
            };

            var ok = _state.CreateUser(user, txtPwd.Text.Trim(), out var err);
            if (!ok)
            {
                Log("[USER MGMT] " + err);
                MessageBox.Show(err, "User Management", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                Log($"[USER MGMT] User {user.UserId} created ({user.UserType})");
                MessageBox.Show("User created", "User Management");
            }
        };

        grpUser.Controls.AddRange(new Control[]
        {
    lblUid, txtUid, lblName, txtName,
    lblType, cbType, lblPwd, txtPwd, btnCreateUser
        });


    }

    private void DtDate_ValueChanged(object? sender, EventArgs e)
    {
        _state.SetCurrentDate(_dtDate.Value.Date, new UiLogger(this));
        RefreshSlotsSafe();
    }

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        if (_running) return;

        int port = 5000;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _running = true;
        Log($"[SERVER] Listening on port {port}...");

        RefreshSlotsSafe();

        while (_running)
        {
            var client = await _listener.AcceptTcpClientAsync();
            Log("[SERVER] New client connected");
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient)
    {
        using (tcpClient)
        using (var stream = tcpClient.GetStream())
        using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
        {
            string? line;
            string? clientId = null;
            string? currentUserType = null;

            try
            {
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var msg = line.Trim();
                    if (string.IsNullOrEmpty(msg)) continue;

                    Log($"[RECV] {msg}");

                    var parts = msg.Split('|');
                    var cmd = parts[0].ToUpperInvariant();

                    switch (cmd)
                    {
                        case "LOGIN":
                            if (parts.Length != 3)
                            {
                                await SendAsync(stream, "INFO|LOGIN_FAIL|Invalid format\n");
                                break;
                            }
                            {
                                var userId = parts[1];
                                var password = parts[2];

                                var (success, userType, error) = _state.ValidateUserCredentials(userId, password);
                                if (!success || userType == null)
                                {
                                    await SendAsync(stream, $"INFO|LOGIN_FAIL|{error}\n");
                                    Log($"[LOGIN FAIL] {userId} - {error}");
                                }
                                else
                                {
                                    clientId = userId;
                                    currentUserType = userType;
                                    await SendAsync(stream, $"INFO|LOGIN_OK|{userType}\n");
                                    Log($"[LOGIN OK] {userId} ({userType})");
                                }
                            }
                            break;

                        case "REQUEST":
                            if (parts.Length != 4)
                            {
                                await SendAsync(stream, "INFO|ERROR|Invalid REQUEST format\n");
                                break;
                            }

                            if (clientId == null)
                            {
                                await SendAsync(stream, "INFO|ERROR|NOT_AUTHENTICATED\n");
                                break;
                            }

                            if (!string.Equals(clientId, parts[1], StringComparison.OrdinalIgnoreCase))
                            {
                                await SendAsync(stream, "INFO|ERROR|USER_MISMATCH\n");
                                break;
                            }

                            _state.HandleRequest(clientId, parts[2], parts[3], stream, new UiLogger(this));
                            RefreshSlotsSafe();
                            break;

                        case "RELEASE":
                            if (parts.Length != 4)
                            {
                                await SendAsync(stream, "INFO|ERROR|Invalid RELEASE format\n");
                                break;
                            }

                            if (clientId == null)
                            {
                                await SendAsync(stream, "INFO|ERROR|NOT_AUTHENTICATED\n");
                                break;
                            }

                            if (!string.Equals(clientId, parts[1], StringComparison.OrdinalIgnoreCase))
                            {
                                await SendAsync(stream, "INFO|ERROR|USER_MISMATCH\n");
                                break;
                            }

                            _state.HandleRelease(clientId, parts[2], parts[3], stream, new UiLogger(this));
                            RefreshSlotsSafe();
                            break;

                        default:
                            await SendAsync(stream, "INFO|ERROR|Unknown command\n");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Client {clientId ?? "UNKNOWN"}: {ex.Message}");
            }
            finally
            {
                Log($"[SERVER] Client {clientId ?? "UNKNOWN"} disconnected");
                if (clientId != null)
                {
                    _state.HandleDisconnect(clientId, new UiLogger(this));
                    RefreshSlotsSafe();
                }
            }
        }
    }

    private Task SendAsync(NetworkStream stream, string message)
    {
        var data = Encoding.UTF8.GetBytes(message);
        return stream.WriteAsync(data, 0, data.Length);
    }

    public void Log(string text)
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

    private void RefreshSlotsSafe()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(RefreshSlots));
            return;
        }
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        var summaries = _state.GetAllSlotSummaries();
        _gridSlots.DataSource = null;
        _gridSlots.DataSource = summaries;

        if (_gridSlots.Rows.Count > 0 && _gridSlots.CurrentRow == null)
        {
            _gridSlots.Rows[0].Selected = true;
        }

        UpdateQueueViewForSelected();
    }

    private void GridSlots_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateQueueViewForSelected();
    }

    private void UpdateQueueViewForSelected()
    {
        if (_gridSlots.CurrentRow == null ||
            _gridSlots.CurrentRow.DataBoundItem is not SlotSummary summary)
        {
            _lblQueueTitle.Text = "Queue for: (select a room/slot)";
            _lstQueue.Items.Clear();
            _lstQueue.Items.Add("No selection");
            return;
        }

        var roomId = summary.RoomId;
        var slotId = summary.SlotId;

        _lblQueueTitle.Text = $"Queue for: {summary.Date} - {roomId}-{slotId}";

        var clients = _state.GetQueueClients(roomId, slotId);

        _lstQueue.Items.Clear();
        if (clients.Count == 0)
        {
            _lstQueue.Items.Add("Queue empty");
        }
        else
        {
            for (int i = 0; i < clients.Count; i++)
            {
                _lstQueue.Items.Add($"{i + 1}. {clients[i]}");
            }
        }
    }

    private class UiLogger : TextWriter
    {
        private readonly Form1 _form;
        public UiLogger(Form1 form) => _form = form;
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value)
        {
            if (value != null) _form.Log(value);
        }
    }
}
