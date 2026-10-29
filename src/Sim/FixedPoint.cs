namespace FirstClnkrWar.Sim;

/// <summary>
/// Thousandths fixed-point. 1.720 is raw 1720. No floating point.
/// Sight, range, speed and positions all use this so a lockstep replay
/// cannot drift. Authors still type normal decimals in YAML.
/// </summary>
public readonly struct Fp : IEquatable<Fp>, IComparable<Fp>
{
    public readonly long Raw;

    public const long Scale = 1000;

    public Fp(long raw) => Raw = raw;

    public static Fp Zero => new(0);

    public static Fp FromInt(int value) => new((long)value * Scale);

    public static Fp FromRaw(long raw) => new(raw);

    public static Fp Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Empty fixed-point value.");

        text = text.Trim();
        int sign = 1;
        int i = 0;
        if (text[0] == '-')
        {
            sign = -1;
            i++;
        }
        else if (text[0] == '+')
        {
            i++;
        }

        long whole = 0;
        bool any = false;
        for (; i < text.Length && text[i] != '.'; i++)
        {
            if (text[i] < '0' || text[i] > '9')
                throw new FormatException($"Bad fixed-point '{text}'.");
            any = true;
            whole = whole * 10 + (text[i] - '0');
        }

        long frac = 0;
        int digits = 0;
        if (i < text.Length && text[i] == '.')
        {
            i++;
            for (; i < text.Length && digits < 3; i++, digits++)
            {
                if (text[i] < '0' || text[i] > '9')
                    throw new FormatException($"Bad fixed-point '{text}'.");
                any = true;
                frac = frac * 10 + (text[i] - '0');
            }

            if (i < text.Length)
                throw new FormatException($"Fixed-point '{text}' has more than 3 decimal places.");
        }

        if (!any)
            throw new FormatException($"Bad fixed-point '{text}'.");

        while (digits < 3)
        {
            frac *= 10;
            digits++;
        }

        return new((whole * Scale + frac) * sign);
    }

    public static Fp operator +(Fp a, Fp b) => new(a.Raw + b.Raw);
    public static Fp operator -(Fp a, Fp b) => new(a.Raw - b.Raw);
    public static Fp operator -(Fp a) => new(-a.Raw);
    public static Fp operator *(Fp a, Fp b) => new(a.Raw * b.Raw / Scale);

    public static Fp operator /(Fp a, Fp b)
    {
        if (b.Raw == 0)
            throw new DivideByZeroException();
        return new(a.Raw * Scale / b.Raw);
    }

    public static bool operator <(Fp a, Fp b) => a.Raw < b.Raw;
    public static bool operator >(Fp a, Fp b) => a.Raw > b.Raw;
    public static bool operator <=(Fp a, Fp b) => a.Raw <= b.Raw;
    public static bool operator >=(Fp a, Fp b) => a.Raw >= b.Raw;
    public static bool operator ==(Fp a, Fp b) => a.Raw == b.Raw;
    public static bool operator !=(Fp a, Fp b) => a.Raw != b.Raw;

    public Fp MulDiv(long mul, long div)
    {
        if (div == 0)
            throw new DivideByZeroException();
        return new(Raw * mul / div);
    }

    public int CompareTo(Fp other) => Raw.CompareTo(other.Raw);
    public bool Equals(Fp other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fp other && Equals(other);
    public override int GetHashCode() => Raw.GetHashCode();

    public override string ToString()
    {
        long abs = Math.Abs(Raw);
        string sign = Raw < 0 ? "-" : "";
        return $"{sign}{abs / Scale}.{abs % Scale:000}";
    }
}

/// <summary>
/// 1 small tile = 512 world units. Speeds are small tiles per second.
/// World-units per tick = tiles_per_second * 512 / tick_hz.
/// At 20 Hz a 1.72 tile/s hauler moves 44.032 world units per tick
/// (the design note rounds that illustration to 44).
/// </summary>
public static class WorldScale
{
    public const int TileWorldUnits = 512;

    public static Fp TilesToWorld(Fp tiles) => new(tiles.Raw * TileWorldUnits);

    public static long WorldPerTickRaw(Fp tilesPerSecond, int tickHz)
    {
        if (tickHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(tickHz));
        return tilesPerSecond.Raw * TileWorldUnits / tickHz;
    }

    /// <summary>
    /// Whole ticks for a real-time duration. fireRateScale is thousandths
    /// (750 = 0.75). Weapon cooldowns divide by that scale so 0.75 fires
    /// three quarters as often. Pass scale 1000 for unscaled durations
    /// such as build and hack channels.
    /// </summary>
    public static int DurationTicks(Fp seconds, int tickHz, Fp scale)
    {
        if (tickHz < 1)
            tickHz = 1;
        long denom = scale.Raw < 1 ? Fp.Scale : scale.Raw;
        long ticks = seconds.Raw * tickHz / denom;
        if (ticks < 1)
            ticks = 1;
        return (int)ticks;
    }

    public static int CellOf(Fp world)
    {
        if (world.Raw < 0)
            return -1;
        return (int)(world.Raw / (TileWorldUnits * Fp.Scale));
    }

    public static Fp CellCenter(int cell)
    {
        return new(((long)cell * TileWorldUnits + TileWorldUnits / 2) * Fp.Scale);
    }

    public static long DistanceSquared(Fp ax, Fp ay, Fp bx, Fp by)
    {
        long dx = ax.Raw - bx.Raw;
        long dy = ay.Raw - by.Raw;
        return dx * dx + dy * dy;
    }

    public static bool WithinTiles(Fp ax, Fp ay, Fp bx, Fp by, Fp tiles)
    {
        long limit = tiles.Raw * TileWorldUnits;
        return DistanceSquared(ax, ay, bx, by) <= limit * limit;
    }
}

public static class IntMath
{
    public static long Isqrt(long n)
    {
        if (n <= 0)
            return 0;
        long x = n;
        long y = (x + 1) / 2;
        while (y < x)
        {
            x = y;
            y = (x + n / x) / 2;
        }
        return x;
    }
}

/// <summary>
/// Damage = weaponDamage * scale / 256, minimum 1.
/// scale = 256 * (5 + firepower) * (5 - armour) / 25, levels clamped to [-3, +3].
/// Level 0 is scale 256 (100%). Each firepower level is +20% dealt.
/// Each armour level is -20% taken. Integer division matches the /256 rule,
/// so small payloads can lose a point (10 damage at +1 firepower is 11, not 12).
/// </summary>
public static class DamageMath
{
    public static int Scale(int firepower, int armour)
    {
        int fp = Math.Clamp(firepower, -3, 3);
        int ar = Math.Clamp(armour, -3, 3);
        return (int)(256L * (5 + fp) * (5 - ar) / 25);
    }

    public static int Apply(int weaponDamage, int firepower, int armour)
    {
        if (weaponDamage <= 0)
            return 0;
        int damage = (int)((long)weaponDamage * Scale(firepower, armour) / 256);
        return damage < 1 ? 1 : damage;
    }

    /// <summary>Range bonus is a percent of base, capped. Phase 1 passes 0.</summary>
    public static Fp ApplyRangeBonus(Fp baseRange, int bonusPct, int capPct)
    {
        if (bonusPct < 0)
            bonusPct = 0;
        if (bonusPct > capPct)
            bonusPct = capPct;
        return new Fp(baseRange.Raw * (100 + bonusPct) / 100);
    }
}
