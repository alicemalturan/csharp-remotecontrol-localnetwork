// Server.cs (Legacy-friendly: .NET 4.x / C# 5)
// - 60 FPS ekran yayını (1280x720 downscale, JPEG Q=60)
// - Keyframe + delta blok (x,y,w,h,len,data)
// - İmleç başlığa VIDEO UZAYINDA (targetW x targetH) yazılır → client'ta tam eşleşir
// - Uzaktan kontrol: ayrı TCP (port+1); client video uzayında gönderir, server fiziksele ölçekler
// - TcpClient.NoDelay = true, basit WinForms UI

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class ServerForm : Form
{
    TextBox txtPort;
    Button btnStart;
    CheckBox chkAllowControl;
    Label lblStatus;

    TcpListener listener;
    TcpClient client;
    NetworkStream stream;
    Thread worker;
    volatile bool running = false;

    // Control port (port+1)
    TcpListener ctrlListener;
    TcpClient ctrlClient;
    NetworkStream ctrlStream;
    Thread ctrlWorker;

    Bitmap prevBmp = null;
    int keyframeCounter = 0;
    readonly ImageCodecInfo jpegEncoder;

    // Ekran boyutu ve hedef boyut
    int fullW = 0, fullH = 0;
    const int targetW = 1280;
    const int targetH = 720;

    public ServerForm()
    {
        jpegEncoder = GetJpegEncoder();

        this.Text = "Screen Server (60 FPS • Delta • Control)";
        this.Width = 520; this.Height = 220;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;

        Label lblP = new Label(); lblP.Left = 12; lblP.Top = 20; lblP.Width = 100; lblP.Text = "Base Port:";
        txtPort = new TextBox(); txtPort.Left = 120; txtPort.Top = 16; txtPort.Width = 80; txtPort.Text = "5000";

        chkAllowControl = new CheckBox(); chkAllowControl.Left = 220; chkAllowControl.Top = 18; chkAllowControl.Width = 200; chkAllowControl.Text = "Allow Remote Control";
        chkAllowControl.Checked = false;

        btnStart = new Button(); btnStart.Left = 12; btnStart.Top = 52; btnStart.Width = 480; btnStart.Height = 32; btnStart.Text = "Start Server";
        btnStart.Click += new EventHandler(BtnStart_Click);

        lblStatus = new Label(); lblStatus.Left = 12; lblStatus.Top = 96; lblStatus.Width = 480; lblStatus.Height = 70; lblStatus.Text = "Durum: Beklemede";

        this.Controls.Add(lblP); this.Controls.Add(txtPort);
        this.Controls.Add(chkAllowControl);
        this.Controls.Add(btnStart); this.Controls.Add(lblStatus);

        this.FormClosing += new FormClosingEventHandler(ServerForm_FormClosing);
    }

    void ServerForm_FormClosing(object sender, FormClosingEventArgs e) { StopServer(); }

    void BtnStart_Click(object sender, EventArgs e)
    {
        if (!running)
        {
            int port = 5000; int.TryParse(txtPort.Text.Trim(), out port);
            StartServer(port);
        }
        else StopServer();
    }

    void StartServer(int port)
    {
        try
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            fullW = bounds.Width; fullH = bounds.Height;

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            // control port
            ctrlListener = new TcpListener(IPAddress.Any, port + 1);
            ctrlListener.Start();

            running = true;
            btnStart.Text = "Stop Server";
            lblStatus.Text = "Video @ 0.0.0.0:" + port.ToString() + "  |  Control @ :" + (port + 1).ToString() +
                             "\r\n" + "Screen " + fullW + "x" + fullH + " → Video " + targetW + "x" + targetH + "\r\nİstemci bekleniyor...";

            Thread t = new Thread(AcceptLoop); t.IsBackground = true; t.Start();
            Thread t2 = new Thread(CtrlAcceptLoop); t2.IsBackground = true; t2.Start();
        }
        catch (Exception ex) { MessageBox.Show("Sunucu başlatılamadı: " + ex.Message); }
    }

    void StopServer()
    {
        running = false;

        // video conn
        try { if (stream != null) stream.Close(); } catch { }
        try { if (client != null) client.Close(); } catch { }
        try { if (listener != null) listener.Stop(); } catch { }
        try { if (worker != null) worker.Join(300); } catch { }

        // ctrl conn
        try { if (ctrlStream != null) ctrlStream.Close(); } catch { }
        try { if (ctrlClient != null) ctrlClient.Close(); } catch { }
        try { if (ctrlListener != null) ctrlListener.Stop(); } catch { }
        try { if (ctrlWorker != null) ctrlWorker.Join(300); } catch { }

        if (prevBmp != null) { try { prevBmp.Dispose(); } catch { } prevBmp = null; }
        btnStart.Text = "Start Server";
        lblStatus.Text = "Durum: Durduruldu";
    }

    void AcceptLoop()
    {
        while (running)
        {
            try
            {
                TcpClient pending = listener.AcceptTcpClient();
                pending.NoDelay = true;
                pending.SendBufferSize = 1 << 20;
                pending.ReceiveBufferSize = 1 << 20;

                if (client != null)
                {
                    try { if (stream != null) stream.Close(); } catch { }
                    try { client.Close(); } catch { }
                }
                client = pending;
                stream = client.GetStream();

                this.BeginInvoke(new Action(delegate
                {
                    string ep = "";
                    try { if (client != null && client.Client != null && client.Client.RemoteEndPoint != null) ep = client.Client.RemoteEndPoint.ToString(); } catch { }
                    lblStatus.Text = "Video bağlandı: " + ep + "\r\n" +
                                     "Control: " + (ctrlClient != null && ctrlClient.Connected ? "bağlı" : "bekleniyor") +
                                     " | Allow=" + (chkAllowControl.Checked ? "ON" : "OFF");
                }));

                keyframeCounter = 0;
                if (prevBmp != null) { try { prevBmp.Dispose(); } catch { } prevBmp = null; }

                worker = new Thread(SendLoop); worker.IsBackground = true; worker.Start();
            }
            catch (SocketException) { if (!running) break; }
            catch (Exception) { if (!running) break; }
        }
    }

    void CtrlAcceptLoop()
    {
        while (running)
        {
            try
            {
                TcpClient pending = ctrlListener.AcceptTcpClient();
                pending.NoDelay = true;
                pending.SendBufferSize = 64 * 1024;
                pending.ReceiveBufferSize = 64 * 1024;

                if (ctrlClient != null)
                {
                    try { if (ctrlStream != null) ctrlStream.Close(); } catch { }
                    try { ctrlClient.Close(); } catch { }
                }
                ctrlClient = pending;
                ctrlStream = ctrlClient.GetStream();

                ctrlWorker = new Thread(CtrlReceiveLoop);
                ctrlWorker.IsBackground = true;
                ctrlWorker.Start();

                this.BeginInvoke(new Action(delegate
                {
                    lblStatus.Text = "Control bağlandı. Allow=" + (chkAllowControl.Checked ? "ON" : "OFF");
                }));
            }
            catch (SocketException) { if (!running) break; }
            catch (Exception) { if (!running) break; }
        }
    }

    void SendLoop()
    {
        const long jpegQuality = 60L;
        const int targetFps = 60;
        TimeSpan frameInterval = TimeSpan.FromMilliseconds(1000.0 / targetFps);
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        while (running && client != null && client.Connected)
        {
            TimeSpan t0 = sw.Elapsed;
            try
            {
                using (Bitmap currentFull = CaptureScreenWithCursorScaled(targetW, targetH))
                {
                    keyframeCounter++;
                    bool isFull; byte[] payload;

                    if (prevBmp == null || (keyframeCounter % 30) == 1) // ~0.5 sn'de bir keyframe
                    { payload = EncodeJpeg(currentFull, jpegQuality); isFull = true; }
                    else
                    {
                        byte[] diff = GetDiffBytes(prevBmp, currentFull);
                        if (diff.Length > 0 && diff.Length < 250000) { payload = diff; isFull = false; }
                        else { payload = EncodeJpeg(currentFull, jpegQuality); isFull = true; }
                    }

                    // İmleç: fiziksel → video uzayı
                    POINT p; GetCursorPos(out p);
                    int cx = p.X; int cy = p.Y;
                    if (fullW > 0 && fullH > 0)
                    {
                        // Video koordinatına ölçekle
                        cx = (int)((long)p.X * targetW / (long)fullW);
                        cy = (int)((long)p.Y * targetH / (long)fullH);
                        if (cx < 0) cx = 0; if (cy < 0) cy = 0;
                        if (cx >= targetW) cx = targetW - 1;
                        if (cy >= targetH) cy = targetH - 1;
                    }

                    // Header (16 byte):
                    // 0..3   : int32 dataLen
                    // 4..7   : int32 flags (bit0 = isFull)
                    // 8..11  : int32 cursorX (VIDEO KOORD)
                    // 12..15 : int32 cursorY (VIDEO KOORD)
                    byte[] header = new byte[16];
                    Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, header, 0, 4);
                    int flags = isFull ? 1 : 0;
                    Buffer.BlockCopy(BitConverter.GetBytes(flags), 0, header, 4, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(cx), 0, header, 8, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(cy), 0, header, 12, 4);

                    stream.Write(header, 0, 16);
                    stream.Write(payload, 0, payload.Length);

                    if (prevBmp != null) prevBmp.Dispose();
                    prevBmp = (Bitmap)currentFull.Clone();
                }
            }
            catch
            {
                this.BeginInvoke(new Action(delegate { lblStatus.Text = "Video bağlantı koptu."; }));
                try { if (client != null) client.Close(); } catch { }
                try { if (stream != null) stream.Close(); } catch { }
                break;
            }

            TimeSpan spent = sw.Elapsed - t0;
            TimeSpan wait = frameInterval - spent;
            if (wait.TotalMilliseconds > 0) Thread.Sleep(wait);
        }
    }

    // --- CONTROL RX: client video koord → fiziksel ekrana ölçekle ---
    void CtrlReceiveLoop()
    {
        BinaryReader br = new BinaryReader(ctrlStream);
        while (running && ctrlClient != null && ctrlClient.Connected)
        {
            try
            {
                byte type = br.ReadByte();
                if (!chkAllowControl.Checked) { SkipPayload(br, type); continue; }

                if (type == 1) // move (video koord)
                {
                    int vx = br.ReadInt32();
                    int vy = br.ReadInt32();
                    // video → fiziksel
                    int px = vx, py = vy;
                    if (targetW > 0 && targetH > 0 && fullW > 0 && fullH > 0)
                    {
                        px = (int)((long)vx * fullW / (long)targetW);
                        py = (int)((long)vy * fullH / (long)targetH);
                    }
                    SetCursorPos(px, py);
                }
                else if (type == 2) // down
                {
                    int btn = br.ReadInt32();
                    MouseClick(btn, true);
                }
                else if (type == 3) // up
                {
                    int btn = br.ReadInt32();
                    MouseClick(btn, false);
                }
                else if (type == 4) // wheel
                {
                    int delta = br.ReadInt32();
                    mouse_event(0x0800, 0, 0, delta, 0); // MOUSEEVENTF_WHEEL
                }
                else if (type == 5) // keydown
                {
                    int vk = br.ReadInt32();
                    keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
                }
                else if (type == 6) // keyup
                {
                    int vk = br.ReadInt32();
                    keybd_event((byte)vk, 0, 0x0002, UIntPtr.Zero); // KEYEVENTF_KEYUP
                }
            }
            catch { break; }
        }
    }

    void SkipPayload(BinaryReader br, byte type)
    {
        try
        {
            if (type == 1) { br.ReadInt32(); br.ReadInt32(); }
            else if (type == 2 || type == 3 || type == 5 || type == 6) { br.ReadInt32(); }
            else if (type == 4) { br.ReadInt32(); }
        }
        catch { }
    }

    void MouseClick(int button, bool down)
    {
        // 1=left, 2=right, 3=middle
        if (button == 1) mouse_event(down ? 0x0002u : 0x0004u, 0, 0, 0, 0); // LEFTDOWN/UP
        else if (button == 2) mouse_event(down ? 0x0008u : 0x0010u, 0, 0, 0, 0); // RIGHTDOWN/UP
        else if (button == 3) mouse_event(down ? 0x0020u : 0x0040u, 0, 0, 0, 0); // MIDDLEDOWN/UP
    }

    // --- Capture & Scale + Cursor ---
    Bitmap CaptureScreenWithCursorScaled(int tW, int tH)
    {
        Rectangle bounds = Screen.PrimaryScreen.Bounds;
        Bitmap full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(full))
        { g.CopyFromScreen(Point.Empty, Point.Empty, full.Size); DrawCursor(g); }

        Bitmap bmp = new Bitmap(tW, tH, PixelFormat.Format24bppRgb);
        using (Graphics g2 = Graphics.FromImage(bmp))
        {
            g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g2.DrawImage(full, new Rectangle(0, 0, tW, tH));
        }
        full.Dispose();
        return bmp;
    }

    void DrawCursor(Graphics g)
    {
        CURSORINFO ci = new CURSORINFO();
        ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
        if (GetCursorInfo(out ci) && ci.flags == CURSOR_SHOWING)
        {
            IntPtr hdc = g.GetHdc();
            try { DrawIconEx(hdc, ci.ptScreenPos.X, ci.ptScreenPos.Y, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
            finally { g.ReleaseHdc(hdc); }
        }
    }

    // --- JPEG encode ---
    byte[] EncodeJpeg(Bitmap bmp, long quality)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            if (jpegEncoder == null) bmp.Save(ms, ImageFormat.Jpeg);
            else
            {
                EncoderParameters ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                bmp.Save(ms, jpegEncoder, ep);
            }
            return ms.ToArray();
        }
    }

    ImageCodecInfo GetJpegEncoder()
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
        for (int i = 0; i < codecs.Length; i++) if (codecs[i].MimeType == "image/jpeg") return codecs[i];
        return null;
    }

    // --- Delta: 64x64, (x,y,w,h,len,data) ---
    byte[] GetDiffBytes(Bitmap prev, Bitmap curr)
    {
        const int block = 64;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bool any = false;
            Rectangle fullRect = new Rectangle(0, 0, curr.Width, curr.Height);
            BitmapData d1 = prev.LockBits(fullRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData d2 = curr.LockBits(fullRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int y = 0;
                while (y < curr.Height)
                {
                    int x = 0;
                    while (x < curr.Width)
                    {
                        Rectangle rect = new Rectangle(x, y,
                            Math.Min(block, curr.Width - x),
                            Math.Min(block, curr.Height - y));

                        if (!AreBlocksEqual(d1, d2, rect))
                        {
                            any = true;
                            using (Bitmap blockBmp = curr.Clone(rect, PixelFormat.Format24bppRgb))
                            using (MemoryStream bs = new MemoryStream())
                            {
                                if (jpegEncoder == null) blockBmp.Save(bs, ImageFormat.Jpeg);
                                else
                                {
                                    EncoderParameters ep = new EncoderParameters(1);
                                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 60L);
                                    blockBmp.Save(bs, jpegEncoder, ep);
                                }
                                byte[] data = bs.ToArray();

                                bw.Write((short)rect.X);
                                bw.Write((short)rect.Y);
                                bw.Write((short)rect.Width);
                                bw.Write((short)rect.Height);
                                bw.Write(data.Length);
                                bw.Write(data);
                            }
                        }
                        }
                        x += block;
                    }
                    y += block;
                }
            }
            finally { prev.UnlockBits(d1); curr.UnlockBits(d2); }
            if (any) return ms.ToArray();
            return new byte[0];
        }
    }

    unsafe bool AreBlocksEqual(BitmapData d1, BitmapData d2, Rectangle rect)
    {
        int y = 0;
        while (y < rect.Height)
        {
            byte* row1 = (byte*)d1.Scan0 + ((rect.Y + y) * d1.Stride) + (rect.X * 3);
            byte* row2 = (byte*)d2.Scan0 + ((rect.Y + y) * d2.Stride) + (rect.X * 3);
            int x = 0;
            int rowBytes = rect.Width * 3;
            while (x < rowBytes)
            {
                if (row1[x] != row2[x]) return false;
                x++;
            }
            y++;
        }
        return true;
    }

    // Win32 / Input
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }
    const int CURSOR_SHOWING = 0x00000001; const int DI_NORMAL = 0x0003;

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(out CURSORINFO pci);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, uint dwExtraInfo);
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [STAThread]
    static void Main() { Application.EnableVisualStyles(); Application.Run(new ServerForm()); }
}
