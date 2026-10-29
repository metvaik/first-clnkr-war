using FirstClnkrWar.Sim;
using Xunit;

namespace FirstClnkrWar.Tests;

public class FixedPointTests
{
    [Theory]
    [InlineData("1.72", 1720)]
    [InlineData("3.01", 3010)]
    [InlineData("4.49", 4490)]
    [InlineData("0.94", 940)]
    [InlineData("43.2", 43200)]
    [InlineData("12", 12000)]
    [InlineData("1.720", 1720)]
    [InlineData("-1.5", -1500)]
    public void ParsesThousandths(string text, long raw)
    {
        Assert.Equal(raw, Fp.Parse(text).Raw);
    }

    [Fact]
    public void HaulerMovesExactlyOneSecondAtTwentyHertz()
    {
        var speed = Fp.Parse("1.72");
        long perTick = WorldScale.WorldPerTickRaw(speed, 20);
        Assert.Equal(44032, perTick);
        Assert.Equal(880640, perTick * 20);
    }

    [Fact]
    public void ArithmeticStaysIntegral()
    {
        var a = Fp.Parse("4.4");
        var b = Fp.Parse("2");
        Assert.Equal(4400, a.Raw);
        Assert.Equal(8800, (a * b).Raw);
        Assert.Equal(2200, (a / b).Raw);
        Assert.Equal(6600, a.MulDiv(3, 2).Raw);
    }

    [Fact]
    public void DamageScaleMatchesThe256Rule()
    {
        Assert.Equal(256, DamageMath.Scale(0, 0));
        Assert.Equal(10, DamageMath.Apply(10, 0, 0));
        Assert.Equal(1, DamageMath.Apply(1, 0, 0));
        Assert.Equal(1, DamageMath.Apply(1, -3, 3));
        int boosted = DamageMath.Apply(10, 1, 0);
        Assert.Equal(10 * DamageMath.Scale(1, 0) / 256, boosted);
        Assert.Equal(11, boosted);
    }

    [Fact]
    public void RangeBonusIsCapped()
    {
        var range = Fp.FromInt(20);
        Assert.Equal(25000, DamageMath.ApplyRangeBonus(range, 40, 25).Raw);
        Assert.Equal(22000, DamageMath.ApplyRangeBonus(range, 10, 25).Raw);
    }
}

public class SchemaTests
{
    [Fact]
    public void SpeedZeroNonBuildingIsRejected()
    {
        var ex = Assert.Throws<DataSchemaException>(() => CatalogLoader.Load(new[]
        {
            ("units/bad.yaml", """
                units:
                  - id: bad_turret
                    name: Turret
                    faction: tdo
                    domain: ground
                    hp: 40
                    speed: 0
                """),
        }));
        Assert.Equal("make it a building or transformable", ex.Message);
    }

    [Fact]
    public void MissingSpeedIsTheSameError()
    {
        var ex = Assert.Throws<DataSchemaException>(() => CatalogLoader.Load(new[]
        {
            ("units/bad.yaml", """
                units:
                  - id: stuck
                    name: Stuck
                    hp: 10
                """),
        }));
        Assert.Equal("make it a building or transformable", ex.Message);
    }

    [Fact]
    public void TransformableMayHaveSpeedZero()
    {
        var catalog = CatalogLoader.Load(new[]
        {
            ("units/ok.yaml", """
                units:
                  - id: deploy_tank
                    name: Deploy Tank
                    faction: tdo
                    domain: ground
                    hp: 80
                    speed: 0
                    transformable: true
                    transformed:
                      speed: 1.5
                      sight: 18
                """),
        });
        var unit = catalog.Require("deploy_tank");
        Assert.True(unit.Transformable);
        Assert.Equal(0, unit.Speed.Raw);
        Assert.Equal(3000, unit.TransformSeconds.Raw);
        Assert.NotNull(unit.Transformed);
        Assert.Equal(1500, unit.Transformed!.Speed.Raw);
    }

    [Fact]
    public void BuildingsMayOmitSpeed()
    {
        var catalog = CatalogLoader.Load(new[]
        {
            ("buildings.yaml", """
                buildings:
                  - id: pad
                    name: Pad
                    hp: 50
                    role: refinery
                """),
        });
        Assert.True(catalog.Require("pad").IsBuilding);
        Assert.Equal(0, catalog.Require("pad").Speed.Raw);
    }
}

