namespace FirstClnkrWar.Sim;

public enum TerrainKind : byte
{
    Land = 0,
    Water = 1,
    Rock = 2,
}

public enum EntityKind : byte
{
    Unit = 0,
    Building = 1,
    Projectile = 2,
    Deposit = 3,
}

public enum OrderKind : byte
{
    Idle = 0,
    Move = 1,
    AttackMove = 2,
    Attack = 3,
}

public enum HaulPhase : byte
{
    Auto = 0,
    ToSite = 1,
    Loading = 2,
    ToRefinery = 3,
    Unloading = 4,
    Manual = 5,
}

public enum CommandKind : byte
{
    Stop = 1,
    Move = 2,
    AttackMove = 3,
    Attack = 4,
}

public readonly struct Cell
{
    public readonly int X;
    public readonly int Y;

    public Cell(int x, int y)
    {
        X = x;
        Y = y;
    }
}

public sealed class SimMap
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Heights { get; }
    public TerrainKind[] Terrain { get; }
    public int[] BlockedBy { get; }
    public byte[] Tint { get; }

    public SimMap(int width, int height)
    {
        Width = width;
        Height = height;
        int n = width * height;
        Heights = new byte[n];
        Terrain = new TerrainKind[n];
        BlockedBy = new int[n];
        Tint = new byte[n];
    }

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public int Index(int x, int y) => y * Width + x;

    public bool Walkable(int x, int y, UnitDomain domain)
    {
        if (!InBounds(x, y))
            return false;
        if (domain == UnitDomain.Air)
            return true;
        int i = Index(x, y);
        if (Terrain[i] != TerrainKind.Land)
            return false;
        return BlockedBy[i] == 0;
    }

    public static SimMap Blank(int width, int height, byte heightValue = 60)
    {
        var map = new SimMap(width, height);
        for (int i = 0; i < map.Heights.Length; i++)
        {
            map.Heights[i] = heightValue;
            map.Terrain[i] = TerrainKind.Land;
        }
        return map;
    }
}

public static class MapGenerator
{
    public static SimMap Generate(int width, int height)
    {
        var map = new SimMap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = map.Index(x, y);
                map.Terrain[i] = TerrainKind.Land;
                map.Heights[i] = (byte)(50 + (Hash(x, y) & 31));
            }
        }

        for (int y = 0; y < height; y++)
        {
            bool bridge = (y >= 34 && y <= 41) || (y >= 86 && y <= 93);
            if (bridge)
                continue;
            for (int x = 60; x <= 67; x++)
            {
                if (!map.InBounds(x, y))
                    continue;
                int i = map.Index(x, y);
                map.Terrain[i] = TerrainKind.Water;
                map.Heights[i] = 12;
            }
        }

        StampRock(map, 46, 52, 4);
        StampRock(map, 78, 56, 3);
        StampRock(map, 48, 78, 3);
        StampRock(map, 90, 48, 4);
        StampRock(map, 24, 70, 3);
        StampRock(map, 100, 70, 3);

        ClearLand(map, 8, 8, 42, 42);
        ClearLand(map, 86, 86, 120, 120);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x <= 1 || y <= 1 || x >= width - 2 || y >= height - 2)
                {
                    int i = map.Index(x, y);
                    map.Terrain[i] = TerrainKind.Rock;
                    map.Heights[i] = 210;
                }
            }
        }

        return map;
    }

    public static void Paint(SimMap map, int x, int y, int w, int h, byte tint)
    {
        for (int yy = y; yy < y + h; yy++)
        {
            for (int xx = x; xx < x + w; xx++)
            {
                if (map.InBounds(xx, yy))
                    map.Tint[map.Index(xx, yy)] = tint;
            }
        }
    }

    private static void ClearLand(SimMap map, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (!map.InBounds(x, y))
                    continue;
                int i = map.Index(x, y);
                map.Terrain[i] = TerrainKind.Land;
                map.Heights[i] = 64;
            }
        }
    }

    private static void StampRock(SimMap map, int cx, int cy, int radius)
    {
        int r2 = radius * radius;
        for (int y = cy - radius; y <= cy + radius; y++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (!map.InBounds(x, y))
                    continue;
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy > r2)
                    continue;
                int i = map.Index(x, y);
                map.Terrain[i] = TerrainKind.Rock;
                map.Heights[i] = 200;
            }
        }
    }

    public static uint Hash(int x, int y)
    {
        unchecked
        {
            uint n = (uint)(x * 374761393 + y * 668265263 + 1440662683);
            n = (n ^ (n >> 13)) * 1274126177u;
            return n ^ (n >> 16);
        }
    }
}

public sealed class PlayerState
{
    public int Id;
    public string Faction = "";
    public int Credits;
    public int Firepower;
    public int Armour;
    public int SpeedLevel;
}

public sealed class SimEntity
{
    public int Id;
    public EntityKind Kind;
    public int PlayerId;
    public string TypeId = "";
    public UnitDomain Domain;
    public string Role = "";
    public string Shape = "box";
    public Fp X;
    public Fp Y;
    public Fp PrevX;
    public Fp PrevY;
    public int CellX;
    public int CellY;
    public int Hp;
    public int MaxHp;
    public bool Alive = true;
    public OrderKind Order;
    public int TargetId;
    public int GoalCellX = -1;
    public int GoalCellY = -1;
    public List<Cell> Path = new();
    public int Waypoint;
    public bool PathPending;
    public int PathToken;
    public int HomeId;
    public int LinkId;
    public int Buffer;
    public int DepositRemaining;
    public bool Rare;
    public int CargoUnits;
    public int CargoValueEach = 1;
    public HaulPhase Haul;
    public int RateAcc;
    public int CreditBank;
    public int[] WeaponCooldown = Array.Empty<int>();
    public int SalvoLeft;
    public int SalvoWait;
    public int SalvoWeapon = -1;
    public int Footprint = 1;
    public int PayloadDamage;
    public int PayloadFirepower;
    public int ProjectileTargetId;
    public long ProjectileStepRaw;
}

public sealed class SimCommand
{
    public int ExecuteTick;
    public int Sequence;
    public int PlayerId;
    public CommandKind Kind;
    public int[] UnitIds = Array.Empty<int>();
    public Fp X;
    public Fp Y;
    public int TargetId;
}
