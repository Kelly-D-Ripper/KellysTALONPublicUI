using System;
using KellysTALONPublicUI;

internal static class Program
{
    private static int passed;

    private static void Main()
    {
        var quote = new PurchaseQuoteCache();
        True(quote.RequestDue("a", "agm", 0), "first preset requests an authoritative quote");
        True(!quote.RequestDue("a", "agm", 1), "render frames do not spam quotes");
        True(float.IsPositiveInfinity(quote.Price(0)), "unknown total cannot enable purchase");
        quote.Receive("a", "agm", true, 125f, "ok", 1);
        Equal(125f, quote.Price(1), "server total is displayed without client recomputation");
        True(quote.RequestDue("a", "aam", 1), "preset switch requests a new total immediately");
        quote.Receive("a", "agm", true, 125f, "ok", 2);
        True(float.IsPositiveInfinity(quote.Price(2)), "late reply for prior preset cannot enable purchase");
        quote.Receive("a", "aam", true, 140f, "ok", 2);
        Equal(140f, quote.Price(6), "current quote remains valid within expiry");
        True(float.IsPositiveInfinity(quote.Price(7)), "expired quote cannot authorize purchase");
        quote.Receive("a", "aam", false, 0f, "unavailable", 8);
        True(float.IsPositiveInfinity(quote.Price(8)), "rejected quote is not a free aircraft");
        foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            quote.Receive("a", "aam", true, bad, "ok", 9);
            True(float.IsPositiveInfinity(quote.Price(9)), "invalid server price fails closed");
        }
        quote.Receive("a", "aam", true, 0f, "ok", 10);
        Equal(0f, quote.Price(10), "legitimate zero price is distinct from unavailable");
        quote.Reset();
        quote.Receive("a", "aam", true, 140f, "ok", 11);
        True(float.IsPositiveInfinity(quote.Price(11)), "disconnect clears cached and in-flight quotes");

        foreach(var key in new[]{"RAH-72", "Aryx_LightHelicopter1", "RAH-72 Knockout", "SAH-46", "AttackHelo1", "SAH-46 Chicane"})
        {
            True(!PublicUiLogic.AllowedAircraft(key,null,null), "Removed helicopter hidden: " + key);
            Equal(0, PublicUiLogic.PresetsFor(key,null,null).Count, "Removed helicopter has no selectable presets");
        }
        True(!PublicUiLogic.AllowedAircraft("UtilityHelo1",null,null), "unrequested helicopter not added");

