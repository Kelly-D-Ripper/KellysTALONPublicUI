using System;
using System.Collections.Generic;
using System.Globalization;

namespace KellysTALONPublicUI;

internal enum TalonCommand
{
    FormUp = 0,
    AttackMyTargets = 1,
    HoldHere = 2,
    ReturnToBase = 3,
    CycleFormation = 4,
    Jam = 5,
    FullStrike = 6,
    FlightSalvo = 7
}

internal readonly struct PublicLoadoutPreset
{
    internal PublicLoadoutPreset(string id, string label, string summary)
    {
        Id = id;
        Label = label;
        Summary = summary;
    }

    internal string Id { get; }
    internal string Label { get; }
    internal string Summary { get; }
}

internal static class PublicUiLogic
{
    // Protocol 13 server-observed flight status. Unknown future values stay explicit.
    internal static string FlightStatusLabel(int status) => status switch
    {
        1 => "FORMING UP", 2 => "HOLDING", 3 => "ATTACKING AIR", 4 => "ATTACKING SHIP",
        5 => "ATTACKING GROUND", 6 => "RETURNING TO BASE", 7 => "LAUNCHING",
        8 => "DEFENDING", 9 => "JAMMING", 10 => "LANDING", 11 => "PATROLLING",
        _ => "STATUS UNKNOWN"
    };
    internal const int AllWingmen = -1;
    internal const int MaximumWingmen = 3;
    internal const float DisplayCostFraction = 1f;

    private static readonly PublicLoadoutPreset[] CricketPresets =
    {
        new PublicLoadoutPreset("cricket-air-to-ground-agm", "AIR TO GROUND (AGM)",
            "12.7mm (500) / Fuselage + inner AGM-48 x3 / Left AGM-48 x2 / Right IRM-S2 x2"),
        new PublicLoadoutPreset("cricket-air-to-ground-lynchpins", "AIR TO GROUND (LYNCHPINS)",
            "12.7mm + 20mm (500 each) / Fuselage + inner + left AGR-18 x7 / Right IRM-S2 x2"),
        new PublicLoadoutPreset("cricket-anti-tank", "ANTI TANK",
            "12.7mm (500) / ETS-15 / AT-145 x8 / AGM-68 / Outer IRM-S2 x2")
    };

    private static readonly PublicLoadoutPreset[] VagrantPresets =
    {
        new PublicLoadoutPreset("vagrant-air-to-ground-agm", "AIR TO GROUND (AGM)",
            "Empty centre / Fuselage + inner + outer AGM-48 x2 / Wingtip MMR-S3"),
        new PublicLoadoutPreset("vagrant-air-to-air", "AIR TO AIR",
            "20mm (680) / Inner MMR-S3 x2 / Outer + wingtip AAM-29 Scythe"),
        new PublicLoadoutPreset("vagrant-mixed", "MIXED",
            "Centre + inner GPO-500 / Outer ARAD-45 / Wingtip MMR-S3")
    };

    private static readonly PublicLoadoutPreset[] BrawlerPresets =
    {
        new PublicLoadoutPreset("brawler-augers", "AUGERS",
            "Empty guns + centre + inner wing / Fuselage + middle GPO-2P Auger / Outer IRM-S2 x2"),
        new PublicLoadoutPreset("brawler-air-to-ground-agm", "AIR TO GROUND (AGM)",
            "35mm AP (720) / AGM-68 / AGM-48 x3 / AGM-68 x3 + x2 / Outer AGM-48 x2"),
        new PublicLoadoutPreset("brawler-mixed", "MIXED",
            "35mm AP (720) / Fuselage AGR-37 Jester x3 / Inner + middle ARAD-45 / Outer IRM-S2 x2")
    };

