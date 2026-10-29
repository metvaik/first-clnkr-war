namespace FirstClnkrWar.Sim;

public sealed class DataSchemaException : Exception
{
    public DataSchemaException(string message) : base(message)
    {
    }
}

public enum UnitDomain : byte
{
    Ground = 0,
    Air = 1,
    Sea = 2,
    Troop = 3,
}

[Flags]
public enum TargetMask : byte
{
    None = 0,
    Ground = 1,
    Sea = 2,
    Air = 4,
    Sub = 8,
    Troop = 16,
    Building = 32,
}

public static class Roles
{
    public const string Hauler = "hauler";
    public const string Refinery = "refinery";
    public const string Collector = "collector";
    public const string Combat = "combat";
    public const string Transport = "transport";
}

public sealed class WeaponDef
{
    public string Id { get; set; } = "";
    public int Damage { get; set; }
    public int DamageTroop { get; set; }
    public int Salvo { get; set; } = 1;
    public Fp SalvoInterval { get; set; }
    public Fp Cooldown { get; set; } = Fp.FromInt(1);
    public Fp Range { get; set; }
    public TargetMask Targets { get; set; }
    public Fp ProjectileSpeed { get; set; } = Fp.FromInt(16);

    public int DamageAgainst(UnitDomain domain)
    {
        if (domain == UnitDomain.Troop && DamageTroop > 0)
            return DamageTroop;
        return Damage;
    }

    public bool CanHit(UnitDomain domain, bool building)
    {
        if (building && Targets.HasFlag(TargetMask.Building))
            return true;
        return domain switch
        {
            UnitDomain.Air => Targets.HasFlag(TargetMask.Air),
            UnitDomain.Sea => Targets.HasFlag(TargetMask.Sea),
            UnitDomain.Troop => Targets.HasFlag(TargetMask.Troop) || Targets.HasFlag(TargetMask.Ground),
            _ => Targets.HasFlag(TargetMask.Ground),
        };
    }
}

public sealed class TransformProfile
{
    public Fp Speed { get; set; }
    public Fp Sight { get; set; }
}

public sealed class UnitDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Faction { get; set; } = "";
    public bool IsBuilding { get; set; }
    public bool Transformable { get; set; }
    public Fp TransformSeconds { get; set; }
    public TransformProfile? Transformed { get; set; }
    public UnitDomain Domain { get; set; }
    public string Role { get; set; } = Roles.Combat;
    public string Shape { get; set; } = "box";
    public int Cost { get; set; }
    public int PerOrder { get; set; } = 1;
    public int BuildSeconds { get; set; }
    public int Hp { get; set; }
    public Fp Speed { get; set; }
    public Fp Sight { get; set; } = Fp.FromInt(12);
    public Fp SightAir { get; set; }
    public Fp SightGround { get; set; }
    public Fp SightSea { get; set; }
    public int WeaponSlots { get; set; }
    public int Cargo { get; set; }
    public int Footprint { get; set; } = 1;
    public int StorageCap { get; set; }
    public int BufferCap { get; set; }
    public List<WeaponDef> Weapons { get; set; } = new();
}

public sealed class FactionDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
}

public sealed class SimRules
{
    public int TickHz { get; set; } = 20;
    public Fp SightMax { get; set; } = Fp.FromInt(20);
    public int RangeBonusMaxPct { get; set; } = 25;
    public int ControlGroups { get; set; } = 10;
    public int SelectionMax { get; set; } = 512;
    public int ProjectilePool { get; set; } = 16384;
    public int PopulationCap { get; set; } = 300;
    public int TargetSearchIntervalTicks { get; set; } = 8;
    public int FogUpdateIntervalTicks { get; set; } = 6;
    public int PathRequestsPerTick { get; set; } = 8;
    public int MiningRatePerSecond { get; set; } = 40;
    public int LoadUnitsPerSecond { get; set; } = 100;
    public int UnloadCreditsPerSecond { get; set; } = 100;
    public int HaulerCapacity { get; set; } = 500;
    public int RefineryStorageCap { get; set; } = 10000;
    public int SiteBuffer { get; set; } = 1000;
    public int RareEarthMultiplier { get; set; } = 2;
    public int StartingCredits { get; set; } = 2500;
    public int StartingFirepower { get; set; }
    public int StartingArmour { get; set; }
    public int StartingSpeed { get; set; }
    public Fp SeparationTiles { get; set; } = Fp.Parse("0.5");
    public Fp TransformSecondsDefault { get; set; } = Fp.FromInt(3);
}

public sealed class DataCatalog
{
    public SimRules Rules { get; } = new();
    public Dictionary<string, UnitDef> Units { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FactionDef> Factions { get; } = new(StringComparer.Ordinal);

    public UnitDef Require(string id)
    {
        if (!Units.TryGetValue(id, out var unit))
            throw new DataSchemaException($"Unknown unit '{id}'.");
        return unit;
    }
}
