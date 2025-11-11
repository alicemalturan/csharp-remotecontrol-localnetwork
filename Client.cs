// Client.cs (Responsive • 60 FPS • Delta • Control • Scan • OOM korumalı • Sağlam Splitter)
// .NET 4.x / C# 5 uyumlu

using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

class ClientForm : Form
{
    // Top bar
    TextBox txtIp, txtPort;
    Button btnConnect, btnControl, btnScan;
    Label lblInfo;

    // Layout
    TableLayoutPanel layout;
    SplitContainer split;
    ListBox lstServers;
    PictureBox pb;

    // Video link
    TcpClient client;
    NetworkStream stream;
    Thread worker;
    volatile bool running = false;

    // Control link
    TcpClient ctrlClient;
    NetworkStream ctrlStream;
    volatile bool controlOn = false;

    // Video buffers
    Bitmap currentBmp = null;
    readonly object bmpLock = new object();
    Bitmap backBuffer = null;
    volatile bool paintInProgress = false;

    // Video boyutu (server gönderiyor)
    int serverWidth = 0, serverHeight = 0;
    int cursorX = 0, cursorY = 0; // video koord

    // Split yönetimi
    bool splitterReady = false;   // ilk güvenli ayar yapıldı mı
    int desiredLeftWidth = 260;   // sol panel hedefi

    public ClientForm()
    {
        // ---- FORM ----
        this.Text = "Screen Client (Responsive • 60 FPS • Delta • Control • Scan)";
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Width = 1200; this.Height = 720;
        this.KeyPreview = true;

        // ---- TOP BAR ----
        Panel top = new Panel { Dock = DockStyle.Fill, Height = 36 };

        Label lblI = new Label { Left = 8, Top = 9, Width = 70, Text = "Server IP:" };
        txtIp = new TextBox { Left = 78, Top = 6, Width = 160, Text = "192.168.1.10" };
        Label lblP = new Label { Left = 244, Top = 9, Width = 40, Text = "Port:" };
        txtPort = new TextBox { Left = 284, Top = 6, Width = 60, Text = "5000" };

        btnConnect = new Button { Left = 350, Top = 4, Width = 100, Height = 28, Text = "Connect" };
        btnControl = new Button { Left = 456, Top = 4, Width = 110, Height = 28, Text = "Control: OFF" };
        btnScan    = new Button { Left = 572, Top = 4, Width = 90, Height = 28, Text = "Scan /24" };
        lblInfo    = new Label  { Left = 670, Top = 9, Width = 700, Height = 20, Text = "Durum: Bağlı değil" };

        top.Controls.Add(lblI); top.Controls.Add(txtIp);
        top.Controls.Add(lblP); top.Controls.Add(txtPort);
        top.Controls.Add(btnConnect); top.Controls.Add(btnControl); top.Controls.Add(btnScan);
        top.Controls.Add(lblInfo);

        // ---- SPLIT (min değerleri ŞİMDİ ayarlamıyoruz!) ----
        split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
            // Panel1MinSize/Panel2MinSize/Divider henüz YOK → erken layout patlamaz
        };

