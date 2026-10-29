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
        Assert.Equal(30, data.Require("tdo_gunship").Weapons[1].Damage);

        Expect(data, "clnkr_warhorse", 1000, 140, "2.15", null, 0, 1);
        Expect(data, "clnkr_scout", 750, 30, "5.66", "4", 1, 4);
        Expect(data, "clnkr_mk1", 1300, 81, "3.01", "8", 5, 1);
        Assert.Equal(2, data.Require("clnkr_mk1").Weapons[0].DamageTroop);
        Expect(data, "clnkr_assault_laser", 2500, 180, "3.01", "6", 12, 1);
        Assert.Equal(9, data.Require("clnkr_assault_laser").Weapons[1].Damage);
        Expect(data, "clnkr_hauler", 500, 100, "1.72", null, 0, 1);
        Expect(data, "clnkr_bird", 3500, 68, "4.30", "16", 12, 1);

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
    public void HaulerRaisesCredits()
    {
        var sim = PrototypeScenario.Create(Load());
        int start = sim.Players[0].Credits;
        Step(sim, 900);
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
