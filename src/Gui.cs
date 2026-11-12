// True Flasher - Made by Adromedary
// Gui.cs - the desktop window (WinForms, ships with Windows - no dependencies).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TrueFlasher
{
    static class GuiProgram
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // ====================================================================== theme + small controls

    static class Theme
    {
        // dark theme
        public static readonly Color Navy = Color.FromArgb(16, 22, 64);        // header start
        public static readonly Color Indigo = Color.FromArgb(64, 80, 170);     // finished stages
        public static readonly Color Teal = Color.FromArgb(18, 184, 176);
        public static readonly Color TealDark = Color.FromArgb(12, 146, 140);
        public static readonly Color Bg = Color.FromArgb(17, 20, 27);
        public static readonly Color Card = Color.FromArgb(27, 31, 41);
        public static readonly Color Input = Color.FromArgb(33, 38, 50);
        public static readonly Color Hover = Color.FromArgb(40, 46, 60);
        public static readonly Color LogBg = Color.FromArgb(12, 14, 19);
        public static readonly Color Text = Color.FromArgb(230, 234, 242);
        public static readonly Color Muted = Color.FromArgb(138, 147, 166);
        public static readonly Color Line = Color.FromArgb(47, 54, 70);
        public static readonly Color Track = Color.FromArgb(42, 48, 62);
        public static readonly Color Green = Color.FromArgb(56, 204, 118);
        public static readonly Color Red = Color.FromArgb(242, 92, 92);
        public static readonly Color Amber = Color.FromArgb(244, 176, 48);
        public static readonly Color Blue = Color.FromArgb(96, 156, 255);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// <summary>Dark title bar (Windows 10 1809+ / 11). Silently ignored elsewhere.</summary>
        public static void DarkTitleBar(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch { }
        }

        /// <summary>Dark scrollbars on text boxes / lists (Windows 10+).</summary>
        public static void DarkScroll(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        public static void Style(TextBox t)
        {
            t.BackColor = Input; t.ForeColor = Text; t.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void Style(NumericUpDown n)
        {
            n.BackColor = Input; n.ForeColor = Text; n.BorderStyle = BorderStyle.FixedSingle;
        }

        /// <summary>Common setup for dialogs.</summary>
        public static void Dialog(Form f, string title, int w, int h)
        {
            f.AutoScaleDimensions = new SizeF(96F, 96F);
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = false; f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.BackColor = Card; f.ForeColor = Text; f.Font = F(9.5f);
            f.ClientSize = new Size(w, h);
            Icon i = AppIcon(32); if (i != null) f.Icon = i;
            f.HandleCreated += delegate { DarkTitleBar(f); };
        }

        public static Font F(float size) { return new Font("Segoe UI", size, FontStyle.Regular); }
        public static Font B(float size) { return new Font("Segoe UI Semibold", size, FontStyle.Regular); }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Icon AppIcon(int size)
        {
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrueFlasher.app.ico");
                if (s == null) return null;
                using (s) return new Icon(s, size, size);
            }
            catch { return null; }
        }

        public static Image Logo()
        {
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrueFlasher.logo.png");
                if (s == null) return null;
                using (s) return new Bitmap(Image.FromStream(s));
            }
            catch { return null; }
        }
    }

    class SmoothBar : Control
    {
        double value;
        public Color FillColor = Theme.Teal;
        public bool ShowText = true;
        string caption;
        /// <summary>Text drawn on the bar instead of the percentage (null = percentage).</summary>
        public string Caption { get { return caption; } set { if (value != caption) { caption = value; Invalidate(); } } }

        public SmoothBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.B(9);
        }

        public double Value
        {
            get { return value; }
            set
            {
                double v = Math.Max(0, Math.Min(1, value));
                if (Math.Abs(v - this.value) < 0.0005) return;
                this.value = v; Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            float rad = Height / 2f;
            using (GraphicsPath track = Theme.Round(r, rad))
            using (SolidBrush b = new SolidBrush(Theme.Track))
                g.FillPath(b, track);
            if (value > 0)
            {
                float w = Math.Max(Height, (float)(r.Width * value));
                RectangleF fr = new RectangleF(0, 0, w, r.Height);
                using (GraphicsPath fill = Theme.Round(fr, rad))
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(1, w), r.Height + 1),
                           ControlPaint.Light(FillColor, 0.25f), FillColor, LinearGradientMode.Horizontal))
                    g.FillPath(b, fill);
            }
            if (ShowText)
            {
                string t = caption ?? (value * 100).ToString("0.0") + " %";
                TextRenderer.DrawText(g, t, Font, new Rectangle(0, 0, Width, Height),
                    value > 0.52 ? Color.White : Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Stage indicator: 0 waiting, 1 running, 2 done, 3 skipped.</summary>
    class Pill : Control
    {
        int state;
        public Pill(string text)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Text = text;
            Font = Theme.B(9.5f);
        }
        public int State { get { return state; } set { state = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg, fg;
            string t = Text;
            switch (state)
            {
                case 1: bg = Theme.Teal; fg = Color.White; t = "▶  " + Text; break;
                case 2: bg = Theme.Indigo; fg = Color.White; t = "✓  " + Text; break;
                case 3: bg = Theme.Track; fg = Theme.Muted; t = Text + "  (skipped)"; break;
                default: bg = Theme.Card; fg = Theme.Muted; break;
            }
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (GraphicsPath p = Theme.Round(r, Height / 2f))
            {
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                if (state == 0) using (Pen pen = new Pen(Theme.Line)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, t, Font, new Rectangle(0, 0, Width, Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    class Card : Panel
    {
        public Color Accent = Color.Empty;
        public Card()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (Accent != Color.Empty)
                using (SolidBrush b = new SolidBrush(Accent)) e.Graphics.FillRectangle(b, 0, 0, 6, Height);
        }
    }

    class StatCard : Card
    {
        readonly Label title, value;
        public StatCard(string t)
        {
            title = new Label { Text = t, Font = Theme.B(8), ForeColor = Theme.Muted, Location = new Point(14, 10), AutoSize = true, BackColor = Color.Transparent };
            value = new Label { Text = "-", Font = Theme.B(12.5f), ForeColor = Theme.Text, Location = new Point(13, 30), AutoSize = false,
                                Size = new Size(210, 28), AutoEllipsis = true, BackColor = Color.Transparent };
            Controls.Add(title); Controls.Add(value);
        }
        public string Title { set { title.Text = value; } }
        public string Value
        {
            set
            {
                if (this.value.Text == value) return;
                this.value.Text = value;
                Fit();
            }
        }

        /// <summary>Shrinks the font (12.5pt down to 9pt) until the text fits the card.</summary>
        void Fit()
        {
            float size = 12.5f;
            Font f = Theme.B(size);
            while (size > 9f && TextRenderer.MeasureText(this.value.Text, f).Width > this.value.Width)
            {
                f.Dispose();
                size -= 0.5f;
                f = Theme.B(size);
            }
            Font old = this.value.Font;
            this.value.Font = f;
            if (old != null && old != f) old.Dispose();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (value != null) { value.Width = Width - 24; Fit(); } }
    }

    /// <summary>Custom-drawn check box that stays visible on the dark theme.</summary>
    class DarkCheck : CheckBox
    {
        bool hover;
        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AutoSize = false;
            Height = 24;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int box = 18;
            RectangleF r = new RectangleF(1, (Height - box) / 2f, box, box);
            Color fill = Checked ? (Enabled ? Theme.Teal : Theme.Muted) : Theme.Input;
            Color border = Checked ? fill : (hover && Enabled ? Theme.Teal : Theme.Muted);
            using (GraphicsPath p = Theme.Round(r, 4))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border, 1.5f)) g.DrawPath(pen, p);
            }
            if (Checked)
                using (Pen pen = new Pen(Color.White, 2.2f))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, new PointF[] { new PointF(r.X + 4.5f, r.Y + 9.5f), new PointF(r.X + 7.8f, r.Y + 12.8f), new PointF(r.X + 13.5f, r.Y + 5.5f) });
                }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(box + 10, 0, Width - box - 10, Height),
                Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Fully custom-painted button so it looks right on the dark theme in every state.</summary>
    class FlatBtn : Button
    {
        readonly bool primary;
        bool hover, down;

        public FlatBtn(string text, bool primary)
        {
            this.primary = primary;
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Theme.B(primary ? 11 : 9.5f);
            UseVisualStyleBackColor = false;
            BackColor = Theme.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parentBg = Parent != null ? Parent.BackColor : Theme.Bg;
            g.Clear(parentBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg, fg, border;
            if (!Enabled) { bg = primary ? Theme.Track : Theme.Card; fg = Color.FromArgb(96, 104, 122); border = Theme.Line; }
            else if (primary) { bg = down ? Theme.Indigo : hover ? Theme.TealDark : Theme.Teal; fg = Color.White; border = bg; }
            else { bg = down ? Theme.Track : hover ? Theme.Hover : Theme.Card; fg = Theme.Text; border = hover ? Theme.Muted : Theme.Line; }
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (GraphicsPath p = Theme.Round(r, 7))
            {
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text.Replace("&&", "&"), Font, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues && Enabled)
                using (Pen pen = new Pen(Color.FromArgb(120, Theme.Teal))) { pen.DashStyle = DashStyle.Dot; g.DrawRectangle(pen, 4, 4, Width - 9, Height - 9); }
        }
    }

    class DriveItem
    {
        public DiskInfo Disk;
        public string Text;
        public override string ToString() { return Text; }
    }

    // ====================================================================== bridge engine -> window

    class GuiUi : IUi
    {
        readonly MainForm form;
        readonly StreamWriter log;
        readonly Stopwatch throttle = Stopwatch.StartNew();
        long lastProgress = -1000, lastStatus = -1000;

        public GuiUi(MainForm form, StreamWriter log) { this.form = form; this.log = log; }

        public void Line(string text)
        {
            if (log != null) lock (log) { log.WriteLine(text); log.Flush(); }
            form.Post(delegate { form.AppendLog(text); });
        }

        public void Status(string text)
        {
            long now = throttle.ElapsedMilliseconds;
            if (now - lastStatus < 150) return;
            lastStatus = now;
            text = text.Replace("  [Q = stop]", "");
            form.Post(delegate { form.SetDetail(text); });
        }

        public void Progress(int stage, double fraction, long bytes, double mbPerSec, string note)
        {
            long now = throttle.ElapsedMilliseconds;
            if (now - lastProgress < 120 && fraction < 1) return;
            lastProgress = now;
            form.Post(delegate { form.SetProgress(stage, fraction, bytes, mbPerSec, note); });
        }

        public bool StopRequested() { return form.ConsumeStop(); }
    }

    // ====================================================================== main window

    class MainForm : Form
    {
        ComboBox cbDrive;
        FlatBtn btnRefresh, btnStart, btnStop, btnFix, btnLog;
        DarkCheck chkQuick;
        Pill[] pills;
        SmoothBar bar;
        Label lblStage, lblElapsed, lblDetail, lblVerdict, lblResult;
        StatCard stData, stSpeed, stCheck;
        Card result;
        TextBox txtLog;
        System.Windows.Forms.Timer clock, deviceDebounce;

        volatile bool stopFlag;
        bool running;
        DateTime startedAt;
        string logPath;
        DiskInfo testedDisk;
        Report lastReport;
        long claimed;

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "True Flasher - Made by Adromedary";
            Icon icon = Theme.AppIcon(32);
            if (icon != null) Icon = icon;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(9.5f);
            HandleCreated += delegate { Theme.DarkTitleBar(this); };
            ClientSize = new Size(760, 676);
            DoubleBuffered = true;

            BuildHeader();
            BuildBody();
            ResumeLayout(false);

            clock = new System.Windows.Forms.Timer { Interval = 1000 };
            clock.Tick += delegate { lblElapsed.Text = "Elapsed  " + Engine.Eta((DateTime.Now - startedAt).TotalSeconds); };
            deviceDebounce = new System.Windows.Forms.Timer { Interval = 1200 };
            deviceDebounce.Tick += delegate { deviceDebounce.Stop(); if (!running) RefreshDrives(); };

            Load += delegate { FitToScreen(); RefreshDrives(); ShowIdleResult(); };
            FormClosing += OnClosing;
        }

        // ---------------------------------------------------------------- layout

        void BuildHeader()
        {
            Panel head = new Panel { Location = new Point(0, 0), Size = new Size(760, 80), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            head.Paint += delegate(object s, PaintEventArgs e)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(head.ClientRectangle, Theme.Navy,
                           Color.FromArgb(10, 78, 100), LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(b, head.ClientRectangle);
            };
            PictureBox pic = new PictureBox { Location = new Point(24, 13), Size = new Size(54, 54), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            pic.Image = Theme.Logo();
            Label title = new Label { Text = "True Flasher", Font = Theme.B(20), ForeColor = Color.White, AutoSize = true, Location = new Point(88, 7), BackColor = Color.Transparent };
            Label sub = new Label { Text = "Finds the real capacity of fake USB drives   ·   Made by Adromedary", Font = Theme.F(9.5f),
                                    ForeColor = Color.FromArgb(190, 222, 238), AutoSize = true, Location = new Point(91, 47), BackColor = Color.Transparent };
            head.Controls.Add(pic); head.Controls.Add(title); head.Controls.Add(sub);
            Controls.Add(head);
        }

        void BuildBody()
        {
            Controls.Add(new Label { Text = "USB DRIVE", Font = Theme.B(8.5f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(24, 92) });

            cbDrive = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(10.5f), Location = new Point(24, 111), Size = new Size(560, 30), FlatStyle = FlatStyle.Flat };
            cbDrive.BackColor = Theme.Input; cbDrive.ForeColor = Theme.Text;
            cbDrive.DrawMode = DrawMode.OwnerDrawFixed; cbDrive.ItemHeight = 22;
            cbDrive.DrawItem += delegate(object o, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (SolidBrush b = new SolidBrush(sel ? Theme.TealDark : Theme.Input)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, cbDrive.Items[e.Index].ToString(), cbDrive.Font,
                    new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height),
                    sel ? Color.White : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            cbDrive.HandleCreated += delegate { Theme.DarkScroll(cbDrive); };
            cbDrive.SelectedIndexChanged += delegate { UpdateButtons(); };
            Controls.Add(cbDrive);

            btnRefresh = new FlatBtn("↻  Refresh", false) { Location = new Point(594, 110), Size = new Size(142, 31) };
            btnRefresh.Click += delegate { RefreshDrives(); };
            Controls.Add(btnRefresh);

            chkQuick = new DarkCheck { Text = "Quick probe only  (about 1 minute - catches common fakes, but not drives that recycle data)",
                                       Location = new Point(24, 146), Size = new Size(712, 24) };
            Controls.Add(chkQuick);

            btnStart = new FlatBtn("Start test", true) { Location = new Point(24, 176), Size = new Size(456, 44) };
            btnStart.Click += delegate { StartTest(); };
            Controls.Add(btnStart);

            btnStop = new FlatBtn("■  Stop stage", false) { Location = new Point(490, 176), Size = new Size(246, 44), Enabled = false };
            btnStop.Click += delegate { stopFlag = true; btnStop.Text = "Stopping..."; };
            new ToolTip().SetToolTip(btnStop, "Stops the current stage. During the fill, what was written is still verified.");
            Controls.Add(btnStop);

            pills = new Pill[] { new Pill("1   Quick probe"), new Pill("2   Fill & spot-check"), new Pill("3   Verify") };
            for (int i = 0; i < 3; i++)
            {
                pills[i].Location = new Point(24 + i * 240, 234);
                pills[i].Size = new Size(232, 32);
                Controls.Add(pills[i]);
            }

            lblStage = new Label { Text = "Ready", Font = Theme.B(10.5f), ForeColor = Theme.Text, Location = new Point(24, 278), Size = new Size(520, 22) };
            lblElapsed = new Label { Text = "", Font = Theme.F(9.5f), ForeColor = Theme.Muted, Location = new Point(536, 279), Size = new Size(200, 22), TextAlign = ContentAlignment.TopRight };
            bar = new SmoothBar { Location = new Point(24, 302), Size = new Size(712, 26) };
            lblDetail = new Label { Text = "Pick a drive and press Start.", ForeColor = Theme.Muted, Location = new Point(24, 332), Size = new Size(712, 20), AutoEllipsis = true };
            Controls.Add(lblStage); Controls.Add(lblElapsed); Controls.Add(bar); Controls.Add(lblDetail);

            stData = new StatCard("WRITTEN") { Location = new Point(24, 358), Size = new Size(232, 64) };
            stSpeed = new StatCard("SPEED") { Location = new Point(264, 358), Size = new Size(140, 64) };
            stCheck = new StatCard("LAST CHECK") { Location = new Point(412, 358), Size = new Size(324, 64) };
            Controls.Add(stData); Controls.Add(stSpeed); Controls.Add(stCheck);

            result = new Card { Location = new Point(24, 434), Size = new Size(712, 124) };
            lblVerdict = new Label { Font = Theme.B(13), Location = new Point(22, 12), Size = new Size(520, 28), AutoEllipsis = true, BackColor = Color.Transparent };
            lblResult = new Label { Font = Theme.F(9.5f), ForeColor = Theme.Text, Location = new Point(22, 42), Size = new Size(520, 78), BackColor = Color.Transparent };
            btnFix = new FlatBtn("Fix drive...", true) { Location = new Point(556, 16), Size = new Size(140, 42), Font = Theme.B(10) };
            btnFix.Click += delegate { FixDrive(); };
            new ToolTip().SetToolTip(btnFix, "Shrink the drive to its real size so it can no longer lose files, and label it with that size.");
            btnLog = new FlatBtn("Open log", false) { Location = new Point(556, 70), Size = new Size(140, 34), Enabled = false };
            btnLog.Click += delegate { if (logPath != null && File.Exists(logPath)) Process.Start(logPath); };
            result.Controls.Add(lblVerdict); result.Controls.Add(lblResult); result.Controls.Add(btnFix); result.Controls.Add(btnLog);
            Controls.Add(result);

            Controls.Add(new Label { Text = "ACTIVITY", Font = Theme.B(8.5f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(24, 568) });
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.5f),
                                   BackColor = Theme.LogBg, ForeColor = Color.FromArgb(168, 178, 198), BorderStyle = BorderStyle.FixedSingle,
                                   Location = new Point(24, 586), Size = new Size(712, 72), WordWrap = true,
                                   Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            txtLog.HandleCreated += delegate { Theme.DarkScroll(txtLog); };
            Controls.Add(txtLog);
        }

        /// <summary>Width is fixed; height can shrink (the activity log gives way) so the window fits
        /// small screens at 150% scaling.</summary>
        void FitToScreen()
        {
            int logRoom = txtLog.Height - (int)(24 * DeviceDpi() / 96f);
            MinimumSize = new Size(Width, Height - Math.Max(0, logRoom));
            MaximumSize = new Size(Width, 4000);
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            if (Height > wa.Height) { Height = Math.Max(MinimumSize.Height, wa.Height); }
            if (Bottom > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
            if (Top < wa.Top) Top = wa.Top;
        }

        int DeviceDpi()
        {
            try { using (Graphics g = CreateGraphics()) return (int)g.DpiY; } catch { return 96; }
        }

        // ---------------------------------------------------------------- helpers used by the worker

        public void Post(MethodInvoker m)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(m); } catch (InvalidOperationException) { }
        }

        public bool ConsumeStop()
        {
            if (!stopFlag) return false;
            stopFlag = false;
            Post(delegate { btnStop.Text = "■  Stop stage"; });
            return true;
        }

        public void AppendLog(string text)
        {
            if (txtLog.TextLength > 200000) txtLog.Text = txtLog.Text.Substring(100000);
            txtLog.AppendText(text.Replace("\n", Environment.NewLine) + Environment.NewLine);
        }

        public void SetDetail(string text) { lblDetail.Text = text; }

        public void SetStage(int stage, string text)
        {
            for (int i = 0; i < 3; i++)
                if (i + 1 < stage && pills[i].State != 3) pills[i].State = 2;
                else if (i + 1 == stage) pills[i].State = 1;
            lblStage.Text = text;
            bar.Value = 0;
            bar.Caption = null;
            stData.Title = stage == 3 ? "VERIFIED" : stage == 1 ? "PROBE" : "WRITTEN";
            stCheck.Title = stage == 3 ? "DAMAGED DATA" : stage == 1 ? "STATUS" : "LAST CHECK";
            stData.Value = "-"; stSpeed.Value = "-"; stCheck.Value = "-";
        }

        public void SetProgress(int stage, double fraction, long bytes, double mbs, string note)
        {
            bar.Value = fraction;
            bar.Caption = stage == 2 ? Engine.Gb(bytes) + " written   \u00B7   stops by itself at the real size" : null;
            if (stage == 2)
            {
                stData.Value = Engine.Gb(bytes) + "  of  " + Engine.Gb(claimed);
                stSpeed.Value = mbs.ToString("F1") + " MB/s";
                stCheck.Value = note;
            }
            else if (stage == 3)
            {
                stData.Value = Engine.Gb(bytes);
                stSpeed.Value = mbs.ToString("F1") + " MB/s";
                stCheck.Value = note;
            }
            else
            {
                stData.Value = (fraction * 100).ToString("0") + " %";
                stSpeed.Value = "-";
                stCheck.Value = note;
            }
        }

        // ---------------------------------------------------------------- drive list

        void RefreshDrives()
        {
            DiskInfo previous = Selected();
            cbDrive.Items.Clear();
            List<DiskInfo> all;
            try { all = DiskList.All(); }
            catch (Exception ex) { AppendLog("Could not list disks: " + ex.Message); all = new List<DiskInfo>(); }
            foreach (DiskInfo d in all)
            {
                if (!d.Eligible) continue;
                string name = (d.Vendor + " " + d.Product).Trim();
                if (name.Length == 0) name = "USB drive";
                string letters = d.Letters.Count > 0 ? string.Join(" ", d.Letters.ToArray()) + "   " : "";
                cbDrive.Items.Add(new DriveItem { Disk = d, Text = letters + name + "   ·   claims " + Engine.Gb(d.Size) + "   ·   Disk " + d.Number + " (" + d.Bus + ")" });
            }
            if (cbDrive.Items.Count == 0)
            {
                cbDrive.Items.Add(new DriveItem { Disk = null, Text = "No USB drive found - plug one in and press Refresh" });
                cbDrive.SelectedIndex = 0;
            }
            else
            {
                cbDrive.SelectedIndex = 0;
                if (previous != null)
                    for (int i = 0; i < cbDrive.Items.Count; i++)
                        if (((DriveItem)cbDrive.Items[i]).Disk.Number == previous.Number) cbDrive.SelectedIndex = i;
            }
            UpdateButtons();
        }

        DiskInfo Selected()
        {
            DriveItem it = cbDrive != null ? cbDrive.SelectedItem as DriveItem : null;
            return it != null ? it.Disk : null;
        }

        void UpdateButtons()
        {
            bool has = Selected() != null;
            btnStart.Enabled = has && !running;
            btnFix.Enabled = has && !running;
            cbDrive.Enabled = btnRefresh.Enabled = chkQuick.Enabled = !running;
            btnStop.Enabled = running;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            const int WM_DEVICECHANGE = 0x0219;
            if (m.Msg == WM_DEVICECHANGE && deviceDebounce != null && !running) { deviceDebounce.Stop(); deviceDebounce.Start(); }
        }

        // ---------------------------------------------------------------- run a test

        void StartTest()
        {
            DiskInfo d = Selected();
            if (d == null) return;
            using (ConfirmForm c = new ConfirmForm(d))
                if (c.ShowDialog(this) != DialogResult.OK) return;

            running = true;
            stopFlag = false;
            testedDisk = d;
            lastReport = null;
            claimed = d.Size;
            txtLog.Clear();
            foreach (Pill p in pills) p.State = 0;
            bool quick = chkQuick.Checked;
            if (quick) { pills[1].State = 3; pills[2].State = 3; }
            ShowRunningResult();
            UpdateButtons();
            startedAt = DateTime.Now;
            lblElapsed.Text = "Elapsed  0m 00s";
            clock.Start();

            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
            logPath = Path.Combine(dir, "TrueFlasher_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_disk" + d.Number + ".log");

            Thread t = new Thread(delegate() { Worker(d, quick); });
            t.IsBackground = true;
            t.Name = "TrueFlasher test";
            t.Start();
        }

        void Worker(DiskInfo d, bool quick)
        {
            Report rep = null;
            string error = null;
            StreamWriter log = null;
            try
            {
                log = new StreamWriter(logPath, false, new UTF8Encoding(false));
                GuiUi ui = new GuiUi(this, log);
                ui.Line("True Flasher " + Application.ProductVersion + " - Made by Adromedary");
                ui.Line("Started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                ui.Line(d.Label + "  serial " + d.Serial + "  claims " + Report.Size(d.Size));

                ProbeResult probe = null;
                FillResult fill = null;
                using (WinDisk dev = new WinDisk(d.Number, d.Size))
                {
                    foreach (string l in dev.LockLog) ui.Line("volume: " + l);
                    string err = dev.CheckWritable();
                    if (err != null) throw new IOException("Cannot write to the drive: " + err);

                    Engine engine = new Engine(dev, ui, new EngineConfig());

                    Post(delegate { SetStage(1, "Stage 1 of 3  \u00B7  Quick probe"); });
                    ui.Line("Stage 1 - quick probe...");
                    probe = engine.QuickProbe();
                    ui.Line("  " + probe.Behaviour + (probe.EstimateBytes > 0 ? "  (estimated real size " + Report.Size(probe.EstimateBytes) + ")" : ""));

                    if (!quick && !probe.DeviceGone)
                    {
                        Post(delegate { SetStage(2, "Stage 2 of 3  \u00B7  Filling with spot checks"); });
                        ui.Line("Stage 2 - fill with spot checks...");
                        fill = engine.FillTest(delegate(string s)
                        {
                            ui.Line(s);
                            Post(delegate { SetStage(3, "Stage 3 of 3  \u00B7  Verifying everything written"); });
                        });
                    }
                    rep = Report.Build(d.Label, d.Size, probe, fill);
                }
                ui.Line("");
                foreach (string l in rep.Lines) ui.Line(l);
                if (rep.SafeBytes > 0)
                {
                    ui.Line("");
                    ui.Line("Fix (manual diskpart): " + string.Join(" / ", DriveFixer.Script(d.Number, rep.SafeBytes, DriveFixer.DefaultLabel(rep.SafeBytes)).ToArray()));
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (log != null) try { log.WriteLine("ERROR: " + ex); } catch { }
            }
            finally
            {
                if (log != null) try { log.Dispose(); } catch { }
            }
            Post(delegate { Finished(rep, error); });
        }

        void Finished(Report rep, string error)
        {
            running = false;
            clock.Stop();
            btnStop.Text = "■  Stop stage";
            lastReport = rep;
            btnLog.Enabled = logPath != null && File.Exists(logPath);
            foreach (Pill p in pills) if (p.State == 1) p.State = 2;
            UpdateButtons();

            if (error != null)
            {
                lblStage.Text = "Stopped with an error";
                bar.Caption = null;
                bar.FillColor = Theme.Red;
                ShowResult(Theme.Red, "ERROR", error);
                AppendLog("ERROR: " + error);
                SystemSounds.Hand.Play();
                return;
            }

            bar.Value = 1;
            bar.Caption = null;
            lblDetail.Text = "Full report saved to " + logPath;
            lblStage.Text = "Finished  -  " + Engine.Eta((DateTime.Now - startedAt).TotalSeconds);
            Color c = rep.Kind == VerdictKind.Genuine ? Theme.Green
                    : rep.Kind == VerdictKind.Fake ? Theme.Red
                    : rep.Kind == VerdictKind.Faulty ? Theme.Amber : Theme.Blue;
            bar.FillColor = c;
            bar.Invalidate();

            StringBuilder sb = new StringBuilder();
            if (rep.RealBytes >= 0)
                sb.AppendLine("Real capacity:  " + Report.Size(rep.RealBytes) + "   (claims " + Engine.Gb(claimed) + ")");
            if (rep.How.Length > 0) sb.AppendLine(rep.How);
            if (rep.SafeBytes > 0)
                sb.AppendLine("Press \"Fix drive\" to shrink it to " + (rep.SafeBytes >> 20).ToString("N0") +
                              " MB so it can never lose files again.");
            else if (rep.Kind == VerdictKind.Genuine || rep.Kind == VerdictKind.Partial)
                sb.AppendLine("The drive has no partition now - press \"Fix drive\" to format it.");
            ShowResult(c, rep.Verdict, sb.ToString().TrimEnd());
            btnFix.Text = rep.SafeBytes > 0 ? "Fix drive..." : "Format...";
            if (rep.Kind == VerdictKind.Fake) SystemSounds.Exclamation.Play(); else SystemSounds.Asterisk.Play();
            FlashTaskbar();
        }

        void ShowResult(Color accent, string verdict, string body)
        {
            result.Accent = accent;
            lblVerdict.ForeColor = accent;
            lblVerdict.Text = verdict;
            lblResult.Text = body;
            result.Invalidate();
        }

        void ShowIdleResult()
        {
            ShowResult(Theme.Line, "No result yet", "Pick the drive and press Start. The test ERASES the drive.\r\n" +
                "Already know the real size from an earlier test? Use \"Fix drive\" to apply it.");
            lblVerdict.ForeColor = Theme.Muted;
        }

        void ShowRunningResult()
        {
            bar.FillColor = Theme.Teal;
            bar.Value = 0;
            ShowResult(Theme.Teal, "Testing...", "You can keep using your PC. The window will show the verdict here when done.\r\n" +
                "\"Stop stage\" ends the current stage early; anything written is still verified.");
            lblVerdict.ForeColor = Theme.Teal;
        }

        // ---------------------------------------------------------------- fix drive

        void FixDrive()
        {
            DiskInfo d = Selected();
            if (d == null) return;
            long suggested = 0;
            if (lastReport != null && testedDisk != null && testedDisk.Number == d.Number && testedDisk.Size == d.Size)
                suggested = lastReport.SafeBytes > 0 ? lastReport.SafeBytes
                          : (lastReport.Kind == VerdictKind.Genuine ? ((d.Size >> 20) - 8) << 20 : 0);   // leave room for alignment

            long bytes; string label;
            using (FixForm f = new FixForm(d, suggested))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                bytes = f.Bytes; label = f.LabelText;
            }

            running = true;
            UpdateButtons();
            btnStop.Enabled = false;
            lblStage.Text = "Partitioning and formatting...";
            bar.FillColor = Theme.Teal; bar.Value = 0.5;
            AppendLog("Fixing disk " + d.Number + ": partition " + (bytes >> 20).ToString("N0") + " MB, label \"" + DriveFixer.CleanLabel(label, bytes) + "\"");

            Thread t = new Thread(delegate()
            {
                string output;
                bool ok = DriveFixer.Run(d.Number, d.Size, bytes, label, out output);
                if (logPath != null) try { File.AppendAllText(logPath, Environment.NewLine + "FIX: " + output + Environment.NewLine); } catch { }
                Post(delegate
                {
                    running = false;
                    bar.Value = 1;
                    AppendLog(output);
                    lblStage.Text = ok ? "Drive fixed" : "Fixing failed";
                    if (ok)
                    {
                        ShowResult(Theme.Green, "DONE - the drive is now a reliable " + Engine.Gb(bytes) + " drive",
                            "Partition: " + (bytes >> 20).ToString("N0") + " MB, " + DriveFixer.FileSystemFor(bytes).ToUpperInvariant() +
                            ", label \"" + DriveFixer.CleanLabel(label, bytes) + "\".\r\n" +
                            "Disk Management will still show the fake total with the rest unallocated - leave that space alone.");
                        SystemSounds.Asterisk.Play();
                    }
                    else
                    {
                        ShowResult(Theme.Red, "Fixing failed", output.Length > 300 ? output.Substring(output.Length - 300) : output);
                        SystemSounds.Hand.Play();
                    }
                    RefreshDrives();
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // ---------------------------------------------------------------- misc

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!running) return;
            DialogResult r = MessageBox.Show(this,
                "A test is still running.\n\nClosing now leaves the drive half-written (you will need to format it). Close anyway?",
                "True Flasher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) e.Cancel = true;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool FlashWindowEx(ref FLASHWINFO fi);

        void FlashTaskbar()
        {
            if (Form.ActiveForm == this) return;
            FLASHWINFO fi = new FLASHWINFO();
            fi.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(FLASHWINFO));
            fi.hwnd = Handle; fi.dwFlags = 3 | 12; fi.uCount = 5; fi.dwTimeout = 0;   // FLASHW_ALL | FLASHW_TIMERNOFG
            try { FlashWindowEx(ref fi); } catch { }
        }
    }

    // ====================================================================== dialogs

    /// <summary>Drag the knob all the way to the right to confirm. Released early, it springs back.</summary>
    class SlideConfirm : Control
    {
        public event EventHandler Confirmed;
        public Color Accent = Theme.Red;
        float knob;            // 0 .. MaxTravel
        bool dragging, done;
        float grab;
        readonly System.Windows.Forms.Timer spring;

        public SlideConfirm(string text)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Text = text;
            Font = Theme.B(10);
            Cursor = Cursors.Hand;
            spring = new System.Windows.Forms.Timer { Interval = 15 };
            spring.Tick += delegate
            {
                knob *= 0.72f;
                if (knob < 1) { knob = 0; spring.Stop(); }
                Invalidate();
            };
        }

        int Pad { get { return 4; } }
        int KnobSize { get { return Height - Pad * 2; } }
        float MaxTravel { get { return Width - KnobSize - Pad * 2; } }

        public void Reset() { done = false; knob = 0; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (done || !Enabled) return;
            float kx = Pad + knob;
            if (e.X >= kx - 4 && e.X <= kx + KnobSize + 4) { dragging = true; grab = e.X - kx; spring.Stop(); Capture = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            knob = Math.Max(0, Math.Min(MaxTravel, e.X - grab - Pad));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false; Capture = false;
            if (knob >= MaxTravel - 2)
            {
                knob = MaxTravel; done = true; Invalidate();
                if (Confirmed != null) Confirmed(this, EventArgs.Empty);
            }
            else spring.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF track = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            float r = Height / 2f;
            float t = MaxTravel > 0 ? knob / MaxTravel : 0;

            using (GraphicsPath p = Theme.Round(track, r))
            {
                using (SolidBrush b = new SolidBrush(Enabled ? Theme.Track : Theme.Input)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(Enabled ? 140 : 40, Accent))) g.DrawPath(pen, p);
            }
            if (knob > 0)
            {
                RectangleF fill = new RectangleF(0.5f, 0.5f, Pad * 2 + knob + KnobSize - 1, Height - 2);
                using (GraphicsPath p = Theme.Round(fill, r))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(70 + (int)(110 * t), Accent))) g.FillPath(b, p);
            }

            string label = done ? "Confirmed" : Text;
            int alpha = done ? 255 : (int)(255 * Math.Max(0, 1 - t * 1.6f));
            Color tc = Color.FromArgb(alpha, Enabled ? Theme.Text : Theme.Muted);
            Rectangle tr = new Rectangle(KnobSize + Pad * 2, 0, Width - KnobSize - Pad * 3, Height);
            TextRenderer.DrawText(g, label + (done ? "" : "   ›››"), Font, tr, Color.FromArgb(alpha, tc),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            RectangleF k = new RectangleF(Pad + knob, Pad, KnobSize, KnobSize);
            using (GraphicsPath p = Theme.Round(k, KnobSize / 2f))
            using (SolidBrush b = new SolidBrush(Enabled ? Accent : Theme.Muted)) g.FillPath(b, p);
            TextRenderer.DrawText(g, done ? "✓" : "→", Theme.B(13), Rectangle.Round(k), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    class ConfirmForm : Form
    {
        public ConfirmForm(DiskInfo d)
        {
            Theme.Dialog(this, "Erase and test this drive?", 470, 224);

            Label warn = new Label { Text = "⚠  Everything on this drive will be erased", Font = Theme.B(12), ForeColor = Theme.Red, Location = new Point(20, 16), AutoSize = true };
            string name = (d.Vendor + " " + d.Product).Trim();
            Label info = new Label
            {
                Text = name + "   (Disk " + d.Number + ", " + d.Bus + ")\r\nClaims " + Report.Size(d.Size) +
                       (d.Letters.Count > 0 ? "\r\nCurrently mounted as " + string.Join(" ", d.Letters.ToArray()) : "") +
                       "\r\n\r\nCopy off anything you need first.",
                Location = new Point(22, 50), Size = new Size(430, 88), ForeColor = Theme.Text
            };
            SlideConfirm slide = new SlideConfirm("Slide to erase & test") { Location = new Point(22, 150), Size = new Size(330, 50) };
            slide.Confirmed += delegate
            {
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 250 };
                t.Tick += delegate { t.Stop(); DialogResult = DialogResult.OK; };
                t.Start();
            };
            FlatBtn cancel = new FlatBtn("Cancel", false) { Location = new Point(364, 157), Size = new Size(88, 36) };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            CancelButton = cancel;
            Controls.Add(warn); Controls.Add(info); Controls.Add(slide); Controls.Add(cancel);
        }
    }

    class FixForm : Form
    {
        readonly NumericUpDown num;
        readonly TextBox label;
        public long Bytes { get { return (long)num.Value << 20; } }
        public string LabelText { get { return label.Text; } }

        public FixForm(DiskInfo d, long suggestedBytes)
        {
            Theme.Dialog(this, "Fix drive - set its real size", 500, 362);

            Label head = new Label { Text = "Make the drive reliable at its real size", Font = Theme.B(12), ForeColor = Theme.Teal, Location = new Point(20, 16), AutoSize = true };
            Label expl = new Label
            {
                Text = "A partition smaller than the real flash keeps every file inside working storage, so the " +
                       "drive can no longer lose data. It is formatted and labelled with its true size.\r\n" +
                       "The drive's firmware will still report " + Engine.Gb(d.Size) + " to Windows; leave the unallocated rest alone.",
                Location = new Point(22, 48), Size = new Size(460, 104), ForeColor = Theme.Text
            };
            Label l1 = new Label { Text = "Partition size (MB)", Location = new Point(22, 160), AutoSize = true, ForeColor = Theme.Muted };
            num = new NumericUpDown { Location = new Point(24, 182), Size = new Size(200, 28), Font = Theme.B(11), Minimum = 16,
                                      Maximum = Math.Max(16, d.Size >> 20), ThousandsSeparator = true, Increment = 1024 };
            Theme.Style(num);
            if (suggestedBytes > 0) num.Value = Math.Min(num.Maximum, Math.Max(16, suggestedBytes >> 20));
            else num.Value = Math.Min(num.Maximum, 16);
            Label hint = new Label
            {
                Text = suggestedBytes > 0 ? "Suggested from your test (real size minus a safety margin)."
                                          : "No test result for this drive yet - enter the safe size from an earlier report.",
                Location = new Point(236, 186), Size = new Size(250, 40), ForeColor = Theme.Muted, Font = Theme.F(8.5f)
            };
            Label l2 = new Label { Text = "Volume label", Location = new Point(22, 224), AutoSize = true, ForeColor = Theme.Muted };
            label = new TextBox { Location = new Point(24, 246), Size = new Size(200, 28), Font = Theme.B(11) };
            Theme.Style(label);
            label.Text = DriveFixer.CleanLabel(DriveFixer.DefaultLabel(Bytes), Bytes);
            Label fs = new Label { Location = new Point(236, 250), Size = new Size(250, 22), ForeColor = Theme.Muted, Font = Theme.F(8.5f) };
            bool userEditedLabel = false;
            label.KeyPress += delegate { userEditedLabel = true; };
            EventHandler sync = delegate
            {
                fs.Text = "File system: " + DriveFixer.FileSystemFor(Bytes).ToUpperInvariant() + "   (" + Engine.Gb(Bytes) + ")";
                if (!userEditedLabel) label.Text = DriveFixer.CleanLabel(DriveFixer.DefaultLabel(Bytes), Bytes);
            };
            num.ValueChanged += sync;
            sync(null, EventArgs.Empty);

            SlideConfirm slide = new SlideConfirm("Slide to partition & format") { Location = new Point(22, 296), Size = new Size(360, 50), Accent = Theme.Teal };
            slide.Confirmed += delegate
            {
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 250 };
                t.Tick += delegate { t.Stop(); DialogResult = DialogResult.OK; };
                t.Start();
            };
            FlatBtn cancel = new FlatBtn("Cancel", false) { Location = new Point(394, 303), Size = new Size(88, 36) };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            CancelButton = cancel;
            Controls.AddRange(new Control[] { head, expl, l1, num, hint, l2, label, fs, slide, cancel });
        }
    }
}
