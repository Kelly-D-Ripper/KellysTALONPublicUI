using System;
using System.Collections.Generic;

namespace KellysTALONPublicUI;

// These are resolved native Graphic colours, not Theme.GetColors override payloads.
internal readonly struct MfdPaletteColor
{
    internal readonly float R, G, B, A;
    internal MfdPaletteColor(float r, float g, float b, float a) { R=r; G=g; B=b; A=a; }
    internal static MfdPaletteColor Pick(IReadOnlyList<MfdPaletteColor> colors, MfdPaletteColor reference)
    {
        int selected = -1;
        float best = float.PositiveInfinity;
        for (int i = 0; i < colors.Count; i++)
        {
            var c = colors[i];
            // Native theme alpha=0 means inherit opacity; a Graphic with alpha=0 is invisible.
            if (!(c.A > 0f) || c.A > 1f) continue;
            float score = (c.R-reference.R)*(c.R-reference.R) + (c.G-reference.G)*(c.G-reference.G) +
                (c.B-reference.B)*(c.B-reference.B) + (c.A-reference.A)*(c.A-reference.A);
            if (score < best) { best=score; selected=i; }
        }
        if (selected < 0) throw new InvalidOperationException("Native MFD palette has no visible colours.");
        return colors[selected];
    }
}