    private static readonly PublicLoadoutPreset[] RevokerPresets =
    {
        new PublicLoadoutPreset("revoker-air-to-air", "AIR TO AIR",
            "20mm (750) / Bays + wing MRM-S4 Broadsword x3 / Wingtip AAM-29"),
        new PublicLoadoutPreset("revoker-air-to-ground-pgm", "AIR TO GROUND (PGM)",
            "20mm (750) / Bays + wing PGM-400LR / Wingtip IRM-S5 Gladius"),
        new PublicLoadoutPreset("revoker-air-to-air-bvr", "AIR TO AIR (BVR)",
            "20mm (750) / Bay AAM-36 Scimitar x2 / Wing AAM-29 x3 / Wingtip MRM-S4")
    };

    private static bool IsCricket(string? key, string? name, string? objectName) =>
        IsCricketIdentity(key) || IsCricketIdentity(name) || IsCricketIdentity(objectName);
    private static bool IsCricketIdentity(string? value)
    {
        string id = Normalize(value);
        return id == "ci22" || id == "ci22cricket" || id == "cricket" || id == "coin";
    }

    private static bool IsVagrant(string? key, string? name, string? objectName) =>
        IsVagrantIdentity(key) || IsVagrantIdentity(name) || IsVagrantIdentity(objectName);
    private static bool IsVagrantIdentity(string? value)
    {
        string id = Normalize(value);
        return id == "vt7" || id == "vt7vagrant" || id == "vagrant" || id == "vtoltrainer1";
    }

    private static bool IsBrawler(string? key, string? name, string? objectName) =>
        IsBrawlerIdentity(key) || IsBrawlerIdentity(name) || IsBrawlerIdentity(objectName);
    private static bool IsBrawlerIdentity(string? value)
    {
        string id = Normalize(value);
        return id == "a19" || id == "a19brawler" || id == "brawler" || id == "cas1";
    }

    private static bool IsRevoker(string? key, string? name, string? objectName) =>
        IsRevokerIdentity(key) || IsRevokerIdentity(name) || IsRevokerIdentity(objectName);
    private static bool IsRevokerIdentity(string? value)
    {
        string id = Normalize(value);
        return id == "fs12" || id == "fs12revoker" || id == "revoker" || id == "fighter1";
    }

    private static readonly PublicLoadoutPreset[] ShrikePresets =
    {
        new PublicLoadoutPreset("shrike-air-to-air", "AIR TO AIR",
            "AAM-29 Scythe x2 / MRM-S4 Broadsword / IRM-S5 Gladius"),
        new PublicLoadoutPreset("shrike-mixed", "MIXED",
            "AAM-29 Scythe x2 / AGM-76 Atlatl x3 / IRM-S2 x2"),
        new PublicLoadoutPreset("shrike-strike", "AIR TO GROUND (STRIKE BOMBER)",
            "PAB-125 x4 + x4 / IRM-S2 x2")
    };

    private static readonly PublicLoadoutPreset[] KingViperPresets =
    {
        new PublicLoadoutPreset("king-viper-air-to-air", "AIR TO AIR",
            "ECS-32 / ARAD-45 / AAM-29 x2 / MRM-S4 / IRM-S5"),
        new PublicLoadoutPreset("king-viper-air-to-ground", "AIR TO GROUND",
            "AGM-76 x3 + x4 + x2 / IRM-S5 x2 / ETS-15"),
        new PublicLoadoutPreset("king-viper-strike-bomber", "STRIKE BOMBER",
            "GPO-500 / GPO-2P Auger / GPO-500 / IRM-S5 x2 / ETS-15")
    };

    private static readonly PublicLoadoutPreset[] EclipsePresets =
    {
        new PublicLoadoutPreset("eclipse-air-to-air", "AIR TO AIR",
            "AAM-45 x2 + x2 / IRM-S2 / AAM-29 x3 / MRM-S4 x2"),
        new PublicLoadoutPreset("eclipse-strike", "STRIKE",
            "GPO-500 x2 + x2 + x2 / IRM-S2 / MRM-S4 x2"),
        new PublicLoadoutPreset("eclipse-anti-ship", "ANTI SHIP",
            "AAM-29 x4 / AGM-99 x3 + x2 + x1 / IRM-S2")
    };

