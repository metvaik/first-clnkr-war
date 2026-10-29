using FirstClnkrWar.Sim;

namespace FirstClnkrWar;

public static class VisualScale
{
    public const float Tile = 2f;
    public const float HeightScale = 6f;

    public static float FromRaw(long raw) => (raw / 1000f) * (Tile / WorldScale.TileWorldUnits);

    public static Fp ToWorld(float visual)
    {
        double raw = visual * (WorldScale.TileWorldUnits * 1000.0 / Tile);
        return new Fp((long)Math.Round(raw, MidpointRounding.ToEven));
    }

    public static float CellX(int cell) => (cell + 0.5f) * Tile;

    public static float Height(byte height) => height / 255f * HeightScale;
}
