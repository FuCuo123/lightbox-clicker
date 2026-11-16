using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LightboxDesktopClicker
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int hookId, LowLevelMouseProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] internal static extern IntPtr GetModuleHandle(string moduleName);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [StructLayout(LayoutKind.Sequential)] internal struct MouseHookData { internal Point Point; internal uint MouseData, Flags, Time; internal IntPtr ExtraInfo; }
        internal delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);
        internal const uint LeftDown = 0x0002, LeftUp = 0x0004, NoRepeat = 0x4000;
        internal const int HotkeyMessage = 0x0312;
        internal const int LowLevelMouseHook = 14;
        internal const uint ModAlt = 1, ModControl = 2, ModShift = 4;
    }

    internal static class ClickerTheme
    {
        internal static readonly Color Ink = Color.FromArgb(48, 39, 126);
        internal static readonly Color Purple = Color.FromArgb(114, 83, 239);
        internal static Image ResourceImage(string name)
        {
            Stream stream = typeof(ClickerTheme).Assembly.GetManifestResourceStream("LightboxDesktopClicker." + name);
            if (stream == null) return null;
            using (stream) using (Bitmap source = new Bitmap(stream)) return new Bitmap(source);
        }
        internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int d = radius * 2; GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
    }

    internal sealed class ProportionalResizer
    {
        private sealed class Item
        {
            internal Control Control;
            internal Rectangle Bounds;
            internal float FontSize;
        }
        private readonly Form form;
        private readonly Size designSize;
        private readonly List<Item> items = new List<Item>();
        private bool resizing;
        internal ProportionalResizer(Form target)
        {
            form = target; designSize = target.ClientSize;
            foreach (Control control in target.Controls)
            {
                control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                items.Add(new Item { Control = control, Bounds = control.Bounds, FontSize = control.Font.Size });
            }
            target.Resize += Resize;
        }
        private void Resize(object sender, EventArgs e)
        {
            if (resizing || designSize.Width <= 0 || designSize.Height <= 0) return;
            float scale = Math.Min(form.ClientSize.Width / (float)designSize.Width, form.ClientSize.Height / (float)designSize.Height);
            if (scale <= 0) return;
            int offsetX = (form.ClientSize.Width - (int)Math.Round(designSize.Width * scale)) / 2;
            int offsetY = (form.ClientSize.Height - (int)Math.Round(designSize.Height * scale)) / 2;
            resizing = true; form.SuspendLayout();
            foreach (Item item in items)
            {
                if (item.Control.IsDisposed) continue;
                Rectangle b = item.Bounds;
                item.Control.Bounds = new Rectangle(offsetX + (int)Math.Round(b.X * scale), offsetY + (int)Math.Round(b.Y * scale), Math.Max(1, (int)Math.Round(b.Width * scale)), Math.Max(1, (int)Math.Round(b.Height * scale)));
                item.Control.Font = new Font(item.Control.Font.FontFamily, Math.Max(8F, item.FontSize * scale), item.Control.Font.Style);
                item.Control.Invalidate(true);
            }
            form.ResumeLayout(true); resizing = false;
        }
    }

    internal abstract class ProportionalForm : Form
    {
        private double aspect;
        internal void LockAspectRatio() { aspect = Width / (double)Math.Max(1, Height); }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0214 && aspect > 0)
            {
                NativeRect rect = (NativeRect)Marshal.PtrToStructure(message.LParam, typeof(NativeRect));
                int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
                int edge = message.WParam.ToInt32();
                bool horizontal = edge == 1 || edge == 2;
                bool left = edge == 1 || edge == 4 || edge == 7;
                bool top = edge == 3 || edge == 4 || edge == 5;
                if (horizontal)
                {
                    height = (int)Math.Round(width / aspect);
                    if (top) rect.Top = rect.Bottom - height; else rect.Bottom = rect.Top + height;
                }
                else
                {
                    width = (int)Math.Round(height * aspect);
                    if (left) rect.Left = rect.Right - width; else rect.Right = rect.Left + width;
                }
                Marshal.StructureToPtr(rect, message.LParam, false);
            }
            base.WndProc(ref message);
        }
    }

    internal sealed class SoftCard : Panel
    {
        internal SoftCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.FromArgb(252, 251, 255);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(BackColor);
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = ClickerTheme.RoundedRectangle(bounds, 18))
            using (LinearGradientBrush brush = new LinearGradientBrush(bounds, Color.FromArgb(255, 254, 255), Color.FromArgb(246, 243, 255), 90F))
            using (Pen border = new Pen(Color.FromArgb(218, 210, 255), 1.4F)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(border, path); }
        }
    }

    internal sealed class HotkeyBinding
    {
        internal uint Key;
        internal uint Modifiers;
        internal HotkeyBinding(uint key, uint modifiers) { Key = key; Modifiers = modifiers; }
        internal string Label
        {
            get
            {
                string text = "";
                if ((Modifiers & NativeMethods.ModControl) != 0) text += "Ctrl+";
                if ((Modifiers & NativeMethods.ModAlt) != 0) text += "Alt+";
                if ((Modifiers & NativeMethods.ModShift) != 0) text += "Shift+";
                string keyName = (Key >= 0x70 && Key <= 0x7B) ? "F" + (Key - 0x6F) : Key == 0x13 ? "Pause" : Key == 0x2D ? "Insert" : Key == 0x24 ? "Home" : Key == 0x23 ? "End" : Key == 0x21 ? "PageUp" : Key == 0x22 ? "PageDown" : Key == 0x91 ? "ScrollLock" : Enum.GetName(typeof(Keys), (int)Key);
                if (String.IsNullOrEmpty(keyName)) keyName = "键码" + Key;
                return text + keyName;
            }
        }
        public override string ToString() { return Label; }
    }

    internal sealed class ColorWheelControl : Control
    {
        private float hue = 260, saturation = .66f, brightness = .86f;
        private bool dragging;
        private Bitmap ringBitmap;
        internal Color SelectedColor { get { return Hsv(hue, saturation, brightness); } }
        internal event EventHandler ColorChanged;
        internal ColorWheelControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(230, 230); MinimumSize = new Size(180, 180); RebuildRing();
        }
        internal void SetColor(Color color)
        {
            hue = color.GetHue(); saturation = color.GetSaturation();
            float max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255f; brightness = max;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); int side = Math.Min(ClientSize.Width, ClientSize.Height), cx = ClientSize.Width / 2, cy = ClientSize.Height / 2;
            float scale = side / 230f, outer = 108 * scale, ringInner = 78 * scale, inner = 72 * scale;
            if (ringBitmap != null) e.Graphics.DrawImageUnscaled(ringBitmap, (ClientSize.Width - side) / 2, (ClientSize.Height - side) / 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF innerRect = new RectangleF(cx - inner, cy - inner, inner * 2, inner * 2);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(innerRect);
                using (PathGradientBrush fill = new PathGradientBrush(path))
                {
                    fill.CenterColor = Color.White; fill.SurroundColors = new Color[] { Hsv(hue, 1, 1) };
                    e.Graphics.FillPath(fill, path);
                }
                GraphicsState state = e.Graphics.Save(); e.Graphics.SetClip(path);
                using (LinearGradientBrush shade = new LinearGradientBrush(new Point(0, (int)(cy - inner)), new Point(0, (int)(cy + inner)), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(170, 0, 0, 0)))
                    e.Graphics.FillRectangle(shade, cx - inner, cy - inner, inner * 2, inner * 2);
                e.Graphics.Restore(state);
            }
            float hueAngle = hue * (float)Math.PI / 180f; float hx = cx + (ringInner + outer) / 2 * (float)Math.Cos(hueAngle), hy = cy + (ringInner + outer) / 2 * (float)Math.Sin(hueAngle);
            using (Pen p = new Pen(Color.White, 2)) { e.Graphics.DrawEllipse(p, hx - 10 * scale, hy - 10 * scale, 20 * scale, 20 * scale); }
            float satRadius = saturation * inner, selectedX = cx + satRadius * (float)Math.Cos(hueAngle), selectedY = cy + satRadius * (float)Math.Sin(hueAngle);
            using (Pen p = new Pen(Color.White, 2.2f)) using (SolidBrush b = new SolidBrush(SelectedColor)) { e.Graphics.FillEllipse(b, selectedX - 7 * scale, selectedY - 7 * scale, 14 * scale, 14 * scale); e.Graphics.DrawEllipse(p, selectedX - 7 * scale, selectedY - 7 * scale, 14 * scale, 14 * scale); }
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; Capture = true; UpdateFromPoint(e.Location); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) UpdateFromPoint(e.Location); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = false; Capture = false; } base.OnMouseUp(e); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); RebuildRing(); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing && ringBitmap != null) { ringBitmap.Dispose(); ringBitmap = null; } base.Dispose(disposing); }
        private void RebuildRing()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            if (ringBitmap != null) { ringBitmap.Dispose(); ringBitmap = null; }
            int side = Math.Min(ClientSize.Width, ClientSize.Height), offsetX = (ClientSize.Width - side) / 2, offsetY = (ClientSize.Height - side) / 2, center = side / 2;
            float scale = side / 230f, outer = 108 * scale, ringInner = 78 * scale;
            ringBitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
            {
                float dx = x - center, dy = y - center, d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d >= ringInner && d <= outer) ringBitmap.SetPixel(x + offsetX, y + offsetY, Hsv((float)((Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360), 1, 1));
            }
        }
        private void UpdateFromPoint(Point p)
        {
            float cx = ClientSize.Width / 2f, cy = ClientSize.Height / 2f, scale = Math.Min(ClientSize.Width, ClientSize.Height) / 230f;
            float dx = p.X - cx, dy = p.Y - cy, distance = (float)Math.Sqrt(dx * dx + dy * dy), angle = (float)(Math.Atan2(dy, dx) * 180 / Math.PI); if (angle < 0) angle += 360;
            if (distance >= 72 * scale) hue = angle;
            else { saturation = Math.Min(1, distance / (72 * scale)); brightness = 1f - .52f * Math.Max(0, Math.Min(1, (dy / (72 * scale) + 1f) / 2f)); }
            Invalidate(); if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
        }
        private static Color Hsv(float h, float s, float v)
        {
            h = (h % 360 + 360) % 360; s = Math.Max(0, Math.Min(1, s)); v = Math.Max(0, Math.Min(1, v));
            float chroma = v * s, x = chroma * (1 - Math.Abs((h / 60f) % 2 - 1)), m = v - chroma, r = 0, g = 0, b = 0;
            if (h < 60) { r = chroma; g = x; } else if (h < 120) { r = x; g = chroma; } else if (h < 180) { g = chroma; b = x; } else if (h < 240) { g = x; b = chroma; } else if (h < 300) { r = x; b = chroma; } else { r = chroma; b = x; }
            return Color.FromArgb(255, (int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }
    }

    internal sealed class ClickerSettings
    {
        internal HotkeyBinding[] Hotkeys = new HotkeyBinding[] { new HotkeyBinding(0x75, 0), new HotkeyBinding(0x76, 0), new HotkeyBinding(0x77, 0), new HotkeyBinding(0x78, 0), new HotkeyBinding(0x79, 0) };
        internal int MarkerSize = 50, Interval = 100, Rounds = 1;
        internal int MarkerColor = Color.FromArgb(34, 36, 34).ToArgb(), NumberColor = Color.FromArgb(92, 75, 220).ToArgb();
        internal bool RecordedTiming = true, MarkersVisible = true;
        private static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightboxToolbox", "clicker-settings.ini"); } }
        internal static ClickerSettings Load()
        {
            ClickerSettings s = new ClickerSettings();
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split('='); if (p.Length != 2) continue; int n; bool b;
                    if (p[0] == "size" && int.TryParse(p[1], out n)) s.MarkerSize = Math.Max(32, Math.Min(100, n));
                    else if (p[0] == "interval" && int.TryParse(p[1], out n)) s.Interval = Math.Max(20, Math.Min(10000, n));
                    else if (p[0] == "rounds" && int.TryParse(p[1], out n)) s.Rounds = Math.Max(1, Math.Min(10000, n));
                    else if (p[0] == "markerColor" && int.TryParse(p[1], out n)) s.MarkerColor = n;
                    else if (p[0] == "numberColor" && int.TryParse(p[1], out n)) s.NumberColor = n;
                    else if (p[0] == "recorded" && bool.TryParse(p[1], out b)) s.RecordedTiming = b;
                    else if (p[0] == "visible" && bool.TryParse(p[1], out b)) s.MarkersVisible = b;
                    else if (p[0].StartsWith("hotkey") && int.TryParse(p[0].Substring(6), out n) && n >= 0 && n < 5)
                    { string[] pair = p[1].Split(','); uint k, m; if (pair.Length == 2 && uint.TryParse(pair[0], out k) && uint.TryParse(pair[1], out m)) s.Hotkeys[n] = new HotkeyBinding(k, m); }
                }
            }
            catch { }
            return s;
        }
        internal void Save()
        {
            string path = FilePath; Directory.CreateDirectory(Path.GetDirectoryName(path));
            List<string> lines = new List<string>();
            lines.Add("size=" + MarkerSize); lines.Add("interval=" + Interval); lines.Add("rounds=" + Rounds);
            lines.Add("markerColor=" + MarkerColor); lines.Add("numberColor=" + NumberColor); lines.Add("recorded=" + RecordedTiming); lines.Add("visible=" + MarkersVisible);
            for (int i = 0; i < 5; i++) lines.Add("hotkey" + i + "=" + Hotkeys[i].Key + "," + Hotkeys[i].Modifiers);
            File.WriteAllLines(path, lines.ToArray());
        }
    }

    internal sealed class SettingsForm : ProportionalForm
    {
        private readonly TextBox[] hotkeyFields = new TextBox[5];
        private readonly HotkeyBinding[] bindings = new HotkeyBinding[5];
        private readonly NumericUpDown size = new NumericUpDown(), interval = new NumericUpDown(), rounds = new NumericUpDown();
        private readonly CheckBox recorded = new CheckBox(), visible = new CheckBox();
        private readonly Button markerTarget = new Button(), numberTarget = new Button();
        private readonly ColorWheelControl colorWheel = new ColorWheelControl();
        private readonly MarkerPreview preview = new MarkerPreview();
        private Color marker, number;
        private bool editingNumberColor;
        private int captureIndex = -1;
        private readonly Label title;
        private readonly Label help;
        private ProportionalResizer resizer;
        internal ClickerSettings Result;
        private static readonly string[] Names = { "开始/停止重放", "紧急停止", "手动添加点位", "开始/停止录制", "显示/隐藏准星" };
        internal SettingsForm(ClickerSettings current)
        {
            Result = new ClickerSettings(); Result.Hotkeys = new HotkeyBinding[5];
            Result.MarkerSize = current.MarkerSize; Result.Interval = current.Interval; Result.Rounds = current.Rounds; Result.MarkerColor = current.MarkerColor; Result.NumberColor = current.NumberColor; Result.RecordedTiming = current.RecordedTiming; Result.MarkersVisible = current.MarkersVisible;
            marker = Color.FromArgb(current.MarkerColor); number = Color.FromArgb(current.NumberColor);
            Text = "轻盒 · 自定义设置"; ClientSize = new Size(700, 700); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true; MinimizeBox = true; KeyPreview = true; Font = new Font("Microsoft YaHei UI", 9F); BackColor = Color.FromArgb(247, 245, 255); MinimumSize = Size;
            title = new Label { Text = "⚙  自定义设置", Location = new Point(20, 16), Size = new Size(380, 28), Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold) }; Controls.Add(title);
            help = new Label { Text = "点击快捷键框后，直接按下要设置的按键；可同时按 Ctrl / Alt / Shift。", Location = new Point(20, 48), Size = new Size(390, 35), ForeColor = Color.Gray }; Controls.Add(help);
            for (int i = 0; i < 5; i++)
            {
                Controls.Add(new Label { Text = Names[i], Location = new Point(22, 91 + i * 38), Size = new Size(165, 26), TextAlign = ContentAlignment.MiddleLeft });
                bindings[i] = new HotkeyBinding(current.Hotkeys[i].Key, current.Hotkeys[i].Modifiers);
                hotkeyFields[i] = new TextBox { Location = new Point(190, 91 + i * 38), Size = new Size(210, 27), ReadOnly = true, TabStop = true, TextAlign = HorizontalAlignment.Center };
                UpdateHotkeyField(i, false); Controls.Add(hotkeyFields[i]);
                int index = i; hotkeyFields[i].Click += delegate { captureIndex = index; UpdateHotkeyField(index, true); hotkeyFields[index].Focus(); };
            }
            Controls.Add(new Label { Text = "准星大小", Location = new Point(22, 291), Size = new Size(125, 25) }); ConfigureNumber(size, 155, 289, 32, 100, current.MarkerSize);
            markerTarget.Text = "设置准星颜色"; markerTarget.Location = new Point(22, 324); markerTarget.Size = new Size(180, 28); markerTarget.FlatStyle = FlatStyle.Flat; Controls.Add(markerTarget);
            numberTarget.Text = "设置编号颜色"; numberTarget.Location = new Point(215, 324); numberTarget.Size = new Size(180, 28); numberTarget.FlatStyle = FlatStyle.Flat; Controls.Add(numberTarget);
            markerTarget.Click += delegate { editingNumberColor = false; colorWheel.SetColor(marker); UpdateColorSelection(); }; numberTarget.Click += delegate { editingNumberColor = true; colorWheel.SetColor(number); UpdateColorSelection(); };
            colorWheel.Location = new Point(120, 358); colorWheel.SetColor(marker); colorWheel.ColorChanged += delegate { if (editingNumberColor) number = colorWheel.SelectedColor; else marker = colorWheel.SelectedColor; UpdateColorSelection(); }; Controls.Add(colorWheel);
            Controls.Add(preview);
            UpdateColorSelection();
            Controls.Add(new Label { Text = "默认间隔（毫秒）", Location = new Point(22, 602), Size = new Size(125, 25) }); ConfigureNumber(interval, 155, 600, 20, 10000, current.Interval);
            Controls.Add(new Label { Text = "默认重放轮数", Location = new Point(22, 636), Size = new Size(125, 25) }); ConfigureNumber(rounds, 155, 634, 1, 10000, current.Rounds);
            recorded.Text = "默认按录制间隔重放"; recorded.Checked = current.RecordedTiming; recorded.Location = new Point(22, 672); recorded.AutoSize = true; Controls.Add(recorded);
            visible.Text = "启动时显示桌面准星"; visible.Checked = current.MarkersVisible; visible.Location = new Point(220, 672); visible.AutoSize = true; Controls.Add(visible);
            Button save = new Button { Text = "保存设置", Location = new Point(210, 710), Size = new Size(90, 32), DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "取消", Location = new Point(310, 710), Size = new Size(90, 32), DialogResult = DialogResult.Cancel }; Controls.Add(save); Controls.Add(cancel); AcceptButton = save;
            ApplySettingsLayout();
            resizer = new ProportionalResizer(this);
            LockAspectRatio();
            KeyDown += OnCaptureKeyDown;
            save.Click += delegate { if (captureIndex >= 0) { MessageBox.Show(this, "请先完成快捷键录入。", "快捷键录入中", MessageBoxButtons.OK, MessageBoxIcon.Information); DialogResult = DialogResult.None; return; } if (!ValidateHotkeys()) { DialogResult = DialogResult.None; return; } for (int i = 0; i < 5; i++) Result.Hotkeys[i] = bindings[i]; Result.MarkerSize = Decimal.ToInt32(size.Value); Result.MarkerColor = marker.ToArgb(); Result.NumberColor = number.ToArgb(); Result.Interval = Decimal.ToInt32(interval.Value); Result.Rounds = Decimal.ToInt32(rounds.Value); Result.RecordedTiming = recorded.Checked; Result.MarkersVisible = visible.Checked; };
        }
        private void ApplySettingsLayout()
        {
            title.Location = new Point(28, 18); title.Size = new Size(680, 34); title.ForeColor = Color.FromArgb(53, 43, 126); title.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
            help.Location = new Point(30, 55); help.Size = new Size(680, 28); help.ForeColor = Color.FromArgb(119, 111, 158);
            AddCard(new Rectangle(374, 144, 586, 286));
            AddCard(new Rectangle(374, 440, 586, 330));
            AddCard(new Rectangle(660, 535, 275, 230));
            AddCard(new Rectangle(374, 780, 586, 126));
            PictureBox art = new PictureBox { Location = new Point(0, 52), Size = new Size(350, 914), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom, SizeMode = PictureBoxSizeMode.StretchImage, Image = ClickerTheme.ResourceImage("settings-mascot.png"), BackColor = Color.FromArgb(249, 247, 255) };
            Controls.Add(art); art.SendToBack();
            for (int i = 0; i < 5; i++)
            {
                // The named controls are laid out explicitly below; labels are found by their caption.
                foreach (Control control in Controls) if (control is Label && ((Label)control).Text == Names[i]) { control.Location = new Point(400, 163 + i * 47); control.Size = new Size(190, 34); control.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold); control.ForeColor = Color.FromArgb(64, 56, 120); break; }
                hotkeyFields[i].Location = new Point(590, 163 + i * 47); hotkeyFields[i].Size = new Size(340, 36); hotkeyFields[i].Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; hotkeyFields[i].Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold); hotkeyFields[i].ForeColor = Color.FromArgb(55, 45, 132); hotkeyFields[i].BorderStyle = BorderStyle.FixedSingle;
            }
            foreach (Control control in Controls)
            {
                if (control is Label && ((Label)control).Text == "准星大小") { control.Location = new Point(400, 450); control.Size = new Size(150, 30); control.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold); control.ForeColor = Color.FromArgb(75, 62, 156); }
                if (control is Label && ((Label)control).Text == "默认间隔（毫秒）") { control.Location = new Point(400, 786); control.Size = new Size(180, 28); control.ForeColor = Color.FromArgb(75, 62, 156); }
                if (control is Label && ((Label)control).Text == "默认重放轮数") { control.Location = new Point(675, 786); control.Size = new Size(170, 28); control.ForeColor = Color.FromArgb(75, 62, 156); }
            }
            size.Location = new Point(560, 450); size.Size = new Size(350, 32);
            markerTarget.Location = new Point(400, 490); markerTarget.Size = new Size(255, 38);
            numberTarget.Location = new Point(670, 490); numberTarget.Size = new Size(255, 38);
            colorWheel.Location = new Point(405, 535); colorWheel.Size = new Size(232, 232);
            preview.Location = new Point(675, 552); preview.Size = new Size(245, 195);
            interval.Location = new Point(510, 783); interval.Size = new Size(125, 32);
            rounds.Location = new Point(795, 783); rounds.Size = new Size(125, 32);
            recorded.Location = new Point(400, 830); recorded.Size = new Size(255, 28);
            visible.Location = new Point(675, 830); visible.Size = new Size(255, 28);
            foreach (Control control in Controls)
            {
                if (control is CheckBox) { control.ForeColor = Color.FromArgb(73, 65, 133); control.BackColor = Color.FromArgb(252, 251, 255); control.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold); }
                if (control is NumericUpDown) { control.BackColor = Color.White; control.ForeColor = Color.FromArgb(53, 43, 126); control.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold); }
                if (control is Button && control != markerTarget && control != numberTarget)
                {
                    Button button = (Button)control; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(190, 178, 255); button.FlatAppearance.BorderSize = 1; button.BackColor = Color.FromArgb(246, 243, 255); button.ForeColor = Color.FromArgb(83, 65, 176); button.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
                }
            }
            markerTarget.FlatAppearance.BorderColor = Color.FromArgb(185, 169, 255); numberTarget.FlatAppearance.BorderColor = Color.FromArgb(185, 169, 255);
            // Save and cancel buttons are the two buttons without a hotkey role.
            Button[] actions = new Button[2]; int count = 0;
            foreach (Control control in Controls) if (control is Button && control != markerTarget && control != numberTarget && count < 2) actions[count++] = (Button)control;
            if (count == 2) { actions[0].Text = "✓  保存设置"; actions[0].Location = new Point(520, 915); actions[0].Size = new Size(220, 42); actions[0].BackColor = Color.FromArgb(112, 84, 239); actions[0].ForeColor = Color.White; actions[0].FlatAppearance.BorderColor = Color.FromArgb(142, 119, 255); actions[1].Text = "✕  取消"; actions[1].Location = new Point(755, 915); actions[1].Size = new Size(170, 42); }
            title.Text = "⚙  自定义设置"; title.Location = new Point(400, 69); title.Size = new Size(520, 44); title.Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold);
            help.Location = new Point(402, 112); help.Size = new Size(530, 27); help.ForeColor = Color.FromArgb(119, 111, 158);
            ScaleSettingsControls(.72F);
        }
        private void ScaleSettingsControls(float scale)
        {
            foreach (Control control in Controls)
            {
                int originalTop = control.Top;
                control.Location = new Point((int)Math.Round(control.Left * scale), (int)Math.Round(control.Top * scale));
                control.Size = new Size(Math.Max(1, (int)Math.Round(control.Width * scale)), Math.Max(1, (int)Math.Round(control.Height * scale)));
                control.Font = new Font(control.Font.FontFamily, Math.Max(8.5F, control.Font.Size * scale), control.Font.Style);
                if (control is SoftCard) control.Anchor = originalTop >= 700 ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom : AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                if (control is Button && control != markerTarget && control != numberTarget) control.Anchor = ((Button)control).Text.Contains("取消") ? AnchorStyles.Right | AnchorStyles.Bottom : AnchorStyles.Left | AnchorStyles.Bottom;
            }
            colorWheel.MinimumSize = new Size(140, 140);
            foreach (Control control in Controls)
            {
                if (control == saveControl()) control.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                if (control == cancelControl()) control.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                if (control is TextBox) control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                if (control == numberTarget) control.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                if (control == size) control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                if (control == interval || control == recorded) control.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                if (control == rounds || control == visible) control.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                if (control is Label && ((Label)control).Text == "默认重放轮数") control.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                if (control is Label && ((Label)control).Text == "默认间隔（毫秒）") control.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            }
        }
        private Control saveControl() { foreach (Control control in Controls) if (control is Button && ((Button)control).Text.Contains("保存设置")) return control; return null; }
        private Control cancelControl() { foreach (Control control in Controls) if (control is Button && ((Button)control).Text.Contains("取消")) return control; return null; }
        private void AddCard(Rectangle bounds)
        {
            Panel card = new SoftCard { Bounds = bounds };
            Controls.Add(card); card.SendToBack();
        }
        private void UpdateHotkeyField(int index, bool capturing) { hotkeyFields[index].Text = capturing ? "请按下快捷键…（Esc 取消录入）" : bindings[index].Label; hotkeyFields[index].BackColor = capturing ? Color.FromArgb(239, 237, 255) : SystemColors.Window; }
        private void OnCaptureKeyDown(object sender, KeyEventArgs e)
        {
            if (captureIndex < 0) return;
            e.SuppressKeyPress = true; e.Handled = true;
            if (e.KeyCode == Keys.Escape) { int canceled = captureIndex; captureIndex = -1; UpdateHotkeyField(canceled, false); return; }
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return;
            uint modifiers = 0; if (e.Control) modifiers |= NativeMethods.ModControl; if (e.Alt) modifiers |= NativeMethods.ModAlt; if (e.Shift) modifiers |= NativeMethods.ModShift;
            bindings[captureIndex] = new HotkeyBinding((uint)e.KeyCode, modifiers);
            int assigned = captureIndex; captureIndex = -1; UpdateHotkeyField(assigned, false);
        }
        private static void ConfigureNumber(NumericUpDown control, int x, int y, int min, int max, int value) { control.Location = new Point(x, y); control.Size = new Size(100, 26); control.Minimum = min; control.Maximum = max; control.Value = Math.Max(min, Math.Min(max, value)); }
        private void UpdateColorSelection() { markerTarget.BackColor = marker; markerTarget.ForeColor = marker.GetBrightness() < .5 ? Color.White : Color.Black; numberTarget.BackColor = number; numberTarget.ForeColor = number.GetBrightness() < .5 ? Color.White : Color.Black; markerTarget.Text = (editingNumberColor ? "选择准星颜色 · " : "✓ 准星颜色 · ") + ColorName(marker); numberTarget.Text = (editingNumberColor ? "✓ 编号颜色 · " : "选择编号颜色 · ") + ColorName(number); preview.MarkerColor = marker; preview.NumberColor = number; }
        private static string ColorName(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        private bool ValidateHotkeys() { HashSet<string> used = new HashSet<string>(); foreach (HotkeyBinding binding in bindings) { string key = binding.Key + ":" + binding.Modifiers; if (!used.Add(key)) { MessageBox.Show(this, "每个操作需要使用不同的快捷键组合。", "快捷键重复", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; } } return true; }
    }

    internal sealed class MarkerPreview : Control
    {
        internal Color MarkerColor = Color.FromArgb(193, 167, 178);
        internal Color NumberColor = Color.FromArgb(255, 255, 255);
        internal MarkerPreview() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.FromArgb(252, 251, 255); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Min(Width / 245F, Height / 195F); int cx = Width / 2, cy = (int)(Height * .53F);
            using (Font label = new Font("Microsoft YaHei UI", Math.Max(8F, 10F * scale), FontStyle.Bold)) using (SolidBrush text = new SolidBrush(ClickerTheme.Ink)) e.Graphics.DrawString("颜色预览", label, text, 8, 5);
            using (Pen p = new Pen(MarkerColor, 4F * scale)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; e.Graphics.DrawLine(p, cx - 29 * scale, cy, cx - 12 * scale, cy); e.Graphics.DrawLine(p, cx + 12 * scale, cy, cx + 29 * scale, cy); e.Graphics.DrawLine(p, cx, cy - 29 * scale, cx, cy - 12 * scale); e.Graphics.DrawLine(p, cx, cy + 12 * scale, cx, cy + 29 * scale); }
            using (Pen p = new Pen(MarkerColor, 4F * scale)) using (SolidBrush b = new SolidBrush(Color.FromArgb(245, 242, 255))) { e.Graphics.FillEllipse(b, cx - 14 * scale, cy - 14 * scale, 28 * scale, 28 * scale); e.Graphics.DrawEllipse(p, cx - 14 * scale, cy - 14 * scale, 28 * scale, 28 * scale); }
            using (Font number = new Font("Microsoft YaHei UI", Math.Max(8F, 11F * scale), FontStyle.Bold)) using (SolidBrush b = new SolidBrush(NumberColor)) using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) e.Graphics.DrawString("1", number, b, new RectangleF(cx - 13 * scale, cy - 13 * scale, 26 * scale, 26 * scale), format);
            using (Font note = new Font("Microsoft YaHei UI", Math.Max(8F, 8.5F * scale))) using (SolidBrush text = new SolidBrush(Color.FromArgb(125, 115, 167))) e.Graphics.DrawString("准星编号示例", note, text, 8, Height - 23);
        }
    }

    internal sealed class TargetPoint
    {
        internal int Number;
        internal int X;
        internal int Y;
        internal int DelayMs;
        internal uint DownFlag;
        internal uint UpFlag;
        internal string ButtonName;
        internal MarkerForm Marker;
        public override string ToString() { return Number + "    " + ButtonName + "    (" + X + ", " + Y + ")    间隔 " + DelayMs + " ms"; }
    }

    internal sealed class MarkerForm : Form
    {
        private readonly int number;
        private readonly Point anchor;
        private readonly Color markerColor, numberColor;
        private readonly float scale;
        internal MarkerForm(int markerNumber, Point screenPoint, int markerSize, Color iconColor, Color labelColor)
        {
            number = markerNumber;
            anchor = screenPoint;
            markerColor = iconColor; numberColor = labelColor; scale = markerSize / 50f;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Size = new Size(markerSize, markerSize);
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            TopMost = true;
            CenterOnAnchor();
            DoubleBuffered = true;
        }

        private void CenterOnAnchor() { Location = new Point(anchor.X - Width / 2, anchor.Y - Height / 2); }

        protected override void OnShown(EventArgs e) { base.OnShown(e); CenterOnAnchor(); }

        protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); CenterOnAnchor(); }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020 | 0x00000080 | 0x00080000 | 0x00000008 | 0x08000000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(scale, scale);
            using (Pen pen = new Pen(markerColor, 3.4f))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (Pen innerPen = new Pen(markerColor, 2.8f))
            using (SolidBrush numberBrush = new SolidBrush(numberColor))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                innerPen.StartCap = innerPen.EndCap = LineCap.Round;
                g.DrawEllipse(pen, 8, 8, 34, 34);
                g.DrawEllipse(innerPen, 14, 14, 22, 22);
                g.DrawLine(pen, 25, 1, 25, 7);
                g.DrawLine(pen, 25, 43, 25, 49);
                g.DrawLine(pen, 1, 25, 7, 25);
                g.DrawLine(pen, 43, 25, 49, 25);
                g.FillEllipse(white, 16, 16, 18, 18);
                string label = number.ToString();
                float size = label.Length == 1 ? 12f : label.Length == 2 ? 9f : 7f;
                using (Font font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                    g.DrawString(label, font, numberBrush, new RectangleF(15, 15, 20, 20), format);
            }
        }
    }

    internal sealed class MainForm : ProportionalForm
    {
        private readonly NumericUpDown xInput = new NumericUpDown();
        private readonly NumericUpDown yInput = new NumericUpDown();
        private readonly NumericUpDown intervalInput = new NumericUpDown();
        private readonly NumericUpDown roundsInput = new NumericUpDown();
        private readonly CheckBox recordedTiming = new CheckBox();
        private readonly CheckBox showMarkers = new CheckBox();
        private readonly ListBox pointList = new ListBox();
        private readonly Button recordButton = new Button();
        private readonly Button addButton = new Button();
        private readonly Button removeButton = new Button();
        private readonly Button clearButton = new Button();
        private readonly Button saveSequenceButton = new Button();
        private readonly Button loadSequenceButton = new Button();
        private readonly Button startButton = new Button();
        private readonly Button stopButton = new Button();
        private readonly Label statusLabel = new Label();
        private readonly Label statsLabel = new Label();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Timer clickTimer = new Timer();
        private readonly List<TargetPoint> points = new List<TargetPoint>();
        private readonly Button settingsButton = new Button();
        private ClickerSettings settings;
        private ProportionalResizer resizer;

        private bool running;
        private bool recording;
        private bool markersVisible = true;
        private int currentRound;
        private int currentPoint;
        private int totalClicks;
        private int completedClicks;
        private Point originalCursor;
        private bool hasOriginalCursor;
        private bool[] hotkeysRegistered = new bool[5];
        private bool allHotkeysRegistered;
        private IntPtr mouseHook;
        private NativeMethods.LowLevelMouseProc mouseHookProc;
        private long lastRecordedClick;

        internal MainForm()
        {
            settings = ClickerSettings.Load();
            Text = "轻盒 · Windows 鼠标操作录制器";
            ClientSize = new Size(560, 800);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = Size;
            BackColor = Color.FromArgb(247, 245, 255);
            Font = new Font("Microsoft YaHei UI", 9F);

            AddLabel("鼠标操作录制与连点", 22, 16, 300, 30, 14F, true, Color.FromArgb(38, 39, 59));
            settingsButton.Text = "⚙ 自定义"; settingsButton.Location = new Point(335, 14); settingsButton.Size = new Size(100, 30); Controls.Add(settingsButton);
            AddLabel("开始录制后，依次点击目标位置；每次点击都会生成带编号的准星步骤。", 22, 49, 425, 34, 8.5F, false, Color.FromArgb(125, 127, 145));

            AddLabel("屏幕坐标 X", 24, 88, 170, 20, 9F, true, Color.FromArgb(85, 87, 108));
            ConfigureNumber(xInput, 24, 109, -32768, 32767, 0, 170);
            AddLabel("屏幕坐标 Y", 240, 88, 170, 20, 9F, true, Color.FromArgb(85, 87, 108));
            ConfigureNumber(yInput, 240, 109, -32768, 32767, 0, 170);

            ConfigureButton(recordButton, "开始录制新流程（" + settings.Hotkeys[3].Label + "）", 24, 143, 184, 33);
            ConfigureButton(addButton, "手动添加左键点位（" + settings.Hotkeys[2].Label + "）", 226, 143, 184, 33);

            AddLabel("鼠标操作顺序（编号显示在桌面准星中央）", 24, 186, 290, 20, 8.5F, true, Color.FromArgb(85, 87, 108));
            showMarkers.Text = "显示操作图标（" + settings.Hotkeys[4].Label + "）";
            showMarkers.Checked = settings.MarkersVisible;
            showMarkers.Location = new Point(320, 182);
            showMarkers.Size = new Size(115, 24);
            showMarkers.ForeColor = Color.FromArgb(85, 87, 108);
            Controls.Add(showMarkers);
            pointList.Location = new Point(24, 208);
            pointList.Size = new Size(386, 133);
            pointList.IntegralHeight = false;
            pointList.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(pointList);
            ConfigureButton(removeButton, "删除选中点", 24, 349, 184, 30);
            ConfigureButton(clearButton, "清空全部位置", 226, 349, 184, 30);
            ConfigureButton(saveSequenceButton, "保存流程", 24, 379, 184, 30);
            ConfigureButton(loadSequenceButton, "载入流程", 226, 379, 184, 30);

            AddLabel("固定重放间隔（毫秒）", 24, 393, 190, 20, 9F, true, Color.FromArgb(85, 87, 108));
            ConfigureNumber(intervalInput, 24, 415, 20, 10000, settings.Interval, 170);
            intervalInput.Increment = 10;
            AddLabel("执行轮数（每轮点击每个位置一次）", 226, 393, 235, 20, 8F, true, Color.FromArgb(85, 87, 108));
            ConfigureNumber(roundsInput, 226, 415, 1, 10000, settings.Rounds, 184);

            recordedTiming.Text = "按录制的操作间隔重放";
            recordedTiming.Checked = settings.RecordedTiming;
            recordedTiming.Location = new Point(24, 443);
            recordedTiming.Size = new Size(230, 22);
            recordedTiming.ForeColor = Color.FromArgb(85, 87, 108);
            Controls.Add(recordedTiming);
            intervalInput.Enabled = !recordedTiming.Checked;

            ConfigureButton(startButton, "开始重放（" + settings.Hotkeys[0].Label + "）", 24, 473, 184, 36);
            startButton.BackColor = Color.FromArgb(101, 87, 237);
            startButton.ForeColor = Color.White;
            startButton.FlatStyle = FlatStyle.Flat;
            ConfigureButton(stopButton, "紧急停止（" + settings.Hotkeys[1].Label + "）", 226, 473, 184, 36);
            stopButton.Enabled = false;

            statusLabel = AddLabel("准备就绪。请先录制一组鼠标操作。", 24, 523, 410, 22, 9F, true, Color.FromArgb(92, 83, 205));
            statsLabel = AddLabel("操作：0 · 已完成：0 / 0", 24, 548, 410, 20, 8.5F, false, Color.FromArgb(105, 106, 123));
            progressBar.Location = new Point(24, 572);
            progressBar.Size = new Size(386, 13);
            progressBar.Minimum = 0;
            progressBar.Maximum = 1;
            Controls.Add(progressBar);
            AddLabel("按录制间隔可保留每步等待时间；取消后使用固定重放间隔。", 24, 594, 420, 20, 8F, false, Color.FromArgb(145, 146, 160));
            AddLabel("支持左键/右键/中键；准星穿透，关闭程序后自动移除。", 24, 614, 420, 20, 8F, false, Color.FromArgb(145, 146, 160));

            recordButton.Click += delegate { if (recording) StopRecording(); else StartRecording(); };
            settingsButton.Click += delegate { OpenSettings(); };
            addButton.Click += delegate { AddPoint(new Point(Decimal.ToInt32(xInput.Value), Decimal.ToInt32(yInput.Value)), NativeMethods.LeftDown, NativeMethods.LeftUp, "左键", Decimal.ToInt32(intervalInput.Value)); };
            removeButton.Click += delegate { RemoveSelectedPoint(); };
            clearButton.Click += delegate { ClearPoints(); };
            saveSequenceButton.Click += delegate { SaveSequence(); };
            loadSequenceButton.Click += delegate { LoadSequence(); };
            startButton.Click += delegate { StartClicking(); };
            stopButton.Click += delegate { StopClicking("已停止。"); };
            pointList.SelectedIndexChanged += delegate
            {
                if (pointList.SelectedIndex >= 0 && pointList.SelectedIndex < points.Count)
                {
                    xInput.Value = points[pointList.SelectedIndex].X;
                    yInput.Value = points[pointList.SelectedIndex].Y;
                }
            };
            clickTimer.Interval = Decimal.ToInt32(intervalInput.Value);
            intervalInput.ValueChanged += delegate { if (!running) clickTimer.Interval = Decimal.ToInt32(intervalInput.Value); };
            recordedTiming.CheckedChanged += delegate { if (!running && !recording) intervalInput.Enabled = !recordedTiming.Checked; };
            showMarkers.CheckedChanged += delegate { SetMarkersVisible(showMarkers.Checked); };
            intervalInput.ValueChanged += delegate { if (settings != null) { settings.Interval = Decimal.ToInt32(intervalInput.Value); SaveSettings(); } };
            roundsInput.ValueChanged += delegate { if (settings != null) { settings.Rounds = Decimal.ToInt32(roundsInput.Value); SaveSettings(); } };
            recordedTiming.CheckedChanged += delegate { if (settings != null) { settings.RecordedTiming = recordedTiming.Checked; SaveSettings(); } };
            clickTimer.Tick += OnClickTimer;
            ApplyMainLayout();
            resizer = new ProportionalResizer(this);
            LockAspectRatio();
        }

        private void ApplyMainLayout()
        {
            PictureBox art = new PictureBox { Location = new Point(18, 57), Size = new Size(292, 180), SizeMode = PictureBoxSizeMode.StretchImage, Image = ClickerTheme.ResourceImage("main-mascot.png"), BackColor = Color.FromArgb(248, 246, 255) };
            Controls.Add(art); art.SendToBack();
            AddMainCard(new Rectangle(18, 235, 744, 180));
            AddMainCard(new Rectangle(18, 425, 744, 315));
            AddMainCard(new Rectangle(18, 750, 744, 220));
            AddMainCard(new Rectangle(18, 980, 744, 125));
            foreach (Control control in Controls)
            {
                if (control is Label)
                {
                    Label label = (Label)control; label.ForeColor = Color.FromArgb(72, 62, 143);
                    if (label.Text == "鼠标操作录制与连点") { label.Text = "鼠标操作录制与连点"; label.Location = new Point(325, 112); label.Size = new Size(420, 44); label.Font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold); label.ForeColor = Color.FromArgb(53, 43, 126); }
                    else if (label.Text.StartsWith("开始录制后")) { label.Location = new Point(328, 158); label.Size = new Size(420, 50); label.ForeColor = Color.FromArgb(119, 111, 158); label.Font = new Font("Microsoft YaHei UI", 10F); }
                }
                if (control is NumericUpDown) { control.BackColor = Color.White; control.ForeColor = Color.FromArgb(53, 43, 126); control.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold); }
                if (control is CheckBox) { control.ForeColor = Color.FromArgb(73, 65, 133); control.BackColor = Color.FromArgb(252, 251, 255); control.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold); }
            }
            settingsButton.Text = "⚙  自定义"; settingsButton.Location = new Point(607, 65); settingsButton.Size = new Size(145, 48); StyleButton(settingsButton, false);
            MoveLabel("屏幕坐标 X", 36, 250, 320, 25, 11F, true); MoveLabel("屏幕坐标 Y", 402, 250, 320, 25, 11F, true);
            xInput.Location = new Point(36, 278); xInput.Size = new Size(325, 37); yInput.Location = new Point(402, 278); yInput.Size = new Size(325, 37);
            recordButton.Location = new Point(36, 329); recordButton.Size = new Size(325, 57); StyleButton(recordButton, true); recordButton.Text = "▶  开始录制新流程（" + settings.Hotkeys[3].Label + "）";
            addButton.Location = new Point(402, 329); addButton.Size = new Size(325, 57); StyleButton(addButton, false); addButton.Text = "⊕  手动添加左键点位（" + settings.Hotkeys[2].Label + "）";
            MoveLabel("鼠标操作顺序（编号显示在桌面准星中央）", 36, 438, 470, 28, 12F, true);
            showMarkers.Location = new Point(555, 435); showMarkers.Size = new Size(185, 30);
            pointList.Location = new Point(36, 472); pointList.Size = new Size(691, 150); pointList.BorderStyle = BorderStyle.FixedSingle; pointList.BackColor = Color.White; pointList.ForeColor = Color.FromArgb(61, 54, 111); pointList.Font = new Font("Microsoft YaHei UI", 11F); pointList.ItemHeight = 28;
            saveSequenceButton.Location = new Point(36, 630); saveSequenceButton.Size = new Size(325, 44); StyleButton(saveSequenceButton, true); saveSequenceButton.Text = "保存当前流程";
            loadSequenceButton.Location = new Point(402, 630); loadSequenceButton.Size = new Size(325, 44); StyleButton(loadSequenceButton, false); loadSequenceButton.Text = "载入已保存流程";
            removeButton.Location = new Point(36, 680); removeButton.Size = new Size(325, 45); StyleButton(removeButton, false); removeButton.Text = "▣  删除选中点";
            clearButton.Location = new Point(402, 680); clearButton.Size = new Size(325, 45); StyleButton(clearButton, false); clearButton.Text = "⌫  清空全部位置";
            MoveLabel("固定重放间隔（毫秒）", 36, 765, 320, 25, 11F, true); MoveLabel("执行轮数（每轮点击每个位置一次）", 402, 765, 325, 25, 11F, true);
            intervalInput.Location = new Point(36, 792); intervalInput.Size = new Size(325, 36); roundsInput.Location = new Point(402, 792); roundsInput.Size = new Size(325, 36);
            recordedTiming.Location = new Point(36, 832); recordedTiming.Size = new Size(340, 28);
            startButton.Location = new Point(36, 870); startButton.Size = new Size(325, 62); StyleButton(startButton, true); startButton.Text = "▶  开始重放（" + settings.Hotkeys[0].Label + "）";
            stopButton.Location = new Point(402, 870); stopButton.Size = new Size(325, 62); StyleButton(stopButton, false); stopButton.Text = "■  紧急停止（" + settings.Hotkeys[1].Label + "）";
            MoveLabel("准备就绪。请先录制一组鼠标操作。", 36, 994, 470, 25, 11F, true);
            MoveLabel("操作：0 · 已完成：0 / 0", 535, 994, 192, 25, 10F, false);
            progressBar.Location = new Point(36, 1022); progressBar.Size = new Size(691, 14);
            MoveLabel("按录制间隔可保留每步等待时间；取消后使用固定重放间隔。", 36, 1044, 691, 20, 8.5F, false);
            MoveLabel("支持左键/右键/中键；准星穿透，关闭程序后自动移除。", 36, 1067, 691, 20, 8.5F, false);
            ScaleMainControls(.72F);
        }

        private void AddMainCard(Rectangle bounds)
        {
            Panel card = new SoftCard { Bounds = bounds, Anchor = bounds.Y >= 980 ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom : AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            Controls.Add(card); card.SendToBack();
        }
        private void ScaleMainControls(float scale)
        {
            foreach (Control control in Controls)
            {
                control.Location = new Point((int)Math.Round(control.Left * scale), (int)Math.Round(control.Top * scale));
                control.Size = new Size(Math.Max(1, (int)Math.Round(control.Width * scale)), Math.Max(1, (int)Math.Round(control.Height * scale)));
                control.Font = new Font(control.Font.FontFamily, Math.Max(8.5F, control.Font.Size * scale), control.Font.Style);
            }
            settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; yInput.Anchor = AnchorStyles.Top | AnchorStyles.Right; addButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; showMarkers.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pointList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; clearButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; roundsInput.Anchor = AnchorStyles.Top | AnchorStyles.Right; stopButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; statsLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; progressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            foreach (Control control in Controls) if (control is Label && (((Label)control).Text.StartsWith("按录制间隔") || ((Label)control).Text.StartsWith("支持左键"))) control.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            pointList.ItemHeight = 21;
        }
        private void MoveLabel(string text, int x, int y, int width, int height, float fontSize, bool bold)
        {
            foreach (Control control in Controls) if (control is Label && ((Label)control).Text == text)
            { control.Location = new Point(x, y); control.Size = new Size(width, height); control.Font = new Font("Microsoft YaHei UI", fontSize, bold ? FontStyle.Bold : FontStyle.Regular); return; }
        }
        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = primary ? 0 : 1; button.FlatAppearance.BorderColor = Color.FromArgb(190, 178, 255);
            button.BackColor = primary ? Color.FromArgb(111, 82, 237) : Color.FromArgb(246, 243, 255); button.ForeColor = primary ? Color.White : Color.FromArgb(79, 63, 164);
            button.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold); button.Cursor = Cursors.Hand;
        }

        private void ConfigureNumber(NumericUpDown control, int x, int y, int min, int max, int value, int width)
        {
            control.Location = new Point(x, y); control.Size = new Size(width, 26);
            control.Minimum = min; control.Maximum = max; control.Value = value; Controls.Add(control);
        }

        private void ConfigureButton(Button button, string text, int x, int y, int width, int height)
        {
            button.Text = text; button.Location = new Point(x, y); button.Size = new Size(width, height);
            button.FlatStyle = FlatStyle.System; Controls.Add(button);
        }

        private Label AddLabel(string text, int x, int y, int width, int height, float size, bool bold, Color color)
        {
            Label label = new Label(); label.Text = text; label.Location = new Point(x, y); label.Size = new Size(width, height);
            label.ForeColor = color; label.Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            Controls.Add(label); return label;
        }

        private void SaveSettings() { try { settings.Save(); } catch { if (statusLabel != null) statusLabel.Text = "设置保存失败，请检查用户配置目录权限。"; } }

        private void OpenSettings()
        {
            if (running || recording) { statusLabel.Text = "请先停止录制或重放，再修改设置。"; return; }
            using (SettingsForm dialog = new SettingsForm(settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ClickerSettings previous = settings, updated = dialog.Result;
                UnregisterHotkeys(); settings = updated;
                if (!RegisterHotkeys())
                {
                    UnregisterHotkeys(); settings = previous; RegisterHotkeys();
                    MessageBox.Show(this, "新快捷键有组合已被其他程序占用，设置未更改。请换一组快捷键后重试。", "快捷键不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
                }
                intervalInput.Value = settings.Interval; roundsInput.Value = settings.Rounds;
                recordedTiming.Checked = settings.RecordedTiming; showMarkers.Checked = settings.MarkersVisible; markersVisible = settings.MarkersVisible;
                recordButton.Text = "开始录制新流程（" + settings.Hotkeys[3].Label + "）";
                addButton.Text = "手动添加左键点位（" + settings.Hotkeys[2].Label + "）";
                startButton.Text = "开始重放（" + settings.Hotkeys[0].Label + "）";
                stopButton.Text = "紧急停止（" + settings.Hotkeys[1].Label + "）";
                showMarkers.Text = "显示操作图标（" + settings.Hotkeys[4].Label + "）";
                RefreshMarkersAndList(); SaveSettings(); statusLabel.Text = "自定义设置已保存。";
            }
        }

        private bool RegisterHotkeys()
        {
            bool all = true;
            for (int i = 0; i < 5; i++)
            {
                HotkeyBinding binding = settings.Hotkeys[i];
                hotkeysRegistered[i] = NativeMethods.RegisterHotKey(Handle, i + 1, binding.Modifiers | NativeMethods.NoRepeat, binding.Key);
                all = all && hotkeysRegistered[i];
            }
            allHotkeysRegistered = all; return all;
        }

        private void UnregisterHotkeys()
        {
            for (int i = 0; i < hotkeysRegistered.Length; i++)
                if (hotkeysRegistered[i]) { NativeMethods.UnregisterHotKey(Handle, i + 1); hotkeysRegistered[i] = false; }
            allHotkeysRegistered = false;
        }

        private void AddPoint(Point point, uint downFlag, uint upFlag, string buttonName, int delayMs)
        {
            if (running || (!recording && pointList.Enabled == false)) return;
            if (points.Count >= 100) { statusLabel.Text = "最多记录 100 个鼠标操作，请停止录制并删除部分步骤。"; if (recording) StopRecording(); return; }
            TargetPoint target = new TargetPoint { Number = points.Count + 1, X = point.X, Y = point.Y, DelayMs = delayMs, DownFlag = downFlag, UpFlag = upFlag, ButtonName = buttonName };
            points.Add(target);
            pointList.Items.Add(target);
            target.Marker = new MarkerForm(target.Number, point, settings.MarkerSize, Color.FromArgb(settings.MarkerColor), Color.FromArgb(settings.NumberColor));
            if (markersVisible) target.Marker.Show();
            pointList.SelectedIndex = points.Count - 1;
            statsLabel.Text = "点位：" + points.Count + " · 已完成：0 / 0";
            statusLabel.Text = (recording ? "录制中 · " : "已添加操作 · ") + target.Number + "：" + buttonName + " · " + point.X + ", " + point.Y + " · 间隔 " + delayMs + " ms";
        }

        private void RefreshMarkersAndList()
        {
            pointList.BeginUpdate(); pointList.Items.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                TargetPoint target = points[i]; target.Number = i + 1;
                if (target.Marker != null) { target.Marker.Close(); target.Marker.Dispose(); }
                target.Marker = new MarkerForm(target.Number, new Point(target.X, target.Y), settings.MarkerSize, Color.FromArgb(settings.MarkerColor), Color.FromArgb(settings.NumberColor));
                if (markersVisible) target.Marker.Show();
                pointList.Items.Add(target);
            }
            pointList.EndUpdate();
            if (points.Count > 0) pointList.SelectedIndex = Math.Min(points.Count - 1, 0);
            statsLabel.Text = "点位：" + points.Count + " · 已完成：0 / 0";
        }

        private void StartRecording()
        {
            if (running || recording) return;
            ClearPoints();
            mouseHookProc = MouseHookCallback;
            lastRecordedClick = 0;
            mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.LowLevelMouseHook, mouseHookProc, NativeMethods.GetModuleHandle(null), 0);
            if (mouseHook == IntPtr.Zero)
            {
                statusLabel.Text = "启动全局鼠标记录失败（" + Marshal.GetLastWin32Error() + "）。";
                mouseHookProc = null;
                return;
            }
            recording = true;
            recordButton.Text = "停止录制（" + settings.Hotkeys[3].Label + "）";
            addButton.Enabled = removeButton.Enabled = clearButton.Enabled = saveSequenceButton.Enabled = loadSequenceButton.Enabled = false;
            pointList.Enabled = xInput.Enabled = yInput.Enabled = intervalInput.Enabled = roundsInput.Enabled = recordedTiming.Enabled = false;
            startButton.Enabled = false;
            statusLabel.Text = "录制中：依次点击目标；左键、右键和中键都会记录。";
        }

        private void StopRecording()
        {
            if (!recording) return;
            recording = false;
            if (mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            mouseHookProc = null;
            recordButton.Text = "开始录制新流程（" + settings.Hotkeys[3].Label + "）";
            addButton.Enabled = removeButton.Enabled = clearButton.Enabled = saveSequenceButton.Enabled = loadSequenceButton.Enabled = true;
            pointList.Enabled = xInput.Enabled = yInput.Enabled = roundsInput.Enabled = recordedTiming.Enabled = true;
            intervalInput.Enabled = !recordedTiming.Checked;
            startButton.Enabled = true;
            statusLabel.Text = "录制结束，共记录 " + points.Count + " 个鼠标操作。";
        }

        private bool IsMainWindowAt(Point point)
        {
            IntPtr window = NativeMethods.WindowFromPoint(point);
            return window != IntPtr.Zero && NativeMethods.GetAncestor(window, 2) == Handle;
        }

        private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && recording)
            {
                int message = wParam.ToInt32();
                uint down = 0, up = 0;
                string name = null;
                if (message == 0x0201) { down = NativeMethods.LeftDown; up = NativeMethods.LeftUp; name = "左键"; }
                else if (message == 0x0204) { down = 0x0008; up = 0x0010; name = "右键"; }
                else if (message == 0x0207) { down = 0x0020; up = 0x0040; name = "中键"; }
                if (name != null)
                {
                    NativeMethods.MouseHookData data = (NativeMethods.MouseHookData)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MouseHookData));
                    if (!IsMainWindowAt(data.Point))
                    {
                        long now = Stopwatch.GetTimestamp();
                        int delay = lastRecordedClick == 0 ? 0 : (int)Math.Min(3600000L, (now - lastRecordedClick) * 1000L / Stopwatch.Frequency);
                        lastRecordedClick = now;
                        AddPoint(data.Point, down, up, name, delay);
                    }
                }
            }
            return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        private void RemoveSelectedPoint()
        {
            if (running || recording || pointList.SelectedIndex < 0) return;
            TargetPoint target = points[pointList.SelectedIndex];
            if (target.Marker != null) { target.Marker.Close(); target.Marker.Dispose(); }
            points.RemoveAt(pointList.SelectedIndex); RefreshMarkersAndList();
            statusLabel.Text = "已删除位置并重新编号。";
        }

        private void ClearPoints()
        {
            if (running || recording) return;
            foreach (TargetPoint target in points)
                if (target.Marker != null) { target.Marker.Close(); target.Marker.Dispose(); }
            points.Clear(); pointList.Items.Clear(); progressBar.Value = 0;
            statsLabel.Text = "点位：0 · 已完成：0 / 0";
            statusLabel.Text = "全部位置已清除。";
        }

        private string SequenceDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightboxToolbox", "Recordings"); }
        }

        private void SaveSequence()
        {
            if (running || recording) { statusLabel.Text = "请先停止录制或重放，再保存流程。"; return; }
            if (points.Count == 0) { statusLabel.Text = "当前没有可保存的鼠标操作。"; return; }
            try
            {
                Directory.CreateDirectory(SequenceDirectory);
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存鼠标操作流程"; dialog.InitialDirectory = SequenceDirectory;
                    dialog.Filter = "轻盒操作流程 (*.lbx)|*.lbx"; dialog.DefaultExt = "lbx"; dialog.AddExtension = true;
                    dialog.FileName = "鼠标流程_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".lbx";
                    dialog.OverwritePrompt = true; dialog.RestoreDirectory = true;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    List<string> lines = new List<string>();
                    lines.Add("LIGHTBOX-CLICKER-SEQUENCE|1");
                    lines.Add("name=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFileNameWithoutExtension(dialog.FileName))));
                    foreach (TargetPoint point in points)
                    {
                        lines.Add(String.Join("|", new string[] {
                            point.X.ToString(CultureInfo.InvariantCulture), point.Y.ToString(CultureInfo.InvariantCulture),
                            point.DelayMs.ToString(CultureInfo.InvariantCulture), point.DownFlag.ToString(CultureInfo.InvariantCulture),
                            point.UpFlag.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(Encoding.UTF8.GetBytes(String.IsNullOrEmpty(point.ButtonName) ? "左键" : point.ButtonName))
                        }));
                    }
                    File.WriteAllLines(dialog.FileName, lines.ToArray(), new UTF8Encoding(false));
                    statusLabel.Text = "流程已保存：" + Path.GetFileName(dialog.FileName);
                }
            }
            catch (Exception error) { MessageBox.Show(this, "保存流程失败：" + error.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void LoadSequence()
        {
            if (running || recording) { statusLabel.Text = "请先停止录制或重放，再载入流程。"; return; }
            try
            {
                Directory.CreateDirectory(SequenceDirectory);
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "载入已保存的鼠标流程"; dialog.InitialDirectory = SequenceDirectory;
                    dialog.Filter = "轻盒操作流程 (*.lbx)|*.lbx|所有文件 (*.*)|*.*"; dialog.CheckFileExists = true; dialog.RestoreDirectory = true;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    FileInfo info = new FileInfo(dialog.FileName);
                    if (info.Length > 1024 * 1024) throw new InvalidDataException("流程文件过大，无法载入。");
                    string[] lines = File.ReadAllLines(dialog.FileName, Encoding.UTF8);
                    if (lines.Length < 3 || lines[0] != "LIGHTBOX-CLICKER-SEQUENCE|1" || !lines[1].StartsWith("name=")) throw new InvalidDataException("这不是有效的轻盒流程文件。");
                    string sequenceName = Encoding.UTF8.GetString(Convert.FromBase64String(lines[1].Substring(5)));
                    List<TargetPoint> loaded = new List<TargetPoint>();
                    for (int i = 2; i < lines.Length; i++)
                    {
                        if (String.IsNullOrWhiteSpace(lines[i])) continue;
                        string[] fields = lines[i].Split('|');
                        if (fields.Length != 6 || loaded.Count >= 100) throw new InvalidDataException("流程步骤格式不正确，或步骤超过 100 个。");
                        int x = Int32.Parse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
                        int y = Int32.Parse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
                        int delay = Int32.Parse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
                        uint down = UInt32.Parse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture);
                        uint up = UInt32.Parse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture);
                        string button = Encoding.UTF8.GetString(Convert.FromBase64String(fields[5]));
                        bool validButton = (button == "左键" && down == NativeMethods.LeftDown && up == NativeMethods.LeftUp)
                            || (button == "右键" && down == 0x0008 && up == 0x0010)
                            || (button == "中键" && down == 0x0020 && up == 0x0040);
                        if (!validButton || delay < 0 || delay > 3600000) throw new InvalidDataException("流程包含无效的点击类型或等待时间。");
                        loaded.Add(new TargetPoint { Number = loaded.Count + 1, X = x, Y = y, DelayMs = delay, DownFlag = down, UpFlag = up, ButtonName = button });
                    }
                    if (loaded.Count == 0) throw new InvalidDataException("这个流程文件没有操作步骤。");
                    if (points.Count > 0 && MessageBox.Show(this, "载入将替换当前录制的操作，是否继续？", "替换当前流程", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    foreach (TargetPoint old in points) if (old.Marker != null) { old.Marker.Close(); old.Marker.Dispose(); }
                    points.Clear(); points.AddRange(loaded); progressBar.Value = 0; RefreshMarkersAndList();
                    statusLabel.Text = "已载入“" + sequenceName + "”，共 " + points.Count + " 个操作。";
                }
            }
            catch (Exception error) { MessageBox.Show(this, "载入流程失败：" + error.Message, "载入失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SetMarkersVisible(bool visible)
        {
            markersVisible = visible;
            if (settings != null) { settings.MarkersVisible = visible; SaveSettings(); }
            foreach (TargetPoint target in points)
            {
                if (target.Marker == null) continue;
                if (visible) target.Marker.Show(); else target.Marker.Hide();
            }
            statusLabel.Text = visible ? "操作图标已显示。" : "操作图标已隐藏；录制序列仍然保留。";
        }

        private void StartClicking()
        {
            if (running) return;
            if (points.Count == 0) { statusLabel.Text = "请先记录或添加至少一个位置。"; return; }
            int rounds = Decimal.ToInt32(roundsInput.Value);
            totalClicks = checked(rounds * points.Count);
            completedClicks = currentRound = currentPoint = 0;
            progressBar.Maximum = Math.Max(1, totalClicks); progressBar.Value = 0;
            clickTimer.Interval = GetReplayDelay(points[0]);
            originalCursor = Cursor.Position; hasOriginalCursor = true; running = true;
            recordButton.Enabled = addButton.Enabled = removeButton.Enabled = clearButton.Enabled = saveSequenceButton.Enabled = loadSequenceButton.Enabled = false;
            startButton.Enabled = false; stopButton.Enabled = true;
            intervalInput.Enabled = roundsInput.Enabled = xInput.Enabled = yInput.Enabled = pointList.Enabled = recordedTiming.Enabled = false;
            UpdateProgressText(); clickTimer.Start();
        }

        private void StopClicking(string message)
        {
            if (!running) return;
            running = false; clickTimer.Stop();
            if (hasOriginalCursor) { NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y); hasOriginalCursor = false; }
            recordButton.Enabled = addButton.Enabled = removeButton.Enabled = clearButton.Enabled = saveSequenceButton.Enabled = loadSequenceButton.Enabled = true;
            startButton.Enabled = true; stopButton.Enabled = false;
            intervalInput.Enabled = !recordedTiming.Checked;
            roundsInput.Enabled = xInput.Enabled = yInput.Enabled = pointList.Enabled = recordedTiming.Enabled = true;
            statusLabel.Text = message; UpdateProgressText();
        }

        private void UpdateProgressText()
        {
            statsLabel.Text = "点位：" + points.Count + " · 已完成：" + completedClicks + " / " + totalClicks;
            if (running && points.Count > 0)
                statusLabel.Text = "第 " + (currentRound + 1) + " / " + Decimal.ToInt32(roundsInput.Value) + " 轮 · 点位 " + (currentPoint + 1) + " / " + points.Count;
        }

        private int GetReplayDelay(TargetPoint point)
        {
            int delay = recordedTiming.Checked ? point.DelayMs : Decimal.ToInt32(intervalInput.Value);
            return Math.Max(1, Math.Min(3600000, delay));
        }

        private void OnClickTimer(object sender, EventArgs e)
        {
            if (!running) return;
            TargetPoint target = points[currentPoint];
            if (!NativeMethods.SetCursorPos(target.X, target.Y)) { StopClicking("移动鼠标失败，已停止。"); return; }
            NativeMethods.mouse_event(target.DownFlag, 0, 0, 0, UIntPtr.Zero);
            NativeMethods.mouse_event(target.UpFlag, 0, 0, 0, UIntPtr.Zero);
            completedClicks++; currentPoint++;
            if (currentPoint >= points.Count) { currentPoint = 0; currentRound++; }
            progressBar.Value = Math.Min(progressBar.Maximum, completedClicks);
            if (completedClicks >= totalClicks) StopClicking("任务完成。");
            else { clickTimer.Interval = GetReplayDelay(points[currentPoint]); UpdateProgressText(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkeys();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!allHotkeysRegistered) statusLabel.Text = "部分快捷键被占用；请使用程序内按钮或关闭占用程序后重试。";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotkeys();
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.HotkeyMessage)
            {
                switch (m.WParam.ToInt32())
                {
                    case 1: if (running) StopClicking("已停止。"); else StartClicking(); break;
                    case 2: StopClicking("紧急停止。"); break;
                    case 3: if (!running && !recording) AddPoint(Cursor.Position, NativeMethods.LeftDown, NativeMethods.LeftUp, "左键", Decimal.ToInt32(intervalInput.Value)); break;
                    case 4: if (recording) StopRecording(); else StartRecording(); break;
                    case 5: showMarkers.Checked = !showMarkers.Checked; break;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopClicking("窗口关闭，已停止。");
            StopRecording();
            foreach (TargetPoint target in points)
                if (target.Marker != null) { target.Marker.Close(); target.Marker.Dispose(); }
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                if (!NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4))) NativeMethods.SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException) { NativeMethods.SetProcessDPIAware(); }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
