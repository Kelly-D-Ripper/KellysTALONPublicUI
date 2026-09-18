using System;

namespace KellysTALONPublicUI;

// Pixel-space placement shared in source with the other Kelly's MFD client; no binary dependency.
internal readonly struct MfdPanelLayout
{
    internal readonly float X, Y, Scale;
    private MfdPanelLayout(float x, float y, float scale) { X = x; Y = y; Scale = scale; }
    internal static MfdPanelLayout Calculate(float width, float height, float bezelLeft, float panelWidth, float panelHeight)
    {
        width = Math.Max(1f, width); height = Math.Max(1f, height);
        if (float.IsNaN(bezelLeft) || float.IsInfinity(bezelLeft)) bezelLeft = width * .3f;
        bezelLeft = Math.Max(0f, Math.Min(width, bezelLeft));
        // A collapsed/unsettled bezel must never shrink a valid panel to a one-pixel sliver.
        float available = Math.Min(Math.Max(1f, width - 16f), Math.Max(panelWidth * .4f, bezelLeft - 22f));
        float scale = Math.Min(1f, Math.Min(available / panelWidth, Math.Max(1f, height - 32f) / panelHeight));
        float x = Math.Max(8f, Math.Min(Math.Max(8f, width - panelWidth * scale - 8f),
            bezelLeft - panelWidth * scale - 12f));
        return new MfdPanelLayout(x, (height - panelHeight * scale) * .5f, scale);
    }
}
