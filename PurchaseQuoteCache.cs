namespace KellysTALONPublicUI;

// Prices belong to a server-resolved aircraft/preset pair, never a guessed client total.
internal sealed class PurchaseQuoteCache
{
    private string key = "", preset = "";
    private float price = float.PositiveInfinity, expires, nextRequest;
    internal string Reason { get; private set; } = "Requesting airframe + weapons price.";

    internal bool RequestDue(string aircraftKey, string presetId, float now)
    {
        if (key != aircraftKey || preset != presetId)
        { Reset(); key = aircraftKey; preset = presetId; }
        if (now < nextRequest) return false;
        nextRequest = now + 2f;
        if (now >= expires) Reason = "Requesting airframe + weapons price.";
        return true;
    }

    internal float Price(float now) => now < expires ? price : float.PositiveInfinity;

    internal void Receive(string aircraftKey, string presetId, bool accepted, float value, string reason, float now)
    {
        if (key != aircraftKey || preset != presetId) return;
        bool valid = accepted && PublicUiLogic.Finite(value) && value >= 0f;
        price = valid ? value : float.PositiveInfinity;
        expires = now + 5f;
        Reason = valid ? "Airframe + selected weapons." : "Price unavailable: " + PublicUiLogic.HumanReason(reason) + ".";
    }

    internal void Reset()
    {
        key = preset = "";
        price = float.PositiveInfinity;
        expires = nextRequest = 0f;
        Reason = "Requesting airframe + weapons price.";
    }
}