        // Regression fixture includes the transparent amber border from the failed runtime.
        var native = new[] {
            new MfdPaletteColor(1f, .502f, 0f, 0f),
            new MfdPaletteColor(0f, 1f, 0f, 1f),
            new MfdPaletteColor(0f, .4f, 1f, 1f),
            new MfdPaletteColor(.5f, .5f, .5f, 1f),
            new MfdPaletteColor(0f, 0f, 0f, .6f)
        };
        foreach (var expected in new[] { native[1], native[2], native[3], native[4] })
        {
            var actual = MfdPaletteColor.Pick(native, expected);
            True(actual.R == expected.R && actual.G == expected.G && actual.B == expected.B,
                "native RGB retained; transparent theme payload excluded");
            True(actual.A == expected.A && actual.A > 0f, "resolved native opacity preserved");
        }
        bool emptyRejected = false;
        try { MfdPaletteColor.Pick(new[] { native[0] }, native[3]); }
        catch (InvalidOperationException) { emptyRejected = true; }
        True(emptyRejected, "all-transparent palette rejected");
        foreach (var viewport in new[] { (1280f, 720f), (1920f, 1080f), (2560f, 1440f), (3440f, 1440f) })
        foreach (float bezel in new[] { viewport.Item1 * .3f, 0f, -2000f, float.NaN })
        {
            var p = MfdPanelLayout.Calculate(viewport.Item1, viewport.Item2, bezel, 460f, 650f);
            Equal(true, p.Scale >= .4f && p.Scale <= 1f, "visible panel even with unsettled bezel");
            Equal(true, p.X >= 8f && p.X + 460f * p.Scale <= viewport.Item1 - 8f, "panel fits horizontal viewport");
            Equal(true, p.Y >= 16f && p.Y + 650f * p.Scale <= viewport.Item2 - 16f, "panel fits vertical viewport");
        }
        foreach (var family in new[] {
            ("CI-22", "CI-22 Cricket", "COIN", new[] {"cricket-air-to-ground-agm", "cricket-air-to-ground-lynchpins", "cricket-anti-tank"}),
            ("VT-7", "VT-7 Vagrant", "VTOLTrainer1", new[] {"vagrant-air-to-ground-agm", "vagrant-air-to-air", "vagrant-mixed"}),
            ("A-19", "A-19 Brawler", "CAS1", new[] {"brawler-augers", "brawler-air-to-ground-agm", "brawler-mixed"}),
            ("FS-12", "FS-12 Revoker", "Fighter1", new[] {"revoker-air-to-air", "revoker-air-to-ground-pgm", "revoker-air-to-air-bvr"}) })
        {
            True(PublicUiLogic.AllowedAircraft(family.Item1,null,null), "new key admitted");
            True(PublicUiLogic.AllowedAircraft(null,family.Item2,null), "new display name admitted");
            True(PublicUiLogic.AllowedAircraft(null,null,family.Item3), "new native object admitted");
            var choices = PublicUiLogic.PresetsFor(family.Item1,null,null);
            Equal(3, choices.Count, "three screenshot presets per aircraft");
            for (int i=0;i<3;i++) Equal(family.Item4[i],choices[i].Id,"new wire ID and screenshot ordering");
            False(PublicUiLogic.AllowedAircraft(family.Item1+" Prototype",null,null),"new lookalike denied");
        }
        MfdSlots();
        string[] statuses = { "STATUS UNKNOWN", "FORMING UP", "HOLDING", "ATTACKING AIR", "ATTACKING SHIP",
            "ATTACKING GROUND", "RETURNING TO BASE", "LAUNCHING", "DEFENDING", "JAMMING", "LANDING", "PATROLLING" };
        for (int i = 0; i < statuses.Length; i++) Equal(statuses[i], PublicUiLogic.FlightStatusLabel(i), "server status label " + i);
        Equal("STATUS UNKNOWN", PublicUiLogic.FlightStatusLabel(99), "unknown server status never claims forming up");
        Equal(4, (int)TalonCommand.CycleFormation, "formation opcode matches server");
        Equal(5, (int)TalonCommand.Jam, "jam opcode matches server");
        Equal(3, (int)TalonCommand.ReturnToBase, "existing RTB opcode preserved");
        Equal(100f, PublicUiLogic.DisplayPrice(100f), "full airframe price");
        Equal(0f, PublicUiLogic.DisplayPrice(0f), "zero price");
        True(float.IsPositiveInfinity(PublicUiLogic.DisplayPrice(-1f)), "negative price rejected");
        True(float.IsPositiveInfinity(PublicUiLogic.DisplayPrice(float.NaN)), "NaN rejected");

