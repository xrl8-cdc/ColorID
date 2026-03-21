# ColorID

A tiny Windows color picker built with .NET 10 WinForms. Shows the color under the cursor, its HEX and RGB values, lets you copy the HEX code, and toggle picking with Space.

Requirements
- .NET 10 SDK (target: net10.0-windows)

Build & Run

**Debug (with .NET 10 SDK):**
```bash
dotnet build
dotnet run --project ColorID.csproj
```

**Publish as Standalone EXE (no .NET required):**
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable will be at:
```
bin\Release\net10.0-windows\win-x64\publish\ColorID.exe
```

Usage
- Press Space to start/stop picking.
- Move your cursor over colors to see the zoomed magnifier view.
- Click "Show Palette" to display the complementary color palettes (window resizes automatically).
- Select a color scheme from the dropdown: **Complementary**, **Analogous**, **Triadic**, **Split-Complementary**, **Tetradic**, or **508 Compliant**.
- Click any palette color swatch to copy its HEX value.
- Each palette swatch displays the HEX, RGB, and English name below it.
- Click "Copy Hex" to copy the main color's hex value to the clipboard.
- Press Esc to quit.

Features
- **Color Swatch**: Live preview of the selected color.
- **HEX & RGB**: Color values in both formats.
- **English Name**: Nearest matching English color name from 100+ named colors.
- **Magnified View**: 6× zoomed view with crosshair to pick precise pixels.
- **Color Harmony** (toggle): Select from 5 color scheme modes to generate complementary palettes:
  - **Complementary**: Opposite on the color wheel (180°)
  - **Analogous**: Adjacent colors (±30°)
  - **Triadic**: Three evenly-spaced colors (120° apart)
  - **Split-Complementary**: Two colors flanking the complement (150°, 210°)
  - **Tetradic**: Four colors in a square (90°, 180°, 270°)
  - **508 Compliant**: Four colors where the last 3 meet 508 contrast requirements
- **Palette Colors**: Click any palette swatch to copy its HEX value; view RGB and English name labels.
  - Also copies the color to the second contrast swatch so users can calculate contrast ratios easily.

Notes
- This simple implementation uses Win32 APIs to sample the screen; it targets Windows only.
- Standalone EXE is ~111 MB (includes the full .NET 10 runtime).
