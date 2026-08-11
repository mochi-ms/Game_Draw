namespace GameDraw.Core.Models;

/// <summary>
/// Strategy used to translate an image into input strokes.
/// </summary>
public enum DrawingMode
{
    Auto = 0,
    Pixel = 1,
    HorizontalScanline = 2,
    VerticalScanline = 3,
    Contour = 4,
    Fill = 5,
    Hybrid = 6,
    CleanStroke = 7,
    ArtistStroke = 8
}
