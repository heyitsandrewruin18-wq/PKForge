using PKForge.Domain;
using SkiaSharp;

namespace PKForge.Chrome;

/// <summary>A cell of the sprite shelf: a name and its picture (null while the file is not on the device).</summary>
public sealed record SpriteShelfCell(string Name, SKBitmap? Sprite);

/// <summary>
/// The sprite shelf widget: the chosen sprites in the widget family's cyan panels on its sky-blue
/// ground, as many cells as the layout has (2x2: 4, 4x2: 8, 4x4: 16), the sprites as big as their
/// cell allows with their proportions kept. Pixel-art sprites are enlarged by whole steps with
/// nearest sampling so every pixel stays square; larger artwork is smoothed down. Flat colours only.
/// </summary>
public static class SpriteShelfPaint
{
    private static readonly SKColor PanelFill = new(0xe0, 0xf8, 0xf8), PanelRim = new(0x38, 0x98, 0xb0);
    private static readonly SKColor PanelText = new(0x20, 0x40, 0x50), SubText = new(0x48, 0x70, 0x80);
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);
    private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>Sprites up to this size on a side are pixel art (PokéAPI's front sprites are 96 or less, Showdown's 120 or less).</summary>
    private const int PixelArtLimit = 160;

    /// <summary>Paints the shelf at that pixel size. At most the layout's capacity of <paramref name="cells"/> is drawn.</summary>
    public static SKBitmap Render(IReadOnlyList<SpriteShelfCell> cells, HomeWidgetSizeHint size, int widthPx, int heightPx, SKTypeface face)
    {
        widthPx = Math.Max(16, widthPx);
        heightPx = Math.Max(16, heightPx);
        var output = new SKBitmap(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(HomeWidgetPaint.Ground);

        // One unit of border and gap, about a pixel of the widget's own art.
        var unit = Math.Max(1, (int)Math.Round(Math.Min(widthPx, heightPx) / 90.0));
        var margin = unit * 3;
        var shown = cells.Take(SpriteShelf.Capacity(size)).ToArray();
        if (shown.Length == 0)
        {
            Empty(canvas, face, widthPx, heightPx, unit, margin);
            return output;
        }

        // Only as many cells as there are sprites: one sprite fills the widget, three sit in a row.
        var (maxColumns, maxRows) = HomeWidgetSizes.Cells(size);
        var columns = Math.Min(maxColumns, shown.Length);
        var rows = Math.Min(maxRows, (shown.Length + columns - 1) / columns);
        var cellWidth = (widthPx - margin * (columns + 1)) / columns;
        var cellHeight = (heightPx - margin * (rows + 1)) / rows;
        if (cellWidth < 4 || cellHeight < 4) return output;

        var labelled = cellHeight >= unit * 26 && cellWidth >= unit * 24;
        var fontSize = Math.Clamp(cellHeight * 0.13f, 9f, 28f);
        using var font = new SKFont(face, fontSize) { Edging = SKFontEdging.Alias, Hinting = SKFontHinting.None };
        using var paint = new SKPaint { IsAntialias = false };
        for (var i = 0; i < shown.Length; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var x = margin + column * (cellWidth + margin);
            var y = margin + row * (cellHeight + margin);
            Panel(canvas, paint, x, y, cellWidth, cellHeight, unit);

            var inner = new SKRect(x + unit * 2, y + unit * 2, x + cellWidth - unit * 2, y + cellHeight - unit * 2);
            var labelHeight = labelled ? fontSize + unit * 3 : 0f;
            var art = new SKRect(inner.Left, inner.Top, inner.Right, inner.Bottom - labelHeight);
            if (shown[i].Sprite is { } sprite) Sprite(canvas, sprite, art);
            else Pending(canvas, paint, art, unit);
            if (labelled) Label(canvas, font, paint, shown[i].Name, inner, fontSize);
        }
        return output;
    }

    private static void Panel(SKCanvas canvas, SKPaint paint, int x, int y, int width, int height, int unit)
    {
        paint.Color = PanelRim;
        canvas.DrawRect(x, y, width, height, paint);
        paint.Color = PanelFill;
        canvas.DrawRect(x + unit, y + unit, width - unit * 2, height - unit * 2, paint);
    }

    /// <summary>The sprite centred in <paramref name="area"/>, as large as fits; pixel art in whole steps where it can.</summary>
    private static void Sprite(SKCanvas canvas, SKBitmap sprite, SKRect area)
    {
        if (sprite.Width <= 0 || sprite.Height <= 0 || area.Width <= 0 || area.Height <= 0) return;
        var fit = Math.Min(area.Width / sprite.Width, area.Height / sprite.Height);
        var pixelArt = Math.Max(sprite.Width, sprite.Height) <= PixelArtLimit;
        var scale = pixelArt && fit >= 1 ? MathF.Floor(fit) : fit;
        float width = sprite.Width * scale, height = sprite.Height * scale;
        var left = MathF.Round(area.MidX - width / 2);
        var top = MathF.Round(area.MidY - height / 2);
        using var image = SKImage.FromBitmap(sprite);
        canvas.DrawImage(image, SKRect.Create(left, top, width, height), pixelArt && scale >= 1 ? Nearest : Smooth);
    }

    /// <summary>A sprite whose file is not on the device yet: three quiet dots, no claim about the Pokémon.</summary>
    private static void Pending(SKCanvas canvas, SKPaint paint, SKRect area, int unit)
    {
        paint.Color = SubText;
        var size = unit * 2;
        for (var i = -1; i <= 1; i++)
            canvas.DrawRect(area.MidX + i * size * 2 - size / 2f, area.MidY - size / 2f, size, size, paint);
    }

    private static void Label(SKCanvas canvas, SKFont font, SKPaint paint, string name, SKRect inner, float fontSize)
    {
        var fitted = name;
        var room = inner.Width - 2;
        while (fitted.Length > 2 && font.MeasureFallback(fitted) > room) fitted = fitted[..^2].TrimEnd() + "…";
        paint.Color = PanelText;
        canvas.DrawTextFallback(fitted, inner.MidX, inner.Bottom - fontSize * 0.2f, SKTextAlign.Center, font, paint);
    }

    /// <summary>An empty shelf says so, and where to fill it.</summary>
    private static void Empty(SKCanvas canvas, SKTypeface face, int width, int height, int unit, int margin)
    {
        using var paint = new SKPaint { IsAntialias = false };
        Panel(canvas, paint, margin, margin, width - margin * 2, height - margin * 2, unit);
        var size = Math.Clamp(Math.Min(width, height) * 0.11f, 9f, 30f);
        using var font = new SKFont(face, size) { Edging = SKFontEdging.Alias, Hinting = SKFontHinting.None };
        var lines = new[] { ("Sprite shelf", PanelText), ("No sprites yet", SubText), ("Add some in PKForge", SubText) };
        var line = size * 1.5f;
        var top = height / 2f - line * lines.Length / 2f + size;
        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].Item1;
            while (text.Length > 2 && font.MeasureFallback(text) > width - margin * 4) text = text[..^2].TrimEnd() + "…";
            paint.Color = lines[i].Item2;
            canvas.DrawTextFallback(text, width / 2f, top + i * line, SKTextAlign.Center, font, paint);
        }
    }
}