        lstServers = new ListBox { Dock = DockStyle.Fill };
        pb = new PictureBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom };

        split.Panel1.Controls.Add(lstServers);
        split.Panel2.Controls.Add(pb);

        // ---- Ana Layout ----
        layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(split, 0, 1);
        this.Controls.Add(layout);

        // ---- Eventler ----
        lstServers.DoubleClick += LstServers_DoubleClick;
        btnConnect.Click += BtnConnect_Click;
        btnControl.Click += BtnControl_Click;
        btnScan.Click    += BtnScan_Click;

        pb.MouseMove += Pb_MouseMove;
        pb.MouseDown += Pb_MouseDown;
        pb.MouseUp   += Pb_MouseUp;
        pb.MouseWheel+= Pb_MouseWheel;

        this.KeyDown += Form_KeyDown;
        this.KeyUp   += Form_KeyUp;

        this.FormClosing += ClientForm_FormClosing;

        // ---- SplitterDistance güvenli ayarlama kancaları ----
        split.HandleCreated += (s, e) => InitSplitterSafe();
        this.Shown          += (s, e) => InitSplitterSafe();
        this.Resize         += (s, e) =>
        {
            if (!splitterReady) { InitSplitterSafe(); return; }
            SafeSetSplitter((int)Math.Max(150, this.ClientSize.Width * 0.18));
        };
    }

    // --- Splitter güvenli ve geç ayarı ---
    void InitSplitterSafe()
    {
        int w = split.ClientSize.Width;
        if (w <= 0) return; // henüz ölçü yok

        // Min değerleri ŞİMDİ veriyoruz
        int p1min = Math.Max(120, w / 6);  // ~%16, min 120
        int p2min = Math.Max(240, w / 3);  // ~%33, min 240

        // Eğer min toplamı genişliği aşıyorsa azalt
        if (p1min + p2min >= w)
        {
            int spare = Math.Max(0, w - 50); // splitter için biraz boşluk
            p2min = Math.Max(150, spare - p1min);
            if (p1min + p2min >= w)
            {
                p1min = Math.Max(80, w / 8);
                p2min = Math.Max(150, w - p1min - 20);
                if (p1min < 0) p1min = 0;
                if (p2min < 0) p2min = 0;
            }
        }

        try { split.Panel1MinSize = p1min; } catch { }
        try { split.Panel2MinSize = p2min; } catch { }

        // Hedef sol genişlik
        int desired = desiredLeftWidth;
        if (desired > w - p2min) desired = Math.Max(p1min + 10, w / 5);

        SafeSetSplitter(desired);
        splitterReady = true;
    }

    void SafeSetSplitter(int distance)
    {
        int w = split.ClientSize.Width;
        if (w <= 0) return;
        int min = 0, max = w;

        try { min = split.Panel1MinSize; } catch { min = 0; }
        try { max = Math.Max(min, w - split.Panel2MinSize); } catch { max = w - 50; }

        int d = Math.Max(min, Math.Min(distance, max));

        try
        {
            if (split.SplitterDistance != d) split.SplitterDistance = d;
        }
        catch
        {
            // Son çare: güvenli bir sayı
            try { split.SplitterDistance = Math.Max(min, Math.Min(200, max)); } catch { }
        }
    }

    // ---- Lifecycle ----
    void ClientForm_FormClosing(object sender, FormClosingEventArgs e) { Disconnect(); }

    // ---- UI ----
    void LstServers_DoubleClick(object sender, EventArgs e)
    {
        if (lstServers.SelectedItem == null) return;
        string ip = lstServers.SelectedItem.ToString();
        int idx = ip.IndexOf(' ');
        if (idx > 0) ip = ip.Substring(0, idx);
        txtIp.Text = ip;
    }

    void BtnConnect_Click(object sender, EventArgs e)
    {
        if (!running) Connect();
        else Disconnect();
    }

    void BtnControl_Click(object sender, EventArgs e)
    {
        if (!running) return;
        if (!controlOn) StartControl();
        else StopControl();
    }

    void BtnScan_Click(object sender, EventArgs e)
    {
        lstServers.Items.Clear();
        Thread t = new Thread(new ThreadStart(ScanNetwork)) { IsBackground = true };
        t.Start();
    }

    // ---- Connect/Disconnect ----
    void Connect()
    {
        string ip = txtIp.Text.Trim();
        int port = 5000; int.TryParse(txtPort.Text.Trim(), out port);

        try
        {
            client = new TcpClient { NoDelay = true, SendBufferSize = 1 << 20, ReceiveBufferSize = 1 << 20 };
            client.Connect(ip, port);
            stream = client.GetStream();
            running = true;
            btnConnect.Text = "Disconnect";

            string ep = "";
            try { if (client.Client != null && client.Client.RemoteEndPoint != null) ep = client.Client.RemoteEndPoint.ToString(); } catch { }
            lblInfo.Text = "Durum: Video bağlı -> " + ep;

            worker = new Thread(new ThreadStart(ReceiveLoop)) { IsBackground = true };
            worker.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Bağlanamadı: " + ex.Message);
            try { if (client != null) client.Close(); } catch { }
            client = null; stream = null;
        }
    }

    void Disconnect()
    {
        StopControl();
        running = false;
        try { if (stream != null) stream.Close(); } catch { }
        try { if (client != null) client.Close(); } catch { }
        try { if (worker != null) worker.Join(200); } catch { }
        btnConnect.Text = "Connect";
        lblInfo.Text = "Durum: Bağlı değil";
        lock (bmpLock)
        {
            if (currentBmp != null) { currentBmp.Dispose(); currentBmp = null; }
            if (backBuffer != null) { backBuffer.Dispose(); backBuffer = null; }
            pb.Image = null;
            serverWidth = serverHeight = 0;
        }
    }

    // ---- Control link ----
    void StartControl()
    {
        if (controlOn) return;
        string ip = txtIp.Text.Trim();
        int port = 5000; int.TryParse(txtPort.Text.Trim(), out port);
        try
        {
            ctrlClient = new TcpClient { NoDelay = true, SendBufferSize = 64 * 1024, ReceiveBufferSize = 64 * 1024 };
            ctrlClient.Connect(ip, port + 1);
            ctrlStream = ctrlClient.GetStream();
            controlOn = true;
            btnControl.Text = "Control: ON";
            lblInfo.Text = "Durum: Video + Control bağlı";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Control bağlantısı başarısız: " + ex.Message);
            StopControl();
        }
    }

    void StopControl()
    {
        controlOn = false;
        btnControl.Text = "Control: OFF";
        try { if (ctrlStream != null) ctrlStream.Close(); } catch { }
        try { if (ctrlClient != null) ctrlClient.Close(); } catch { }
        ctrlStream = null; ctrlClient = null;
    }

    // ---- Video RX ----
    void ReceiveLoop()
    {
        byte[] header = new byte[16];

        while (running && client != null && client.Connected)
        {
            try
            {
                if (!ReadExact(stream, header, 0, 16)) break;

                int dataLen = BitConverter.ToInt32(header, 0);
                int flags = BitConverter.ToInt32(header, 4);
                bool isFull = (flags & 1) != 0;
                cursorX = BitConverter.ToInt32(header, 8);   // video koord
                cursorY = BitConverter.ToInt32(header, 12);  // video koord

                if (dataLen <= 0 || dataLen > 50000000) break;

                byte[] data = new byte[dataLen];
                if (!ReadExact(stream, data, 0, dataLen)) break;

                using (MemoryStream ms = new MemoryStream(data))
                {
                    if (isFull)
                    {
                        Bitmap bmp = new Bitmap(ms);
                        lock (bmpLock)
                        {
                            if (currentBmp != null) currentBmp.Dispose();
                            currentBmp = bmp;
                            serverWidth = bmp.Width; serverHeight = bmp.Height;

                            if (backBuffer == null || backBuffer.Width != serverWidth || backBuffer.Height != serverHeight)
                            {
                                if (backBuffer != null) backBuffer.Dispose();
                                backBuffer = new Bitmap(serverWidth, serverHeight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                            }
                        }
                    }
                    else
                    {
                        lock (bmpLock)
                        {
                            if (currentBmp == null) continue;

                            int pos = 0;
                            while (pos + 12 <= data.Length)
                            {
                                short bx = BitConverter.ToInt16(data, pos); pos += 2;
                                short by = BitConverter.ToInt16(data, pos); pos += 2;
                                short bw = BitConverter.ToInt16(data, pos); pos += 2;
                                short bh = BitConverter.ToInt16(data, pos); pos += 2;
                                int blen = BitConverter.ToInt32(data, pos); pos += 4;

                                if (blen <= 0 || pos + blen > data.Length) break;

                                using (MemoryStream bms = new MemoryStream(data, pos, blen, false))
                                using (Bitmap patch = new Bitmap(bms))
                                using (Graphics g = Graphics.FromImage(currentBmp))
                                {
                                    g.DrawImage(patch, bx, by);
                                }
                                pos += blen;
                            }
                        }
                    }
                }

                // UI (tek backBuffer)
                if (pb.IsHandleCreated && !paintInProgress)
                {
                    paintInProgress = true;
                    pb.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            lock (bmpLock)
                            {
                                if (currentBmp == null || backBuffer == null) { paintInProgress = false; return; }

                                using (Graphics g = Graphics.FromImage(backBuffer))
                                {
                                    g.DrawImage(currentBmp, 0, 0);

                                    int drawX = cursorX - 8;
                                    int drawY = cursorY - 8;
                                    if (drawX < -8) drawX = -8;
                                    if (drawY < -8) drawY = -8;
                                    g.DrawEllipse(Pens.Red, drawX, drawY, 16, 16);
                                    g.DrawLine(Pens.Red, drawX + 8, drawY, drawX + 8, drawY + 16);
                                    g.DrawLine(Pens.Red, drawX, drawY + 8, drawX + 16, drawY + 8);
                                }

                                Image oldImg = pb.Image;
                                pb.Image = (Image)backBuffer.Clone();
                                if (oldImg != null) oldImg.Dispose();
                            }
                        }
                        finally { paintInProgress = false; }
                    }));
                }
            }
            catch { break; }
        }

        Disconnect();
    }

    // ---- Mouse & Keyboard capture (video koord gönder) ----
    void Pb_MouseMove(object sender, MouseEventArgs e)
    {
        if (!controlOn || ctrlStream == null) return;
        int vx, vy;
        if (!TranslateToVideoCoords(e.X, e.Y, out vx, out vy)) return;
        try
        {
            BinaryWriter bw = new BinaryWriter(ctrlStream);
            bw.Write((byte)1); bw.Write(vx); bw.Write(vy); bw.Flush();
        }
        catch { }
    }

    void Pb_MouseDown(object sender, MouseEventArgs e)
    {
        if (!controlOn || ctrlStream == null) return;
        try { var bw = new BinaryWriter(ctrlStream); bw.Write((byte)2); bw.Write(MouseBtnToInt(e.Button)); bw.Flush(); }
        catch { }
    }

    void Pb_MouseUp(object sender, MouseEventArgs e)
    {
        if (!controlOn || ctrlStream == null) return;
        try { var bw = new BinaryWriter(ctrlStream); bw.Write((byte)3); bw.Write(MouseBtnToInt(e.Button)); bw.Flush(); }
        catch { }
    }

    void Pb_MouseWheel(object sender, MouseEventArgs e)
    {
        if (!controlOn || ctrlStream == null) return;
        try { var bw = new BinaryWriter(ctrlStream); bw.Write((byte)4); bw.Write(e.Delta); bw.Flush(); }
        catch { }
    }

    void Form_KeyDown(object sender, KeyEventArgs e)
    {
        if (!controlOn || ctrlStream == null) return;
        try { var bw = new BinaryWriter(ctrlStream); bw.Write((byte)5); bw.Write((int)e.KeyCode); bw.Flush(); e.Handled = true; }
        catch { }
    }

    void Form_KeyUp(object sender, KeyEventArgs e)
    {
        if (!controlOn || ctrlStream == null) return;
        try { var bw = new BinaryWriter(ctrlStream); bw.Write((byte)6); bw.Write((int)e.KeyCode); bw.Flush(); e.Handled = true; }
        catch { }
    }

    int MouseBtnToInt(MouseButtons b)
    {
        if (b == MouseButtons.Left) return 1;
        if (b == MouseButtons.Right) return 2;
        if (b == MouseButtons.Middle) return 3;
        return 1;
    }

    // pb.Zoom alanına göre → VIDEO KOORD (serverWidth x serverHeight)
    bool TranslateToVideoCoords(int px, int py, out int vx, out int vy)
    {
        vx = 0; vy = 0;
        if (serverWidth <= 0 || serverHeight <= 0) return false;

        Rectangle rect = GetImageDisplayRectangle(pb);
        if (rect.Width <= 0 || rect.Height <= 0) return false;
        if (px < rect.X || py < rect.Y || px > rect.Right || py > rect.Bottom) return false;

        float scaleX = (float)serverWidth / (float)rect.Width;
        float scaleY = (float)serverHeight / (float)rect.Height;
        vx = (int)((px - rect.X) * scaleX);
        vy = (int)((py - rect.Y) * scaleY);
        if (vx < 0) vx = 0; if (vy < 0) vy = 0;
        if (vx >= serverWidth) vx = serverWidth - 1;
        if (vy >= serverHeight) vy = serverHeight - 1;
        return true;
    }

    static Rectangle GetImageDisplayRectangle(PictureBox pb)
    {
        if (pb.Image == null) return Rectangle.Empty;
        Image img = pb.Image;
        float imageRatio = (float)img.Width / (float)img.Height;
        float boxRatio = (float)pb.Width / (float)pb.Height;

        int w, h, x, y;
        if (imageRatio > boxRatio)
        {
            w = pb.Width;
            h = (int)((float)pb.Width / imageRatio);
            x = 0; y = (pb.Height - h) / 2;
        }
        else
        {
            h = pb.Height;
            w = (int)((float)pb.Height * imageRatio);
            y = 0; x = (pb.Width - w) / 2;
        }
        return new Rectangle(x, y, w, h);
    }

    // --- /24 tarama ---
    void ScanNetwork()
    {
        AddServerLine("[scan] Başladı…");
        string baseIp = GetLocalBase24();
        if (baseIp == null) { AddServerLine("[scan] Yerel IPv4 bulunamadı"); return; }

        int port = 5000; int timeoutMs = 150;
        int maxParallel = 64; int active = 0; object locker = new object();

        for (int i = 1; i <= 254; i++)
        {
            while (true)
            {
                lock (locker) { if (active < maxParallel) { active++; break; } }
                Thread.Sleep(5);
            }
            string ip = baseIp + i.ToString();
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    using (TcpClient probe = new TcpClient())
                    {
                        IAsyncResult ar = probe.BeginConnect(ip, port, null, null);
                        bool ok = ar.AsyncWaitHandle.WaitOne(timeoutMs, false);
                        if (ok && probe.Connected) AddServerLine(ip + "  (5000 açık)");
                    }
                }
                catch { }
                finally { lock (locker) { active--; } }
            });
        }

        while (true) { lock (locker) { if (active == 0) break; } Thread.Sleep(20); }
        AddServerLine("[scan] Bitti.");
    }

    string GetLocalBase24()
    {
        try
        {
            IPHostEntry he = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in he.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    string s = ip.ToString();
                    if (s.StartsWith("10.") || s.StartsWith("192.168.") ||
                        s.StartsWith("172.16.") || s.StartsWith("172.17.") || s.StartsWith("172.18.") ||
                        s.StartsWith("172.19.") || s.StartsWith("172.2") || s.StartsWith("172.3"))
                    {
                        int lastDot = s.LastIndexOf('.');
                        if (lastDot > 0) return s.Substring(0, lastDot + 1);
                    }
                }
            }
        }
        catch { }
        return null;
    }

    void AddServerLine(string s)
    {
        if (lstServers.IsHandleCreated)
            lstServers.BeginInvoke(new Action(delegate { lstServers.Items.Add(s); }));
    }

    // --- helpers ---
    static bool ReadExact(NetworkStream s, byte[] buf, int offset, int len)
    {
        int read = 0;
        while (read < len)
        {
            int n = 0;
            try { n = s.Read(buf, offset + read, len - read); }
            catch { return false; }
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    static string LogPath()
    {
        try { return Path.Combine(Path.GetTempPath(), "Client_err.txt"); }
        catch { return "Client_err.txt"; }
    }

    static void LogError(string where, Exception ex)
    {
        try
        {
            File.AppendAllText(LogPath(),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + where + "] " + ex.ToString() + Environment.NewLine);
        }
        catch { }
    }

    // --- MAIN ---
    [STAThread]
    static void Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) =>
            {
                try { File.AppendAllText(LogPath(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [ThreadException] " + e.Exception + Environment.NewLine); }
                catch { }
                MessageBox.Show("Beklenmeyen hata (ThreadException):\n" + e.Exception.Message + "\n\nDetay: " + LogPath(),
                                "Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                if (ex != null)
                {
                    try { File.AppendAllText(LogPath(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [UnhandledException] " + ex + Environment.NewLine); }
                    catch { }
                    MessageBox.Show("Beklenmeyen hata (UnhandledException):\n" + ex.Message + "\n\nDetay: " + LogPath(),
                                    "Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            Application.Run(new ClientForm());
        }
        catch (Exception exMain)
        {
            try { File.AppendAllText(LogPath(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [MainCatch] " + exMain + Environment.NewLine); }
            catch { }
            MessageBox.Show("Uygulama başlatılamadı:\n" + exMain.Message + "\n\nDetay: " + LogPath(),
                            "Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
