namespace FirstClnkrWar.Sim;

/// <summary>
/// 128x128 test map. Two bases, a river with two land bridges, rock,
/// ore fields and rare-earth pockets. No AI: both slots are human.
/// </summary>
public static class PrototypeScenario
{
    public const int MapSize = 128;

    public static Simulation Create(DataCatalog data)
    {
        var map = MapGenerator.Generate(MapSize, MapSize);
        var sim = new Simulation(data, map, 2);
        sim.Players[0].Faction = "tdo";
        sim.Players[1].Faction = "clnkr";

        var tdoOre = sim.SpawnDeposit(26, 18, 22000, rare: false);
        var tdoRare = sim.SpawnDeposit(32, 30, 6000, rare: true);
        MapGenerator.Paint(map, 25, 17, 3, 3, 1);
        MapGenerator.Paint(map, 31, 29, 2, 2, 2);
        var tdoRefinery = sim.SpawnUnit("tdo_refinery", 0, 14, 14);
        var tdoMine = sim.SpawnUnit("tdo_mine", 0, 26, 18);
        tdoMine.LinkId = tdoOre.Id;
        var tdoHauler = sim.SpawnUnit("tdo_hauler", 0, 22, 22);
        tdoHauler.HomeId = tdoRefinery.Id;
        sim.SpawnUnit("tdo_apc", 0, 18, 24);
        sim.SpawnUnit("tdo_jeep", 0, 20, 26);
        sim.SpawnUnit("tdo_jeep", 0, 23, 26);
        sim.SpawnUnit("tdo_dragoon", 0, 18, 29);
        sim.SpawnUnit("tdo_dragoon", 0, 22, 29);
        sim.SpawnUnit("tdo_sam", 0, 26, 30);
        sim.SpawnUnit("tdo_gunship", 0, 16, 33);
        _ = tdoRare;

        var clnkrOre = sim.SpawnDeposit(100, 104, 24000, rare: false);
        var clnkrRare = sim.SpawnDeposit(94, 96, 7000, rare: true);
        MapGenerator.Paint(map, 99, 103, 3, 3, 1);
        MapGenerator.Paint(map, 93, 95, 2, 2, 2);
        var clnkrRefinery = sim.SpawnUnit("clnkr_refinery", 1, 108, 108);
        var clnkrMine = sim.SpawnUnit("clnkr_mine", 1, 100, 104);
        clnkrMine.LinkId = clnkrOre.Id;
        var clnkrHauler = sim.SpawnUnit("clnkr_hauler", 1, 104, 102);
        clnkrHauler.HomeId = clnkrRefinery.Id;
        sim.SpawnUnit("clnkr_warhorse", 1, 112, 100);
        sim.SpawnUnit("clnkr_scout", 1, 108, 98);
        sim.SpawnUnit("clnkr_scout", 1, 112, 96);
        sim.SpawnUnit("clnkr_mk1", 1, 100, 96);
        sim.SpawnUnit("clnkr_mk1", 1, 104, 96);
        sim.SpawnUnit("clnkr_assault_laser", 1, 96, 100);
        sim.SpawnUnit("clnkr_bird", 1, 114, 96);
        _ = clnkrRare;

        var centerSouth = sim.SpawnDeposit(74, 38, 8000, rare: true);
        var centerNorth = sim.SpawnDeposit(74, 90, 8000, rare: true);
        MapGenerator.Paint(map, 73, 37, 3, 3, 2);
        MapGenerator.Paint(map, 73, 89, 3, 3, 2);
        _ = centerSouth;
        _ = centerNorth;

        sim.UpdateFog();
        return sim;
    }
}