public class SimulationTests
{
    private static DataCatalog Load()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            string candidate = Path.Combine(dir, "data", "rules.yaml");
            if (File.Exists(candidate))
                return CatalogLoader.LoadFromDirectory(Path.Combine(dir, "data"));
            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }
        throw new DirectoryNotFoundException("Could not find data/rules.yaml.");
    }

    [Fact]
    public void RosterMatchesTheDesignTables()
    {
        var data = Load();
        Expect(data, "tdo_apc", 750, 100, "3.01", null, 0, 1);
        Expect(data, "tdo_jeep", 500, 35, "4.49", "4", 1, 5);
        Expect(data, "tdo_dragoon", 1000, 72, "2.15", "8", 10, 1);
        Assert.Equal(3, data.Require("tdo_dragoon").Weapons[0].DamageTroop);
        Expect(data, "tdo_sam", 2500, 120, "3.01", "20", 40, 1);
        Assert.Equal(20000, data.Require("tdo_sam").SightAir.Raw);
        Assert.Equal(TargetMask.Air, data.Require("tdo_sam").Weapons[0].Targets);
        Expect(data, "tdo_hauler", 500, 100, "1.72", null, 0, 1);
        Assert.Equal(12000, data.Require("tdo_hauler").Sight.Raw);
        Assert.Equal(500, data.Require("tdo_hauler").Cargo);
        Expect(data, "tdo_gunship", 4000, 54, "5.78", "16", 8, 1);
        Expect(data, "tdo_technician", 1000, 18, "1.05", null, 0, 1);
        Expect(data, "tdo_hacker", 2000, 18, "1.05", null, 0, 1);
        Assert.Equal(30, data.Require("tdo_gunship").Weapons[1].Damage);

        Expect(data, "clnkr_warhorse", 1000, 140, "2.15", null, 0, 1);
        Expect(data, "clnkr_scout", 750, 30, "5.66", "4", 1, 4);
        Expect(data, "clnkr_mk1", 1300, 81, "3.01", "8", 5, 1);
        Assert.Equal(2, data.Require("clnkr_mk1").Weapons[0].DamageTroop);
        Expect(data, "clnkr_assault_laser", 2500, 180, "3.01", "6", 12, 1);
        Assert.Equal(9, data.Require("clnkr_assault_laser").Weapons[1].Damage);
        Expect(data, "clnkr_hauler", 500, 100, "1.72", null, 0, 1);
        Expect(data, "clnkr_bird", 3500, 68, "4.30", "16", 12, 1);
        Expect(data, "clnkr_constructobot", 1100, 14, "0.86", null, 0, 1);
        Expect(data, "clnkr_agent", 2000, 18, "1.05", null, 0, 1);

        Assert.Equal(1000, data.Require("tdo_refinery").Cost);
        Assert.Equal(1000, data.Require("tdo_refinery").Hp);
        Assert.Equal(800, data.Require("tdo_mine").Cost);
        Assert.Equal(400, data.Require("clnkr_mine").Hp);
        Assert.Equal(20, data.Rules.TickHz);
        Assert.Equal(100, data.Rules.UnloadCreditsPerSecond);
    }

    [Fact]
    public void PathCrossesTheTestMapBridges()
    {
        var sim = PrototypeScenario.Create(Load());
        var path = Pathfinder.Find(sim.Map, UnitDomain.Ground, 22, 22, 104, 102);
        Assert.NotEmpty(path);
        Assert.Equal(22, path[0].X);
        Assert.Equal(22, path[0].Y);
        Assert.Equal(104, path[^1].X);
        Assert.Equal(102, path[^1].Y);
    }

    [Fact]
    public void SameCommandLogProducesTheSameHash()
    {
        ulong firstStart = 0;
        ulong firstMid = 0;
        ulong firstEnd = 0;
        ulong Run()
        {
            var sim = PrototypeScenario.Create(Load());
            EnqueuePush(sim);
            firstStart = sim.Hash();
            Step(sim, 40);
            firstMid = sim.Hash();
            Step(sim, 210);
            return sim.Hash();
        }

        ulong a = Run();
        ulong start = firstStart;
        ulong mid = firstMid;
        ulong b = Run();
        Assert.Equal(a, b);
        Assert.Equal(start, firstStart);
        Assert.Equal(mid, firstMid);
        Assert.NotEqual(start, a);
        firstEnd = a;
        Assert.NotEqual(0UL, firstEnd);
    }

    [Fact]
    public void GroundUnitsDamageEachOther()
    {
        var data = Load();
        var sim = new Simulation(data, SimMap.Blank(32, 32), 2);
        sim.Players[0].Faction = "tdo";
        sim.Players[1].Faction = "clnkr";
        var jeep = sim.SpawnUnit("tdo_jeep", 0, 8, 8);
        var walker = sim.SpawnUnit("clnkr_mk1", 1, 11, 8);
        sim.UpdateFog();
        int before = jeep.Hp + walker.Hp;
        Step(sim, 120);
        int after = (jeep.Alive ? jeep.Hp : 0) + (walker.Alive ? walker.Hp : 0);
        Assert.True(after < before, $"expected damage, jeep {jeep.Hp}/{jeep.MaxHp} walker {walker.Hp}/{walker.MaxHp}");
    }

    [Fact]
    public void MobileSamSeesAirBeyondNormalSight()
    {
        var data = Load();
        var sim = new Simulation(data, SimMap.Blank(48, 48), 2);
        sim.Players[0].Faction = "tdo";
        sim.Players[1].Faction = "clnkr";
        var sam = sim.SpawnUnit("tdo_sam", 0, 10, 10);
        var gunship = sim.SpawnUnit("clnkr_bird", 1, 10, 28);
        var walker = sim.SpawnUnit("clnkr_mk1", 1, 28, 10);
        sim.UpdateFog();
        Assert.True(sim.CellVisible(0, gunship.CellX, gunship.CellY));
        Assert.False(sim.CellVisible(0, walker.CellX, walker.CellY));
        int walkerHp = walker.Hp;
        Step(sim, 160);
        Assert.True(gunship.Hp < gunship.MaxHp || !gunship.Alive, $"gunship hp {gunship.Hp}");
        Assert.Equal(walkerHp, walker.Hp);
        Assert.True(sam.Alive);
    }

    [Fact]
    public void GroupMoveSpreadsAndAcceptsASecondOrder()
    {
        var data = Load();
        var sim = new Simulation(data, SimMap.Blank(64, 64), 1);
        sim.Players[0].Faction = "tdo";
        var units = new List<SimEntity>();
        for (int i = 0; i < 4; i++)
            units.Add(sim.SpawnUnit("tdo_jeep", 0, 8 + i, 8));

        void Order(int cellX, int cellY)
        {
            sim.Enqueue(new SimCommand
            {
                ExecuteTick = sim.Tick + 1,
                PlayerId = 0,
                Kind = CommandKind.Move,
                UnitIds = units.Select(u => u.Id).ToArray(),
                X = WorldScale.CellCenter(cellX),
                Y = WorldScale.CellCenter(cellY),
            });
        }

        Order(40, 40);
        Step(sim, 900);
        AssertNear(units, 40, 40, "first order");
        AssertSeparated(units);

        Order(12, 18);
        Step(sim, 900);
        AssertNear(units, 12, 18, "second order");
        AssertSeparated(units);
        Assert.All(units, u => Assert.False(u.PathPending));
    }

    [Fact]
    public void HaulerDocksAgainstTheBuilding()
    {
        var sim = PrototypeScenario.Create(Load());
        SimEntity? hauler = null;
        bool docked = false;
        for (int n = 0; n < 2000 && !docked; n++)
        {
            sim.Step();
            hauler = sim.Entities.First(e => e.TypeId == "tdo_hauler");
            docked = hauler.Haul is HaulPhase.Loading or HaulPhase.Unloading;
        }
        Assert.True(docked, $"never docked, haul {hauler!.Haul} cell {hauler.CellX},{hauler.CellY}");
        var site = sim.Entities.First(e => e.TypeId == "tdo_mine");
        var refinery = sim.Entities.First(e => e.TypeId == "tdo_refinery");
        var building = hauler!.Haul == HaulPhase.Unloading ? refinery : site;
        long gap = GapToFootprint(hauler, building);
        Assert.InRange(gap, 50L * 1000, 230L * 1000);
    }

    [Fact]
    public void SpeedScaleHalvesTravel()
    {
        var data = Load();
        Assert.Equal(500, data.Rules.SpeedScale.Raw);
        var sim = new Simulation(data, SimMap.Blank(48, 48), 1);
        sim.Players[0].Faction = "tdo";
        var jeep = sim.SpawnUnit("tdo_jeep", 0, 4, 10);
        long start = jeep.X.Raw;
        sim.Enqueue(new SimCommand
        {
            ExecuteTick = 1,
            PlayerId = 0,
            Kind = CommandKind.Move,
            UnitIds = new[] { jeep.Id },
            X = WorldScale.CellCenter(30),
            Y = WorldScale.CellCenter(10),
        });
        Step(sim, 22);
        long moved = jeep.X.Raw - start;
        long tile = WorldScale.TileWorldUnits * Fp.Scale;
        double tiles = moved / (double)tile;
        double expected = 4.49 * 0.5;
        Assert.InRange(tiles, expected * 0.7, expected * 1.15);
    }

    [Fact]
    public void FireRateScaleLengthensCooldowns()
    {
        int full = WorldScale.DurationTicks(Fp.Parse("0.8"), 20, Fp.FromInt(1));
        int scaled = WorldScale.DurationTicks(Fp.Parse("0.8"), 20, Fp.Parse("0.75"));
        Assert.Equal(16, full);
        Assert.Equal(21, scaled);
        Assert.True(scaled > full);
    }

    [Fact]
    public void ConstructorPlacesARefinery()
    {
        var data = Load();
        var sim = new Simulation(data, SimMap.Blank(32, 32), 1);
        sim.Players[0].Faction = "tdo";
        sim.Players[0].Credits = 5000;
        var tech = sim.SpawnUnit("tdo_technician", 0, 4, 4);
        sim.Enqueue(new SimCommand
        {
            ExecuteTick = 1,
            PlayerId = 0,
            Kind = CommandKind.Build,
            UnitIds = new[] { tech.Id },
            BuildingTypeId = "tdo_refinery",
            X = WorldScale.CellCenter(12),
            Y = WorldScale.CellCenter(8),
        });
        Step(sim, 1200);
        Assert.Contains(sim.Entities, e => e.Alive && e.TypeId == "tdo_refinery" && e.PlayerId == 0);
        Assert.Equal(4000, sim.Players[0].Credits);
    }

    [Fact]
    public void HackerCapturesThenScrambles()
    {
        var data = Load();
        var sim = new Simulation(data, SimMap.Blank(40, 40), 2);
        sim.Players[0].Faction = "tdo";
        sim.Players[1].Faction = "clnkr";
        var centre = sim.SpawnUnit("computer_centre", -1, 12, 12);
        var hacker = sim.SpawnUnit("tdo_hacker", 0, 8, 12);
        var victim = sim.SpawnUnit("clnkr_scout", 1, 20, 12);
        int victimCell = victim.CellX;
        sim.Enqueue(new SimCommand
        {
            ExecuteTick = 1,
            PlayerId = 0,
            Kind = CommandKind.Hack,
            UnitIds = new[] { hacker.Id },
            TargetId = centre.Id,
        });
        int opened = -1;
        int cellX = victim.CellX;
        int cellY = victim.CellY;
        int distracted = 0;
        for (int n = 0; n < 1400; n++)
        {
            sim.Step();
            if (opened < 0 && centre.AccessTicksLeft > 0)
                opened = sim.Tick;
            if (opened > 0 && sim.Tick == opened + 40)
            {
                cellX = victim.CellX;
                cellY = victim.CellY;
                distracted = victim.DistractedUntil;
            }
        }
        Assert.Equal(0, centre.PlayerId);
        int dx = cellX - victimCell;
        int dy = cellY - 12;
        Assert.True(dx * dx + dy * dy >= 4 || distracted > opened, $"scramble did not take, cell {cellX},{cellY} distracted {distracted}");
    }

    [Fact]
    public void HaulerRaisesCredits()
    {
        var sim = PrototypeScenario.Create(Load());
        int start = sim.Players[0].Credits;
        Step(sim, 2000);
        var hauler = sim.Entities.First(e => e.TypeId == "tdo_hauler");
        Assert.True(sim.Players[0].Credits > start,
            $"credits {sim.Players[0].Credits} start {start} haul {hauler.Haul} cargo {hauler.CargoUnits} cell {hauler.CellX},{hauler.CellY} buffer {BufferOf(sim, 0)} deposit {DepositOf(sim, "deposit_ore")}");
    }

    private static void EnqueuePush(Simulation sim)
    {
        int[] Ids(int player)
        {
            return sim.Entities
                .Where(e => e.Alive && e.PlayerId == player && e.Kind == EntityKind.Unit && e.Role != Roles.Hauler)
                .Select(e => e.Id)
                .ToArray();
        }

        sim.Enqueue(new SimCommand
        {
            ExecuteTick = 2,
            PlayerId = 0,
            Kind = CommandKind.AttackMove,
            UnitIds = Ids(0),
            X = WorldScale.CellCenter(70),
            Y = WorldScale.CellCenter(38),
        });
        sim.Enqueue(new SimCommand
        {
            ExecuteTick = 2,
            PlayerId = 1,
            Kind = CommandKind.AttackMove,
            UnitIds = Ids(1),
            X = WorldScale.CellCenter(70),
            Y = WorldScale.CellCenter(38),
        });
    }

    private static void AssertNear(List<SimEntity> units, int cellX, int cellY, string label)
    {
        foreach (var unit in units)
        {
            int dx = unit.CellX - cellX;
            int dy = unit.CellY - cellY;
            Assert.True(dx * dx + dy * dy <= 8, $"{label}: {unit.Id} at {unit.CellX},{unit.CellY} order {unit.Order} pending {unit.PathPending} wp {unit.Waypoint}/{unit.Path.Count}");
        }
    }

    private static void AssertSeparated(List<SimEntity> units)
    {
        long min = (long)(0.4 * WorldScale.TileWorldUnits * Fp.Scale);
        long min2 = min * min;
        for (int i = 0; i < units.Count; i++)
        {
            for (int j = i + 1; j < units.Count; j++)
            {
                long dist = WorldScale.DistanceSquared(units[i].X, units[i].Y, units[j].X, units[j].Y);
                Assert.True(dist >= min2, $"overlap {units[i].Id} and {units[j].Id}");
            }
        }
    }

    private static long GapToFootprint(SimEntity unit, SimEntity building)
    {
        long left = (long)building.CellX * WorldScale.TileWorldUnits * Fp.Scale;
        long right = (long)(building.CellX + building.Footprint) * WorldScale.TileWorldUnits * Fp.Scale;
        long top = (long)building.CellY * WorldScale.TileWorldUnits * Fp.Scale;
        long bottom = (long)(building.CellY + building.Footprint) * WorldScale.TileWorldUnits * Fp.Scale;
        long dx = 0;
        if (unit.X.Raw < left)
            dx = left - unit.X.Raw;
        else if (unit.X.Raw > right)
            dx = unit.X.Raw - right;
        long dy = 0;
        if (unit.Y.Raw < top)
            dy = top - unit.Y.Raw;
        else if (unit.Y.Raw > bottom)
            dy = unit.Y.Raw - bottom;
        return IntMath.Isqrt(dx * dx + dy * dy);
    }

    private static void Step(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            sim.Step();
    }

    private static int BufferOf(Simulation sim, int player)
    {
        return sim.Entities.Where(e => e.PlayerId == player && e.Role == Roles.Collector).Sum(e => e.Buffer);
    }

    private static int DepositOf(Simulation sim, string type)
    {
        return sim.Entities.Where(e => e.TypeId == type).Sum(e => e.DepositRemaining);
    }

    private static void Expect(DataCatalog data, string id, int cost, int hp, string speed, string? range, int damage, int salvo)
    {
        var unit = data.Require(id);
        Assert.Equal(cost, unit.Cost);
        Assert.Equal(hp, unit.Hp);
        Assert.Equal(Fp.Parse(speed).Raw, unit.Speed.Raw);
        if (range == null)
        {
            Assert.Empty(unit.Weapons);
            return;
        }
        Assert.Equal(Fp.Parse(range).Raw, unit.Weapons[0].Range.Raw);
        Assert.Equal(damage, unit.Weapons[0].Damage);
        Assert.Equal(salvo, unit.Weapons[0].Salvo);
    }
}
