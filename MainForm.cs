using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ColorID
{
    public partial class MainForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private struct HSV { public double H, S, V; }

        private enum ColorScheme { Complementary, Analogous, Triadic, SplitComplementary, Tetradic }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        private System.Windows.Forms.Timer pollTimer = null!;
        private Panel swatch = null!;
        private Label hexLabel = null!;
        private Label rgbLabel = null!;
        private Label nameLabel = null!;
        private PictureBox zoomBox = null!;
        private Button toggleBtn = null!;
        private ToolTip toolTip = null!;

        private const int SampleSize = 21; // square sample (odd preferred)
        private const int ZoomFactor = 6; // scaling multiplier
        private bool picking = false;
        private bool paletteVisible = false;
        private ComboBox schemeCombo = null!;
        private PictureBox[] paletteSwatch = null!;
        private Label[] paletteInfoLabel = null!;
        private Button paletteToggleBtn = null!;

        public MainForm()
        {
            BuildUi();
            SetupTimer();
        }

        private void BuildUi()
        {
            Text = "ColorID";
            TopMost = true;
            FormBorderStyle = FormBorderStyle.FixedDialog; // still fixed size
            MaximizeBox = false;
            MinimizeBox = true; // user requested ability to minimize the app
            ClientSize = new Size(520, 220);

            swatch = new Panel { Location = new Point(12, 12), Size = new Size(100, 100), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black, Cursor = Cursors.Hand };
            swatch.Click += (s, e) => CopyColor(swatch.BackColor, (Control)s!);
            Controls.Add(swatch);

            zoomBox = new PictureBox { Location = new Point(380, 12), Size = new Size(SampleSize * ZoomFactor, SampleSize * ZoomFactor), BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(zoomBox);

            toolTip = new ToolTip();
            toolTip.SetToolTip(swatch, "Click to copy hex color");
            toolTip.SetToolTip(zoomBox, "Zoomed view of sampled area");

            hexLabel = new Label { Location = new Point(124, 16), AutoSize = true, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), Text = "#000000" };
            Controls.Add(hexLabel);

            rgbLabel = new Label { Location = new Point(124, 48), AutoSize = true, Text = "rgb(0, 0, 0) @ 0,0" };
            Controls.Add(rgbLabel);

            nameLabel = new Label { Location = new Point(124, 80), AutoSize = true, Text = "Unknown" };
            Controls.Add(nameLabel);

            toggleBtn = new Button { Size = new Size(160, 28), Text = "Start Picking (Space)" };
            toggleBtn.Click += (s, e) => TogglePicking();
            Controls.Add(toggleBtn);

            paletteToggleBtn = new Button { Size = new Size(160, 28), Text = "Show Palette" };
            paletteToggleBtn.Click += (s, e) => TogglePaletteVisibility();
            Controls.Add(paletteToggleBtn);

            Label schemeLabel = new Label { AutoSize = true, Text = "Scheme:" };
            schemeLabel.Tag = "paletteUI";
            Controls.Add(schemeLabel);

            schemeCombo = new ComboBox { Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList, Tag = "paletteUI" };
            schemeCombo.Items.AddRange(Enum.GetNames(typeof(ColorScheme)));
            schemeCombo.SelectedIndex = 0;
            schemeCombo.SelectedIndexChanged += (s, e) => UpdatePaletteUI();
            Controls.Add(schemeCombo);

            // centering will be handled once all palette controls are created

            paletteInfoLabel = new Label[4];
            paletteSwatch = new PictureBox[4];
            for (int i = 0; i < 4; i++)
            {
                int xPos = 12 + i * 120;
                paletteSwatch[i] = new PictureBox { Location = new Point(xPos, 270), Size = new Size(100, 80), BorderStyle = BorderStyle.FixedSingle, Tag = "paletteUI", Cursor = Cursors.Hand };
                paletteSwatch[i].Click += (s, e) => CopyColor(((PictureBox)s!).BackColor, (Control)s!);
                toolTip.SetToolTip(paletteSwatch[i], "Click to copy hex color");
                Controls.Add(paletteSwatch[i]);

                paletteInfoLabel[i] = new Label { Location = new Point(xPos, 355), Size = new Size(100, 60), AutoSize = false, Text = "#000000\nrgb(0,0,0)\nUnknown", Tag = "paletteUI", TextAlign = ContentAlignment.TopCenter, Font = new Font(Font.FontFamily, 8) };
                Controls.Add(paletteInfoLabel[i]);
            }

            // update palette based on default scheme/color and then reposition
            UpdatePaletteUI();
            RepositionPaletteControls();

            // start hidden; user must click Show Palette to reveal
            foreach (Control c in Controls)
                if ((string?)c.Tag == "paletteUI")
                    c.Visible = false;
            paletteVisible = false;

            // center buttons now that they exist
            RepositionButtons();

            KeyPreview = true;
            KeyDown += MainForm_KeyDown;
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { TogglePicking(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { Close(); }
        }

        private void SetupTimer()
        {
            pollTimer = new System.Windows.Forms.Timer { Interval = 80 };
            pollTimer.Tick += PollTimer_Tick;
            pollTimer.Start();
        }

        private void PollTimer_Tick(object? sender, EventArgs e)
        {
            if (!picking) return;
            if (GetCursorPos(out POINT pt))
            {
                var color = GetColorAt(pt.X, pt.Y);
                UpdateUi(color, pt.X, pt.Y);
                UpdateZoomAt(pt.X, pt.Y);
            }
        }

        private void UpdateZoomAt(int x, int y)
        {
            int half = SampleSize / 2;
            Rectangle src = new Rectangle(x - half, y - half, SampleSize, SampleSize);
            try
            {
                using var bmp = new Bitmap(SampleSize, SampleSize, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(src.Location, Point.Empty, src.Size, CopyPixelOperation.SourceCopy);
                }

                int dstSize = SampleSize * ZoomFactor;
                var scaled = new Bitmap(dstSize, dstSize, PixelFormat.Format24bppRgb);
                using (var g2 = Graphics.FromImage(scaled))
                {
                    g2.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g2.PixelOffsetMode = PixelOffsetMode.Half;
                    g2.DrawImage(bmp, new Rectangle(0, 0, dstSize, dstSize), new Rectangle(0, 0, SampleSize, SampleSize), GraphicsUnit.Pixel);

                    // draw crosshair
                    using var pen = new Pen(Color.FromArgb(200, Color.White), 1);
                    int cx = dstSize / 2;
                    int cy = dstSize / 2;
                    g2.DrawLine(pen, cx - ZoomFactor, cy, cx + ZoomFactor, cy);
                    g2.DrawLine(pen, cx, cy - ZoomFactor, cx, cy + ZoomFactor);
                    using var pen2 = new Pen(Color.FromArgb(200, Color.Black), 1);
                    g2.DrawRectangle(pen2, cx - ZoomFactor / 2, cy - ZoomFactor / 2, ZoomFactor, ZoomFactor);
                }

                var old = zoomBox.Image;
                zoomBox.Image = scaled;
                old?.Dispose();
            }
            catch
            {
                // ignore transient screen-capture errors
            }
        }

        private Color GetColorAt(int x, int y)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            try
            {
                uint pixel = GetPixel(hdc, x, y);
                int r = (int)(pixel & 0x000000FF);
                int g = (int)((pixel & 0x0000FF00) >> 8);
                int b = (int)((pixel & 0x00FF0000) >> 16);
                return Color.FromArgb(r, g, b);
            }
            finally { ReleaseDC(IntPtr.Zero, hdc); }
        }

        private static readonly (string Name, Color Color)[] NamedColors = CreateNamedColors();

        private static (string, Color)[] CreateNamedColors()
        {
            var list = new List<(string, Color)>();
            foreach (KnownColor kc in Enum.GetValues(typeof(KnownColor)))
            {
                Color c = Color.FromKnownColor(kc);
                if (!c.IsSystemColor)
                {
                    list.Add((kc.ToString(), c));
                }
            }
            return list.ToArray();
        }

        private string GetNearestColorName(Color c)
        {
            double bestDist = double.MaxValue;
            string bestName = "Unknown";
            foreach (var entry in NamedColors)
            {
                int dr = c.R - entry.Color.R;
                int dg = c.G - entry.Color.G;
                int db = c.B - entry.Color.B;
                double dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestName = entry.Name;
                }
            }
            return bestName;
        }

        private void UpdateUi(Color c, int x, int y)
        {
            swatch.BackColor = c;
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            hexLabel.Text = hex;
            rgbLabel.Text = $"rgb({c.R}, {c.G}, {c.B}) @ {x},{y}";
            nameLabel.Text = GetNearestColorName(c);
            UpdatePaletteUI();
        }

        private void TogglePicking()
        {
            picking = !picking;
            toggleBtn.Text = picking ? "Stop Picking (Space)" : "Start Picking (Space)";
        }

        private void TogglePaletteVisibility()
        {
            paletteVisible = !paletteVisible;
            if (paletteVisible)
            {
                // ensure swatch visibility matches the current scheme before showing
                UpdatePaletteUI();
            }

            foreach (Control c in Controls)
            {
                if ((string?)c.Tag == "paletteUI")
                {
                    if (paletteVisible)
                    {
                        bool isScheme = (c == schemeCombo) || (c is Label l && l.Text == "Scheme:");
                        c.Visible = isScheme || c.Visible;
                    }
                    else
                    {
                        c.Visible = false;
                    }
                }
            }
        
            paletteToggleBtn.Text = paletteVisible ? "Hide Palette" : "Show Palette";
            ClientSize = paletteVisible ? new Size(520, 430) : new Size(520, 220);
            RepositionPaletteControls(); // recenter when toggling
            RepositionButtons();
        }

        /// <summary>
        /// Adjusts the location of scheme controls and palette swatches so that
        /// they are centered within the current client area.  This should be
        /// called whenever the form is resized, palette visibility changes, or
        /// the number of active palette entries is updated.
        /// </summary>
        private void RepositionPaletteControls()
        {
            // position scheme label/combo on same line, centered horizontally
            // compute actual width of scheme label after AutoSize
            if (schemeCombo != null && schemeCombo.Parent != null)
            {
                var schemeLabel = Controls.OfType<Label>().FirstOrDefault(l => l.Text == "Scheme:");
                if (schemeLabel != null)
                {
                    int gap = 8;
                    int totalWidth = schemeLabel.Width + gap + schemeCombo.Width;
                    int startX = (ClientSize.Width - totalWidth) / 2;
                    int y = 220;
                    schemeLabel.Location = new Point(startX, y);
                    schemeCombo.Location = new Point(startX + schemeLabel.Width + gap, y);
                }
            }

            // reposition palette swatches that are visible, centering them horizontally
            if (paletteSwatch != null)
            {
                var visibleSwatches = paletteSwatch.Where(pb => pb.Visible).ToArray();
                int count = visibleSwatches.Length;
                if (count > 0)
                {
                    int swatchWidth = 100;
                    int spacing = 20; // distance between swatches
                    int totalWidth = count * swatchWidth + (count - 1) * spacing;
                    int startX = (ClientSize.Width - totalWidth) / 2;
                    int swatchY = 270;
                    int infoY = 355;
                    for (int i = 0, x = startX; i < paletteSwatch.Length; i++)
                    {
                        if (!paletteSwatch[i].Visible)
                            continue;
                        paletteSwatch[i].Location = new Point(x, swatchY);
                        paletteInfoLabel[i].Location = new Point(x, infoY);
                        x += swatchWidth + spacing;
                    }
                }
            }
            // keep buttons centered too
            RepositionButtons();
        }

        /// <summary>
        /// Center the action buttons horizontally within the client area.
        /// </summary>
        private void RepositionButtons()
        {
            int btnWidth = 160;
            int startX = (ClientSize.Width - btnWidth) / 2;
            if (toggleBtn != null) toggleBtn.Location = new Point(startX, 112);
            if (paletteToggleBtn != null) paletteToggleBtn.Location = new Point(startX, 144);
        }

        private void CopyColor(Color color, Control control)
        {
            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            Clipboard.SetText(hex);
            FlashControl(control);
        }

        private async void FlashControl(Control control)
        {
            var originalColor = control.BackColor;
            var flashColor = Color.FromArgb(
                Math.Min(255, originalColor.R + 50),
                Math.Min(255, originalColor.G + 50),
                Math.Min(255, originalColor.B + 50)
            );
            control.BackColor = flashColor;
            await Task.Delay(150);
            control.BackColor = originalColor;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            pollTimer?.Stop();
            base.OnFormClosing(e);
        }

        private HSV RgbToHsv(Color c)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;
            if (delta != 0)
            {
                if (max == r) h = 60 * (((g - b) / delta) % 6);
                else if (max == g) h = 60 * (((b - r) / delta) + 2);
                else h = 60 * (((r - g) / delta) + 4);
                if (h < 0) h += 360;
            }

            double s = max == 0 ? 0 : delta / max;
            double v = max;
            return new HSV { H = h, S = s, V = v };
        }

        private Color HsvToRgb(HSV hsv)
        {
            double h = hsv.H % 360;
            double s = Math.Clamp(hsv.S, 0, 1);
            double v = Math.Clamp(hsv.V, 0, 1);

            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;

            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        private Color[] GetComplementaryColors(Color baseColor)
        {
            ColorScheme scheme = (ColorScheme)schemeCombo.SelectedIndex;
            HSV hsv = RgbToHsv(baseColor);
            var colors = new List<Color> { baseColor };

            switch (scheme)
            {
                case ColorScheme.Complementary:
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H + 180) % 360, S = hsv.S, V = hsv.V }));
                    break;
                case ColorScheme.Analogous:
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H + 30) % 360, S = hsv.S, V = hsv.V }));
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H - 30 + 360) % 360, S = hsv.S, V = hsv.V }));
                    break;
                case ColorScheme.Triadic:
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H + 120) % 360, S = hsv.S, V = hsv.V }));
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H + 240) % 360, S = hsv.S, V = hsv.V }));
                    break;
                case ColorScheme.SplitComplementary:
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H + 150) % 360, S = hsv.S, V = hsv.V }));
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H + 210) % 360, S = hsv.S, V = hsv.V }));
                    break;
                case ColorScheme.Tetradic:
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H + 90) % 360, S = hsv.S, V = hsv.V }));
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H + 180) % 360, S = hsv.S, V = hsv.V }));
                    colors.Add(HsvToRgb(new HSV { H = (hsv.H + 270) % 360, S = hsv.S, V = hsv.V }));
                    break;
            }

            return colors.ToArray();
        }

        private void UpdatePaletteUI()
        {
            Color baseColor = swatch.BackColor;
            Color[] palette = GetComplementaryColors(baseColor);
            for (int i = 0; i < paletteSwatch.Length; i++)
            {
                if (i < palette.Length)
                {
                    Color c = palette[i];
                    paletteSwatch[i].BackColor = c;
                    string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                    string rgb = $"rgb({c.R},{c.G},{c.B})";
                    string name = GetNearestColorName(c);
                    paletteInfoLabel[i].Text = $"{hex}\n{rgb}\n{name}";
                    paletteSwatch[i].Visible = true;
                    paletteInfoLabel[i].Visible = true;
                }
                else
                {
                    paletteSwatch[i].Visible = false;
                    paletteInfoLabel[i].Visible = false;
                }
            }
            RepositionPaletteControls();
        }
    }
}