        False(PublicUiLogic.AllowedAircraft("Kestrel", null, null), "Kestrel excluded");
        False(PublicUiLogic.AllowedAircraft("FQ-106", null, null), "FQ-106 excluded");
        True(PublicUiLogic.AllowedAircraft("Medusa", null, null), "Medusa key admitted");
        True(PublicUiLogic.AllowedAircraft("Aryx_LightFighter1", null, null), "Shrike key admitted");
        True(PublicUiLogic.AllowedAircraft("unknown", "F-99 Shrike", null), "Shrike display name admitted");
        Equal("raptor-air-to-air", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[0].Id, "F22 screenshot 1 wire ID");
        Equal("AIR TO AIR", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[0].Label, "F22 screenshot 1 label");
        Equal("raptor-stealth-air-to-air", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[1].Id, "F22 screenshot 2 wire ID");
        Equal("AIR TO AIR (STEALTH)", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[1].Label, "F22 screenshot 2 label");
        Equal("raptor-air-to-ground-agm", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[2].Id, "F22 screenshot 3 wire ID");
        Equal("AIR TO GROUND (AGM)", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[2].Label, "F22 screenshot 3 label");
        Equal("raptor-air-to-ground-pgm", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[3].Id, "F22 screenshot 4 wire ID");
        Equal("AIR TO GROUND (PGM)", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[3].Label, "F22 screenshot 4 label");
        Equal("raptor-mixed", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[4].Id, "F22 screenshot 5 wire ID");
        Equal("MIXED", PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null)[4].Label, "F22 screenshot 5 label");
        False(PublicUiLogic.AllowedAircraft("Unknown_F22",null,null), "Unverified F22 identity rejected");
        Equal(5, PublicUiLogic.PresetsFor("Aryx_F22E_StrikeRaptor",null,null).Count, "Five armed F22 presets");
        var presets = PublicUiLogic.PresetsFor("Aryx_LightFighter1", null, null);
        Equal(3, presets.Count, "Shrike has three verified presets");
        Equal("shrike-air-to-air", presets[0].Id, "air-to-air preset wire ID");
        Equal("shrike-mixed", presets[1].Id, "mixed preset wire ID");
        Equal("shrike-strike", presets[2].Id, "strike preset wire ID");
        Equal(0, PublicUiLogic.PresetsFor("unknown", null, null).Count, "unsupported aircraft has no presets");
        True(PublicUiLogic.AllowedAircraft("Aryx_F16M", null, null), "King Viper key admitted");
        True(PublicUiLogic.AllowedAircraft("unknown", "F-16M King Viper", null),
            "King Viper display name admitted");
        False(PublicUiLogic.AllowedAircraft("F-16", "Viper", null),
            "ordinary F-16 does not alias King Viper");
        var viperPresets = PublicUiLogic.PresetsFor("Aryx_F16M", null, null);
        Equal(3, viperPresets.Count, "King Viper has three verified presets");
        Equal("king-viper-air-to-air", viperPresets[0].Id, "King Viper air-to-air wire ID");
        Equal("king-viper-air-to-ground", viperPresets[1].Id, "King Viper air-to-ground wire ID");
        Equal("king-viper-strike-bomber", viperPresets[2].Id, "King Viper strike wire ID");
        True(PublicUiLogic.AllowedAircraft("Aryx_FS-41.Eclipse", null, null), "Eclipse key admitted");
        True(PublicUiLogic.AllowedAircraft("unknown", "FS-41 Eclipse", null),
            "Eclipse display name admitted");
        False(PublicUiLogic.AllowedAircraft("FS-40", "Solar Eclipse", null),
            "unrelated Eclipse-like aircraft rejected");
        var eclipsePresets = PublicUiLogic.PresetsFor("Aryx_FS-41.Eclipse", null, null);
        Equal(3, eclipsePresets.Count, "Eclipse has three verified presets");
        Equal("eclipse-air-to-air", eclipsePresets[0].Id, "Eclipse air-to-air wire ID");
        Equal("eclipse-strike", eclipsePresets[1].Id, "Eclipse strike wire ID");
        Equal("eclipse-anti-ship", eclipsePresets[2].Id, "Eclipse anti-ship wire ID");
        True(PublicUiLogic.AllowedAircraft("EW-25", null, null), "Medusa EW-25 key admitted");
        True(PublicUiLogic.AllowedAircraft("unknown", "EW-25 Medusa", null),
            "Medusa display name admitted");
        False(PublicUiLogic.AllowedAircraft("EW-24", "Medusa Missile", null),
            "unrelated Medusa-like identity rejected");
        var medusaPresets = PublicUiLogic.PresetsFor("EW-25", null, null);
        Equal(2, medusaPresets.Count, "Medusa has two verified presets");
        Equal("medusa-radome-only", medusaPresets[0].Id, "Medusa radome-only wire ID");
        Equal("medusa-radome-jammers", medusaPresets[1].Id, "Medusa jammer wire ID");
        foreach (string key in new[] { "MiG-15", "Aryx_MiG-15", "MiG 15" }) {
            False(PublicUiLogic.AllowedAircraft(key, null, null), "MiG hidden from purchase UI");
            False(PublicUiLogic.AllowedAircraft(null, key, null), "MiG display alias hidden");
            False(PublicUiLogic.AllowedAircraft(null, null, key), "MiG object alias hidden");
            Equal(0, PublicUiLogic.PresetsFor(key, null, null).Count, "no MiG loadouts offered");
        }
        True(PublicUiLogic.AllowedAircraft("OA-27", null, null), "Cavalier key admitted");
        True(PublicUiLogic.AllowedAircraft(null, "OA-27 Cavalier", null), "Cavalier display admitted");
        True(PublicUiLogic.AllowedAircraft(null, null, "Cavalier"), "Cavalier object admitted");
        False(PublicUiLogic.AllowedAircraft("OA-28", "Cavalier Prototype", null), "Cavalier lookalikes excluded");
        var cavalierPresets = PublicUiLogic.PresetsFor("OA-27", null, null);
        Equal(2, cavalierPresets.Count, "Cavalier has two presets");
        Equal("cavalier-guns", cavalierPresets[0].Id, "Cavalier guns wire ID");
        Equal("cavalier-ground-attack", cavalierPresets[1].Id, "Cavalier ground wire ID");
        Equal("GUNS", cavalierPresets[0].Label, "Cavalier guns label");
        Equal("GROUND ATTACK", cavalierPresets[1].Label, "Cavalier ground label");
        True(PublicUiLogic.AllowedAircraft("T/A-30", null, null), "Compass key admitted");
        True(PublicUiLogic.AllowedAircraft(null, "T/A-30 Compass", null), "Compass display admitted");
        True(PublicUiLogic.AllowedAircraft(null, null, "Compass"), "Compass object admitted");
        False(PublicUiLogic.AllowedAircraft("T/A-31", "Compass Prototype", "Compass Missile"), "Compass lookalikes excluded");
        var compassPresets = PublicUiLogic.PresetsFor("T/A-30", null, null);
        Equal(3, compassPresets.Count, "Compass has three presets");
        Equal("compass-air-to-air", compassPresets[0].Id, "Compass air wire ID");
        Equal("compass-air-to-ground-agm", compassPresets[1].Id, "Compass AGM wire ID");
        Equal("compass-air-to-ground-bombs", compassPresets[2].Id, "Compass bombs wire ID");
        Equal("AIR TO AIR", compassPresets[0].Label, "Compass air label");
        Equal("AIR TO GROUND (AGM)", compassPresets[1].Label, "Compass AGM label");
        Equal("AIR TO GROUND (BOMBS)", compassPresets[2].Label, "Compass bombs label");
        True(compassPresets[1].Summary.Contains("AGM-48 x3"), "Compass selected AGM-48 shown");
        False(compassPresets[1].Summary.Contains("AGM-68"), "Compass hover description ignored");
        foreach (var compass in compassPresets)
            True(compass.Summary.Contains("MMR-S3"), "Compass outer missile shown");
        Equal(0, PublicUiLogic.PresetsFor("T/A-31", null, null).Count, "Compass lookalike has no presets");
        True(PublicUiLogic.AllowedAircraft("KR-67", null, null), "Ifrit key admitted");
        True(PublicUiLogic.AllowedAircraft(null, "KR-67 Ifrit", null), "Ifrit display admitted");
        True(PublicUiLogic.AllowedAircraft(null, null, "Ifrit"), "Ifrit object admitted");
        False(PublicUiLogic.AllowedAircraft("KR-68", "Ifrit Prototype", "Ifrit Missile"), "Ifrit lookalikes excluded");
        var ifritPresets = PublicUiLogic.PresetsFor("KR-67", null, null);
        Equal(3, ifritPresets.Count, "Only screenshot-confirmed Ifrit presets exposed");
        Equal("ifrit-stealth-air-to-air", ifritPresets[0].Id, "Ifrit stealth wire ID");
        Equal("ifrit-mixed-pab250", ifritPresets[1].Id, "Ifrit mixed wire ID");
        Equal("STEALTH AIR TO AIR", ifritPresets[0].Label, "Ifrit stealth label");
        Equal("MIXED PAB-250", ifritPresets[1].Label, "Ifrit mixed label");
        True(ifritPresets[0].Summary.Contains("Empty wing pylons"), "Ifrit stealth external pylons empty");
        True(ifritPresets[1].Summary.Contains("Inner + Outer PAB-250 x3"), "Ifrit bombs per mount shown");
        Equal("ifrit-air-to-ground-agm", ifritPresets[2].Id, "Ifrit AGM wire ID");
        Equal("AIR TO GROUND (AGM)", ifritPresets[2].Label, "Ifrit AGM label");
        True(ifritPresets[2].Summary.Contains("Forward + rear + inner AGM-68 x2"), "Ifrit AGM station counts displayed");
        foreach (var ifrit in ifritPresets)
        {
            if (ifrit.Id != "ifrit-air-to-ground-agm") True(ifrit.Summary.Contains("MMR-S3 x3"), "Ifrit rear bay missiles shown");
            False(ifrit.Summary.Contains("GPO-500"), "Unconfirmed Ifrit GPO loadout not advertised");
            False(ifrit.Summary.Contains("PGM-400LR"), "Unconfirmed Ifrit PGM loadout not advertised");
        }
        Equal(0, PublicUiLogic.PresetsFor("KR-68", null, null).Count, "Ifrit lookalike has no presets");
        True(PublicUiLogic.AllowedAircraft("FS-20", null, null), "Vortex key admitted");
        True(PublicUiLogic.AllowedAircraft(null, "FS-20 Vortex", null), "Vortex display admitted");
        True(PublicUiLogic.AllowedAircraft(null, null, "Vortex"), "Vortex object admitted");
        False(PublicUiLogic.AllowedAircraft("FS-21", "Vortex Prototype", "Vortex Missile"), "Vortex lookalikes excluded");
        var vortexPresets = PublicUiLogic.PresetsFor("FS-20", null, null);
        Equal(2, vortexPresets.Count, "Vortex has exactly two presets");
        Equal("vortex-stealth-bomber-auger", vortexPresets[0].Id, "Vortex bomber wire ID");
        Equal("vortex-stealth-air-to-air", vortexPresets[1].Id, "Vortex air wire ID");
        Equal("STEALTH BOMBER (AUGER)", vortexPresets[0].Label, "Vortex bomber label");
        Equal("STEALTH AIR TO AIR", vortexPresets[1].Label, "Vortex air label");
        True(vortexPresets[0].Summary.Contains("Inner bay GPO-2P Auger / Outer bay IRM-S2"), "Vortex bomber bays shown");
        True(vortexPresets[1].Summary.Contains("Inner bay IRM-S2 x3 / Outer bay AAM-29 Scythe"), "Vortex air bays shown");
        foreach (var vortex in vortexPresets)
        {
            True(vortex.Summary.Contains("Empty wing pylons"), "Vortex stealth external pylons empty");
            True(vortex.Summary.Contains("20mm Rotary Cannon (500)"), "Vortex gun shown");
        }
        Equal(0, PublicUiLogic.PresetsFor("FS-21", null, null).Count, "Vortex lookalike has no presets");

        Equal(PublicUiLogic.AllWingmen, PublicUiLogic.NextScope(PublicUiLogic.AllWingmen, 0), "empty scope");
        Equal(0, PublicUiLogic.NextScope(PublicUiLogic.AllWingmen, 3), "all to first");
        Equal(1, PublicUiLogic.NextScope(0, 3), "first to second");
        Equal(2, PublicUiLogic.NextScope(1, 3), "second to third");
        Equal(PublicUiLogic.AllWingmen, PublicUiLogic.NextScope(2, 3), "third to all");
        Equal(PublicUiLogic.AllWingmen, PublicUiLogic.NormalizeScope(3, 3), "invalid scope normalized");

        Equal("Hitman-2", PublicUiLogic.Callsign(1, 0), "first callsign");
        Equal("Red-3", PublicUiLogic.Callsign(2, 1), "second session callsign");
        Equal(PublicUiLogic.Callsign(9, 2), PublicUiLogic.Callsign(9, 2), "callsign deterministic");

        Console.WriteLine("KellysTALONPublicUI logic tests passed: " + passed);
    }

    private static void MfdSlots()
    {
        var stock = new object();
        var screens = new System.Collections.Generic.List<object> { stock, stock, stock };
        var talon = new object();
        var lease = MfdSlotLease<object>.TryClaim(screens, 6, talon, _ => true)!;
        Equal(3, lease.Index, "first vacant bezel position");
        Equal(4, screens.Count, "short native list extended only to claimed slot");
        True(lease.Owned, "screen registered");
        True(ReferenceEquals(stock, screens[2]), "HUD untouched");
        lease.Dispose();
        Equal(3, screens.Count, "original list extent restored");
        lease.Dispose();
        Equal(3, screens.Count, "repeat release is harmless");

        screens.Add(stock);
        lease = MfdSlotLease<object>.TryClaim(screens, 6, talon, i => i != 4)!;
        Equal(5, lease.Index, "occupied and missing button skipped");
        screens[5] = stock;
        lease.Dispose();
        True(ReferenceEquals(stock, screens[5]), "successor registration preserved");
        False(lease.Owned, "replaced screen no longer owned");

        screens = new System.Collections.Generic.List<object> { stock, stock, stock, null!, null!, null! };
        lease = MfdSlotLease<object>.TryClaim(screens, 6, talon, _ => true)!;
        var other = new object();
        var second = MfdSlotLease<object>.TryClaim(screens, 6, other, _ => true)!;
        Equal(4, second.Index, "concurrent slot registration coexists");
        lease.Dispose();
        True(second.Owned, "release preserves neighboring screen");
        Equal(6, screens.Count, "serialized null slots retained");
        second.Dispose();
        True(screens[3] == null && screens[4] == null, "both slots cleanly released");
        True(MfdSlotLease<object>.TryClaim(screens, 3, talon, _ => true) == null,
            "never claims stock positions");
        True(MfdSlotLease<object>.TryClaim(screens, 6, talon, _ => false) == null,
            "no usable buttons fails without registration");
        screens = new System.Collections.Generic.List<object> { stock, stock, stock, stock };
        True(MfdSlotLease<object>.TryClaim(screens, 4, talon, _ => true) == null,
            "full MFD fails without replacing a screen");
    }

    private static void True(bool actual, string name)
    {
        if (!actual) throw new InvalidOperationException(name);
        ++passed;
    }

    private static void False(bool actual, string name) => True(!actual, name);

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual);
        ++passed;
    }
}