    private static readonly PublicLoadoutPreset[] MedusaPresets =
    {
        new PublicLoadoutPreset("medusa-radome-only", "RADOME ONLY",
            "120kW High Energy Laser / Radome / 450kg Drop Tank"),
        new PublicLoadoutPreset("medusa-radome-jammers", "RADOME AND JAMMERS",
            "120kW High Energy Laser / IRM-S2 x3 / Radome / Radar Jamming Pods x2")
    };

    private static readonly PublicLoadoutPreset[] CavalierPresets =
    {
        new PublicLoadoutPreset("cavalier-guns", "GUNS",
            "20mm (200) / 30mm (300) / Inner + Outer 12.7mm (500 each mount) / IRM-S2"),
        new PublicLoadoutPreset("cavalier-ground-attack", "GROUND ATTACK",
            "20mm (200) / AGM-48 x2 / Inner + Outer AGR-24 Kingpin x4 / IRM-S2")
    };

    private static readonly PublicLoadoutPreset[] CompassPresets =
    {
        new PublicLoadoutPreset("compass-air-to-air", "AIR TO AIR",
            "Bay IRM-S2 x3 / Inner + Middle AAM-29 Scythe / Outer MMR-S3 / Tail Hook"),
        new PublicLoadoutPreset("compass-air-to-ground-agm", "AIR TO GROUND (AGM)",
            "Bay ETS-15 / Inner + Middle AGM-48 x3 / Outer MMR-S3 / Tail Hook"),
        new PublicLoadoutPreset("compass-air-to-ground-bombs", "AIR TO GROUND (BOMBS)",
            "Bay PAB-250 x2 / Inner PAB-250 x3 / Middle PAB-250 / Outer MMR-S3 / Tail Hook")
    };

    private static readonly PublicLoadoutPreset[] IfritPresets =
    {
        new PublicLoadoutPreset("ifrit-stealth-air-to-air", "STEALTH AIR TO AIR",
            "27mm (540) / Forward AAM-29 x3 / Rear MMR-S3 x3 / Side IRM-S2 / Empty wing pylons / Tail Hook"),
        new PublicLoadoutPreset("ifrit-mixed-pab250", "MIXED PAB-250",
            "27mm (540) / AAM-29 x3 / MMR-S3 x3 / IRM-S2 / Inner + Outer PAB-250 x3 / Tail Hook"),
        new PublicLoadoutPreset("ifrit-air-to-ground-agm", "AIR TO GROUND (AGM)",
            "27mm (540) / Forward + rear + inner AGM-68 x2 / Side IRM-S2 / Outer AAM-29 Scythe / Tail Hook")
    };

    private static readonly PublicLoadoutPreset[] VortexPresets =
    {
        new PublicLoadoutPreset("vortex-stealth-bomber-auger", "STEALTH BOMBER (AUGER)",
            "20mm Rotary Cannon (500) / Inner bay GPO-2P Auger / Outer bay IRM-S2 / Empty wing pylons"),
        new PublicLoadoutPreset("vortex-stealth-air-to-air", "STEALTH AIR TO AIR",
            "20mm Rotary Cannon (500) / Inner bay IRM-S2 x3 / Outer bay AAM-29 Scythe / Empty wing pylons")
    };

    private static readonly string[] Callsigns =
    {
        "Hitman", "Red", "Gold", "Viper", "Falcon", "Saber", "Talon", "Reaper", "Raven", "Jackal",
        "Ghost", "Wolf", "Eagle", "Lancer", "Ranger", "Hunter", "Nomad", "Viking", "Titan", "Cobra",
        "Diamond", "Striker", "Sentinel", "Phoenix", "Thunder", "Cyclone", "Tempest", "Warlock", "Dagger",
        "Spartan", "Banshee", "Raptor", "Arrow", "Charger", "Mustang", "Outlaw", "Paladin", "Hammer",
        "Guardian", "Specter"
    };

    internal static float DisplayPrice(float aircraftValue) =>
        Finite(aircraftValue) && aircraftValue >= 0f ? aircraftValue * DisplayCostFraction : float.PositiveInfinity;

