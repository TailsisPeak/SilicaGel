using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace SilicaGel;

/// <summary>
/// Background renderer that draws red wavy underlines under lines flagged with diagnostics.
/// </summary>
public sealed class ErrorSquigglyRenderer : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Selection;

    private IReadOnlyList<Diagnostic> _diagnostics = System.Array.Empty<Diagnostic>();

    private static readonly Pen ErrorPen   = new(new SolidColorBrush(Color.FromRgb(0xF4, 0x47, 0x47)), 1.4);
    private static readonly Pen WarningPen = new(new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)), 1.4);

    public void SetDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_diagnostics.Count == 0) return;
        if (textView.VisualLines.Count == 0) return;

        var doc = textView.Document;
        if (doc == null) return;

        foreach (var d in _diagnostics)
        {
            if (d.Line < 1 || d.Line > doc.LineCount) continue;
            DocumentLine line;
            try { line = doc.GetLineByNumber(d.Line); } catch { continue; }

            var pen = d.Severity == DiagnosticSeverity.Warning ? WarningPen : ErrorPen;

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
            {
                double y = rect.Bottom - 2;
                double xStart = rect.Left;
                double xEnd   = rect.Right;
                if (xEnd <= xStart) continue;
                DrawWavy(drawingContext, pen, xStart, xEnd, y);
            }
        }
    }

    private static void DrawWavy(DrawingContext dc, Pen pen, double x0, double x1, double y)
    {
        const double step = 4;
        const double amp  = 1.6;
        bool up = true;
        double x = x0;
        var prev = new Point(x, y);
        while (x < x1)
        {
            x += step;
            var next = new Point(x, y + (up ? -amp : amp));
            dc.DrawLine(pen, prev, next);
            prev = next;
            up = !up;
        }
    }
}
