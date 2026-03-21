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

        private enum ColorScheme { Complementary, Analogous, Triadic, SplitComplementary, Tetradic, Compliant508 }
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

        private Panel contrastSwatch1 = null!;
        private Panel contrastSwatch2 = null!;
        private Label contrastLabel1 = null!;
        private Label contrastLabel2 = null!;
        private Label contrastRatioLabel = null!;
        private Button swapContrastBtn = null!;
        private Dictionary<PictureBox, Color> paletteSwatchColors = new Dictionary<PictureBox, Color>();

        public MainForm()
        {
            BuildUi();
            SetupTimer();
        }

        private void BuildUi()
        {
            Text = "ColorID";
            TopMost = true;
            // Allow the user to resize the window; keep the picker on top but not locked.
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = true;
            MinimumSize = new Size(480, 220);
            ClientSize = new Size(480, 220);

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
                paletteSwatch[i].Click += (s, e) =>
                {
                    var pb = (PictureBox)s!;
                    if (!paletteSwatchColors.TryGetValue(pb, out var originalColor))
                        originalColor = pb.BackColor;
                    CopyColor(originalColor, pb);
                    SetContrastColor(contrastSwatch2, contrastLabel2, originalColor);
                };
                toolTip.SetToolTip(paletteSwatch[i], "Click to copy hex color");
                Controls.Add(paletteSwatch[i]);

                paletteInfoLabel[i] = new Label { Location = new Point(xPos, 355), Size = new Size(100, 60), AutoSize = false, Text = "#000000\nrgb(0,0,0)\nUnknown", Tag = "paletteUI", TextAlign = ContentAlignment.TopCenter, Font = new Font(Font.FontFamily, 8) };
                Controls.Add(paletteInfoLabel[i]);
            }

            AddContrastControls();

            // update palette based on default scheme/color and then reposition
            UpdatePaletteUI();
            RepositionPaletteControls();

            // start hidden; user must click Show Palette to reveal
            foreach (Control c in Controls)
                if ((string?)c.Tag == "paletteUI")
                    c.Visible = false;
            paletteVisible = false;

            // layout everything now that the controls exist
            LayoutControls();

            KeyPreview = true;
            KeyDown += MainForm_KeyDown;

            // Keep the UI responsive when the user resizes the window
            Resize += (s, e) => LayoutControls();
        }

        private void AddContrastControls()
        {
            // Contrast Swatch 1
            contrastSwatch1 = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(50, 50),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Black, // or swatch.BackColor if swatch is ready
                Cursor = Cursors.Hand,
                Tag = "paletteUI"
            };
            contrastSwatch1.Click += (s, e) => CopyColor(contrastSwatch1.BackColor, contrastSwatch1);
            Controls.Add(contrastSwatch1);

            // Contrast Label 1
            contrastLabel1 = new Label
            {
                Location = new Point(0, 0),
                AutoSize = true,
                Text = $"#{contrastSwatch1.BackColor.R:X2}{contrastSwatch1.BackColor.G:X2}{contrastSwatch1.BackColor.B:X2}\nrgb({contrastSwatch1.BackColor.R},{contrastSwatch1.BackColor.G},{contrastSwatch1.BackColor.B})",
                Tag = "paletteUI"
            };
            Controls.Add(contrastLabel1);

            // Contrast Swatch 2
            contrastSwatch2 = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(50, 50),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                Tag = "paletteUI"
            };
            contrastSwatch2.Click += (s, e) => CopyColor(contrastSwatch2.BackColor, contrastSwatch2);
            Controls.Add(contrastSwatch2);

            // Contrast Label 2
            contrastLabel2 = new Label
            {
                Location = new Point(0, 0),
                AutoSize = true,
                Text = $"#{contrastSwatch2.BackColor.R:X2}{contrastSwatch2.BackColor.G:X2}{contrastSwatch2.BackColor.B:X2}\nrgb({contrastSwatch2.BackColor.R},{contrastSwatch2.BackColor.G},{contrastSwatch2.BackColor.B})",
                Tag = "paletteUI"
            };
            Controls.Add(contrastLabel2);

            // Contrast Ratio Label
            contrastRatioLabel = new Label
            {
                Location = new Point(0, 0),
                AutoSize = true,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
                Text = "Contrast Ratio: N/A",
                Tag = "paletteUI"
            };
            Controls.Add(contrastRatioLabel);

            // Swap Button
            swapContrastBtn = new Button
            {
                Location = new Point(0, 0),
                Size = new Size(60, 50),
                Text = "Swap",
                Tag = "paletteUI"
            };
            swapContrastBtn.Click += (s, e) =>
            {
                var temp = contrastSwatch1.BackColor;
                SetContrastColor(contrastSwatch1, contrastLabel1, contrastSwatch2.BackColor);
                SetContrastColor(contrastSwatch2, contrastLabel2, temp);
                UpdatePaletteUI();

            };
            Controls.Add(swapContrastBtn);
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

            // Update first contrast swatch to current picked color
            SetContrastColor(contrastSwatch1, contrastLabel1, c);

            // Update palette colors
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
                UpdatePaletteUI();
            }

            foreach (Control c in Controls)
            {
                if ((string?)c.Tag == "paletteUI")
                {
                    c.Visible = paletteVisible;
                }
            }
        
            paletteToggleBtn.Text = paletteVisible ? "Hide Palette" : "Show Palette";
            int width = Math.Max(ClientSize.Width, MinimumSize.Width);
            int height = paletteVisible ? 520 : 220; // increase height to fit palette and contrast controls
            ClientSize = new Size(width, height);
            LayoutControls();
            UpdatePaletteUI();
        }

        /// <summary>
        /// Adjusts the location of scheme controls and palette swatches so that
        /// they are centered within the current client area. This should be
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
            RepositionButtons();
            
                // Position contrast controls centered below palette swatches
                if (paletteVisible)
                {
                    // Calculate horizontal center for contrast controls
                    int totalContrastWidth = contrastSwatch1.Width + 8 + contrastLabel1.Width + 8 + swapContrastBtn.Width + 8 + contrastSwatch2.Width + 8 + contrastLabel2.Width;
                    int startX = (ClientSize.Width - totalContrastWidth) / 2;
                    int contrastY = 430; // position below palette swatches (palette swatches are at y=270-355)

                    // Position contrastSwatch1 and label1
                    contrastSwatch1.Location = new Point(startX, contrastY);
                    contrastLabel1.Location = new Point(contrastSwatch1.Right + 8, contrastY + 10);

                    // Position swap button
                    swapContrastBtn.Location = new Point(contrastLabel1.Right + 8, contrastY);

                    // Position contrastSwatch2 and label2
                    contrastSwatch2.Location = new Point(swapContrastBtn.Right + 8, contrastY);
                    contrastLabel2.Location = new Point(contrastSwatch2.Right + 8, contrastY + 10);

                    // Position contrast ratio label below the swatches and button
                    contrastRatioLabel.Location = new Point(startX, contrastY + contrastSwatch1.Height + 10);
                }
        }

        /// <summary>
        /// Center the action buttons horizontally within the client area.
        /// </summary>
        private void RepositionButtons()
        {
            int btnWidth = 160;
            int startX = (ClientSize.Width - btnWidth) / 2;
            if (toggleBtn != null) toggleBtn.Location = new Point(startX, 136);
            if (paletteToggleBtn != null) paletteToggleBtn.Location = new Point(startX, 168);
        }

        /// <summary>
        /// Layouts the fixed elements (swatch, zoom box, and labels) and then
        /// re-centers the buttons and palette controls. This is called when the
        /// window is resized or when the palette visibility changes.
        /// </summary>
        private void LayoutControls()
        {
            const int margin = 12;

            // Keep swatch in the top-left corner
            swatch.Location = new Point(margin, margin);

            // Keep zoom box in the top-right corner
            zoomBox.Location = new Point(ClientSize.Width - margin - zoomBox.Width, margin);

            // Labels sit to the right of the swatch, but should not overlap the zoom box
            int labelX = swatch.Right + margin;
            int maxLabelWidth = Math.Max(0, zoomBox.Left - margin - labelX);
            if (maxLabelWidth > 0)
            {
                hexLabel.MaximumSize = new Size(maxLabelWidth, 0);
                rgbLabel.MaximumSize = new Size(maxLabelWidth, 0);
                nameLabel.MaximumSize = new Size(maxLabelWidth, 0);
            }

            hexLabel.Location = new Point(labelX, margin + 4);
            rgbLabel.Location = new Point(labelX, hexLabel.Bottom + 6);
            nameLabel.Location = new Point(labelX, rgbLabel.Bottom + 6);

/*             // Position contrast controls near bottom, but inside client area
            int contrastY = ClientSize.Height - 100; // 100 px from bottom
            contrastSwatch1.Location = new Point(12, contrastY);
            contrastLabel1.Location = new Point(contrastSwatch1.Right + 8, contrastY + 10);
            swapContrastBtn.Location = new Point(contrastLabel1.Right + 8, contrastY);
            contrastSwatch2.Location = new Point(swapContrastBtn.Right + 8, contrastY);
            contrastLabel2.Location = new Point(contrastSwatch2.Right + 8, contrastY + 10);
            contrastRatioLabel.Location = new Point(12, contrastY + 60); */

            // Re-center the buttons and palette controls for the current size
            RepositionButtons();
            RepositionPaletteControls();
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
                case ColorScheme.Compliant508:
                    colors = new List<Color>(Get508CompliantColors(baseColor));
                    break;
            }

            return colors.ToArray();
        }

        private static double GetRelativeLuminance(Color c)
        {
            double RsRGB = c.R / 255.0;
            double GsRGB = c.G / 255.0;
            double BsRGB = c.B / 255.0;

            double R = RsRGB <= 0.03928 ? RsRGB / 12.92 : Math.Pow((RsRGB + 0.055) / 1.055, 2.4);
            double G = GsRGB <= 0.03928 ? GsRGB / 12.92 : Math.Pow((GsRGB + 0.055) / 1.055, 2.4);
            double B = BsRGB <= 0.03928 ? BsRGB / 12.92 : Math.Pow((BsRGB + 0.055) / 1.055, 2.4);

            return 0.2126 * R + 0.7152 * G + 0.0722 * B;
        }

        private static double GetContrastRatio(Color c1, Color c2)
        {
            double L1 = GetRelativeLuminance(c1);
            double L2 = GetRelativeLuminance(c2);

            double lighter = Math.Max(L1, L2);
            double darker = Math.Min(L1, L2);

            return (lighter + 0.05) / (darker + 0.05);
        }

        private Color[] Get508CompliantColors(Color baseColor)
        {
            const double minContrast = 4.5;
            var colors = new List<Color> { baseColor };
            HSV baseHsv = RgbToHsv(baseColor);

            // Try darker variants by decreasing brightness
            for (double v = baseHsv.V - 0.2; v >= 0 && colors.Count < 4; v -= 0.1)
            {
                var c = HsvToRgb(new HSV { H = baseHsv.H, S = baseHsv.S, V = v });
                if (GetContrastRatio(baseColor, c) >= minContrast)
                    colors.Add(c);
            }

            // Try lighter variants by increasing brightness
            for (double v = baseHsv.V + 0.2; v <= 1 && colors.Count < 4; v += 0.1)
            {
                var c = HsvToRgb(new HSV { H = baseHsv.H, S = baseHsv.S, V = v });
                if (GetContrastRatio(baseColor, c) >= minContrast)
                    colors.Add(c);
            }

            // If still less than 4 colors, add black or white fallback colors
            if (colors.Count < 4)
            {
                if (GetContrastRatio(baseColor, Color.Black) >= minContrast && !colors.Contains(Color.Black))
                    colors.Add(Color.Black);
                if (colors.Count < 4 && GetContrastRatio(baseColor, Color.White) >= minContrast && !colors.Contains(Color.White))
                    colors.Add(Color.White);
            }

            // Ensure exactly 4 colors
            while (colors.Count < 4)
                colors.Add(baseColor);

            return colors.Take(4).ToArray();
        }

        private void UpdateContrastRatio()
        {
            Color c1 = contrastSwatch1.BackColor;
            Color c2 = contrastSwatch2.BackColor;
            double ratio = GetContrastRatio(c1, c2);
            string passFail = ratio >= 4.5 ? "Passes 508 compliance" : "Fails 508 compliance";
            contrastRatioLabel.Text = $"Contrast Ratio: {ratio:F2} — {passFail}";
        }

        private void SetContrastColor(Panel swatch, Label label, Color color)
        {
            swatch.BackColor = color;
            label.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}\nrgb({color.R},{color.G},{color.B})";
            UpdateContrastRatio();
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
                    paletteSwatchColors[paletteSwatch[i]] = c;
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

            // Update contrast ratio display
            UpdateContrastRatio();
        }
    }
}