    // Identity and 11-group layout verified against the installed F-22E 0.0.6 bundle.
    internal static bool IsRaptor(string? key, string? name, string? objectName) =>
        Normalize(key) == "aryxf22estrikeraptor" || Normalize(name) == "f22estrikeraptor" ||
        Normalize(objectName) == "aryxkingraptordefinition";
    private static readonly PublicLoadoutPreset[] RaptorPresets =
    {
        new PublicLoadoutPreset("raptor-air-to-air", "AIR TO AIR",
            "20mm (500) / Heater IRM-S5 / Bays AAM-29 x3 / Inner MRM-S4 x3 / Outer MRM-S4 x2 / Tailhook"),
        new PublicLoadoutPreset("raptor-stealth-air-to-air", "AIR TO AIR (STEALTH)",
            "20mm (500) / Heater IRM-S5 / Bays AAM-29 x3 / Empty wing pylons / Tailhook"),
        new PublicLoadoutPreset("raptor-air-to-ground-agm", "AIR TO GROUND (AGM)",
            "20mm (500) / Heater IRM-S5 / Outer bays AGM-27 x4 / Inner bays AGM-27 x2 / Inner wing AGM-27 x3 / Outer wing MRM-S4 x2 / Tailhook"),
        new PublicLoadoutPreset("raptor-air-to-ground-pgm", "AIR TO GROUND (PGM)",
            "20mm (500) / Heater IRM-S5 / Outer bays AAM-29 x2 / Inner bays AAM-29 / Inner + outer wing PGM-400LR x2 / Tailhook"),
        new PublicLoadoutPreset("raptor-mixed", "MIXED",
            "20mm (500) / Heater IRM-S5 / Bays AAM-29 x3 / Inner ARAD-45 x2 / Outer AGM-27 x3 / Tailhook")
    };

    internal static bool AllowedAircraft(string? key, string? unitName, string? objectName)
    {
        return IsShrike(key, unitName, objectName) || IsKingViper(key, unitName, objectName) ||
               IsEclipse(key, unitName, objectName) || IsMedusa(key, unitName, objectName) ||
               IsCavalier(key, unitName, objectName) ||
               IsCompass(key, unitName, objectName) || IsIfrit(key, unitName, objectName) ||
               IsVortex(key, unitName, objectName) ||
               IsCricket(key, unitName, objectName) ||
               IsVagrant(key, unitName, objectName) ||
               IsBrawler(key, unitName, objectName) ||
               IsRevoker(key, unitName, objectName) || IsRaptor(key, unitName, objectName);
    }

    internal static IReadOnlyList<PublicLoadoutPreset> PresetsFor(string? key, string? unitName, string? objectName) =>
        IsRaptor(key, unitName, objectName) ? RaptorPresets :
        IsShrike(key, unitName, objectName) ? ShrikePresets :
        IsKingViper(key, unitName, objectName) ? KingViperPresets :
        IsEclipse(key, unitName, objectName) ? EclipsePresets :
        IsMedusa(key, unitName, objectName) ? MedusaPresets :
        IsCavalier(key, unitName, objectName) ? CavalierPresets :
        IsCompass(key, unitName, objectName) ? CompassPresets :
        IsIfrit(key, unitName, objectName) ? IfritPresets :
        IsVortex(key, unitName, objectName) ? VortexPresets :
        IsCricket(key, unitName, objectName) ? CricketPresets :
        IsVagrant(key, unitName, objectName) ? VagrantPresets :
        IsBrawler(key, unitName, objectName) ? BrawlerPresets :
        IsRevoker(key, unitName, objectName) ? RevokerPresets :
        Array.Empty<PublicLoadoutPreset>();

    internal static int NormalizeScope(int scope, int count) =>
        scope >= 0 && scope < Math.Max(0, Math.Min(MaximumWingmen, count)) ? scope : AllWingmen;

    internal static int NextScope(int scope, int count)
    {
        int bounded = Math.Max(0, Math.Min(MaximumWingmen, count));
        if (bounded == 0) return AllWingmen;
        int current = NormalizeScope(scope, bounded);
        return current == AllWingmen ? 0 : current + 1 < bounded ? current + 1 : AllWingmen;
    }

    internal static string Callsign(uint sessionId, int ordinal)
    {
        uint callsignIndex = sessionId == 0 ? 0 : (sessionId - 1u) % (uint)Callsigns.Length;
        long number = Math.Max(0L, (long)ordinal) + 2L;
        return Callsigns[callsignIndex] + "-" + number.ToString(CultureInfo.InvariantCulture);
    }

    internal static string HumanReason(string? reason) => string.IsNullOrWhiteSpace(reason)
        ? "UNKNOWN"
        : reason.Replace('_', ' ').ToUpperInvariant();

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsShrikeIdentity(string? value)
    {
        string identity = Normalize(value);
        return identity == "f99" || identity == "f99shrike" || identity == "shrike" ||
               identity == "aryxlightfighter1" || identity == "aryxf99shrike";
    }

    private static bool IsShrike(string? key, string? unitName, string? objectName) =>
        IsShrikeIdentity(key) || IsShrikeIdentity(unitName) || IsShrikeIdentity(objectName);

    private static bool IsKingViper(string? key, string? unitName, string? objectName) =>
        IsKingViperIdentity(key) || IsKingViperIdentity(unitName) || IsKingViperIdentity(objectName);

    private static bool IsKingViperIdentity(string? value)
    {
        string identity = Normalize(value);
        return identity == "f16m" || identity == "f16mkingviper" || identity == "kingviper" ||
               identity == "aryxf16m" || identity == "aryxf16mkingviper";
    }

    private static bool IsEclipse(string? key, string? unitName, string? objectName) =>
        IsEclipseIdentity(key) || IsEclipseIdentity(unitName) || IsEclipseIdentity(objectName);

    private static bool IsEclipseIdentity(string? value)
    {
        string identity = Normalize(value);
        return identity == "fs41" || identity == "fs41eclipse" || identity == "eclipse" ||
               identity == "aryxfs41" || identity == "aryxfs41eclipse";
    }

    private static bool IsMedusa(string? key, string? unitName, string? objectName) =>
        IsMedusaIdentity(key) || IsMedusaIdentity(unitName) || IsMedusaIdentity(objectName);

    private static bool IsMedusaIdentity(string? value)
    {
        string identity = Normalize(value);
        return identity == "ew25" || identity == "ew25medusa" || identity == "medusa";
    }


    private static bool IsCavalier(string? key, string? unitName, string? objectName) =>
        IsCavalierIdentity(key) || IsCavalierIdentity(unitName) || IsCavalierIdentity(objectName);

    private static bool IsCavalierIdentity(string? value)
    {
        string identity = Normalize(value);
        return identity == "oa27" || identity == "oa27cavalier" || identity == "cavalier";
    }

    private static bool IsCompass(string? key, string? unitName, string? objectName) =>
        IsCompassIdentity(key) || IsCompassIdentity(unitName) || IsCompassIdentity(objectName);

    private static bool IsCompassIdentity(string? value)
    {
        string identity = Normalize(value);
        return identity == "ta30" || identity == "ta30compass" || identity == "compass";
    }

    private static bool IsIfrit(string? key, string? unitName, string? objectName) =>
        IsIfritIdentity(key) || IsIfritIdentity(unitName) || IsIfritIdentity(objectName);

    private static bool IsIfritIdentity(string? value)
    {
        string identity = Normalize(value);
        return identity == "kr67" || identity == "kr67ifrit" || identity == "ifrit";
    }

    private static bool IsVortex(string? key, string? unitName, string? objectName) =>
        IsVortexIdentity(key) || IsVortexIdentity(unitName) || IsVortexIdentity(objectName);

    private static bool IsVortexIdentity(string? value)
    {
        string identity = Normalize(value);
        return identity == "fs20" || identity == "fs20vortex" || identity == "vortex";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        char[] buffer = new char[value.Length];
        int length = 0;
        for (int index = 0; index < value.Length; index++)
            if (char.IsLetterOrDigit(value[index])) buffer[length++] = char.ToLowerInvariant(value[index]);
        return new string(buffer, 0, length);
    }
}
