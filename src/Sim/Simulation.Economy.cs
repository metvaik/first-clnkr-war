namespace FirstClnkrWar.Sim;

public sealed partial class Simulation
{
    private void TickEconomy()
    {
        int tick = Data.Rules.TickHz;
        for (int i = 0; i < _entities.Count; i++)
        {
            var site = _entities[i];
            if (!site.Alive || site.Role != Roles.Collector)
                continue;
            if (!_byId.TryGetValue(site.LinkId, out var deposit) || deposit.DepositRemaining <= 0)
                continue;
            var def = Def(site);
            int bufferCap = def.BufferCap > 0 ? def.BufferCap : Data.Rules.SiteBuffer;
            if (site.Buffer >= bufferCap)
                continue;
            site.RateAcc += Data.Rules.MiningRatePerSecond;
            int units = site.RateAcc / tick;
            site.RateAcc %= tick;
            int room = bufferCap - site.Buffer;
            if (units > room)
                units = room;
            if (units > deposit.DepositRemaining)
                units = deposit.DepositRemaining;
            site.Buffer += units;
            deposit.DepositRemaining -= units;
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            var hauler = _entities[i];
            if (!hauler.Alive || hauler.Role != Roles.Hauler)
                continue;
            if (hauler.Order is OrderKind.Move or OrderKind.Attack or OrderKind.AttackMove)
            {
                hauler.Haul = HaulPhase.Manual;
                continue;
            }
            if (hauler.Haul == HaulPhase.Manual)
                hauler.Haul = HaulPhase.Auto;
            TickHauler(hauler);
        }
    }

    private void TickHauler(SimEntity hauler)
    {
        int capacity = Def(hauler).Cargo;
        if (capacity <= 0)
            capacity = Data.Rules.HaulerCapacity;

        if (hauler.Haul is HaulPhase.Loading)
        {
            if (!_byId.TryGetValue(hauler.LinkId, out var site) || !site.Alive || !NearBuilding(hauler, site))
            {
                hauler.Haul = HaulPhase.Auto;
            }
            else
            {
                int each = CargoEach(site);
                bool sourceLeft = site.Buffer > 0 || DepositLeft(site);
                if (hauler.CargoUnits > 0 && hauler.CargoValueEach != each)
                {
                    hauler.Haul = HaulPhase.ToRefinery;
                }
                else if (hauler.CargoUnits >= capacity || (site.Buffer <= 0 && !sourceLeft && hauler.CargoUnits > 0))
                {
                    hauler.Haul = HaulPhase.ToRefinery;
                }
                else if (site.Buffer > 0 && hauler.CargoUnits < capacity)
                {
                    hauler.CargoValueEach = each;
                    hauler.RateAcc += Data.Rules.LoadUnitsPerSecond;
                    int units = hauler.RateAcc / Data.Rules.TickHz;
                    hauler.RateAcc %= Data.Rules.TickHz;
                    int space = capacity - hauler.CargoUnits;
                    if (units > space)
                        units = space;
                    if (units > site.Buffer)
                        units = site.Buffer;
                    site.Buffer -= units;
                    hauler.CargoUnits += units;
                }
                return;
            }
        }

        if (hauler.Haul is HaulPhase.Unloading)
        {
            if (!_byId.TryGetValue(hauler.HomeId, out var home) || !home.Alive || !NearBuilding(hauler, home))
            {
                hauler.Haul = HaulPhase.ToRefinery;
            }
            else if (hauler.CargoUnits <= 0)
            {
                hauler.Haul = HaulPhase.Auto;
                hauler.CargoValueEach = 1;
                hauler.CreditBank = 0;
            }
            else
            {
                Unload(hauler);
                return;
            }
        }

        if (hauler.CargoUnits > 0)
        {
            var home = ResolveHome(hauler);
            if (home != null)
                GoToBuilding(hauler, home, HaulPhase.ToRefinery);
            return;
        }

        var sitePick = NearestSite(hauler);
        if (sitePick == null)
        {
            hauler.Haul = HaulPhase.Auto;
            return;
        }
        hauler.LinkId = sitePick.Id;
        GoToBuilding(hauler, sitePick, HaulPhase.ToSite);
    }

    private void Unload(SimEntity hauler)
    {
        if ((uint)hauler.PlayerId >= (uint)Players.Length)
            return;
        var player = Players[hauler.PlayerId];
        int cap = StorageCap(hauler.PlayerId);
        int each = hauler.CargoValueEach < 1 ? 1 : hauler.CargoValueEach;
        int tick = Data.Rules.TickHz;
        hauler.RateAcc += Data.Rules.UnloadCreditsPerSecond;
        int guard = 0;
        while (hauler.RateAcc >= tick && hauler.CargoUnits > 0 && player.Credits < cap && guard++ < 100000)
        {
            if (hauler.CreditBank + 1 >= each && player.Credits + each > cap)
                break;
            hauler.RateAcc -= tick;
            hauler.CreditBank++;
            if (hauler.CreditBank >= each)
            {
                hauler.CreditBank = 0;
                hauler.CargoUnits--;
                player.Credits += each;
            }
        }
    }

    private void GoToBuilding(SimEntity hauler, SimEntity building, HaulPhase phase)
    {
        HaulPhase arrive = phase == HaulPhase.ToSite ? HaulPhase.Loading : HaulPhase.Unloading;
        if (NearBuilding(hauler, building))
        {
            if (hauler.Haul != arrive)
            {
                hauler.RateAcc = 0;
                hauler.CreditBank = 0;
            }
            hauler.Haul = arrive;
            hauler.Path.Clear();
            hauler.Waypoint = 0;
            hauler.PathPending = false;
            hauler.PathToken++;
            hauler.GoalCellX = -1;
            hauler.GoalCellY = -1;
            return;
        }

        if (hauler.Haul != phase)
        {
            hauler.Haul = phase;
            hauler.RateAcc = 0;
        }

        if (!TryAdjacent(building, hauler, out int ax, out int ay))
            return;
        RequestPath(hauler, ax, ay);
    }

    private SimEntity? ResolveHome(SimEntity hauler)
    {
        if (_byId.TryGetValue(hauler.HomeId, out var home) && home.Alive && home.Role == Roles.Refinery && home.PlayerId == hauler.PlayerId)
            return home;
        SimEntity? best = null;
        long bestDist = long.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.Role != Roles.Refinery || e.PlayerId != hauler.PlayerId)
                continue;
            long d = WorldScale.DistanceSquared(hauler.X, hauler.Y, e.X, e.Y);
            if (best == null || d < bestDist || (d == bestDist && e.Id < best.Id))
            {
                best = e;
                bestDist = d;
            }
        }
        if (best != null)
            hauler.HomeId = best.Id;
        return best;
    }

    private SimEntity? NearestSite(SimEntity hauler)
    {
        SimEntity? best = null;
        long bestDist = long.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.Role != Roles.Collector || e.PlayerId != hauler.PlayerId)
                continue;
            if (e.Buffer <= 0 && !DepositLeft(e))
                continue;
            long d = WorldScale.DistanceSquared(hauler.X, hauler.Y, e.X, e.Y);
            if (best == null || d < bestDist || (d == bestDist && e.Id < best.Id))
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    private bool DepositLeft(SimEntity site)
    {
        return _byId.TryGetValue(site.LinkId, out var deposit) && deposit.DepositRemaining > 0;
    }

    private int CargoEach(SimEntity site)
    {
        if (_byId.TryGetValue(site.LinkId, out var deposit) && deposit.Rare)
            return Math.Max(1, Data.Rules.RareEarthMultiplier);
        return 1;
    }

    private static bool NearBuilding(SimEntity unit, SimEntity building)
    {
        int minX = building.CellX - 1;
        int maxX = building.CellX + building.Footprint;
        int minY = building.CellY - 1;
        int maxY = building.CellY + building.Footprint;
        return unit.CellX >= minX && unit.CellX <= maxX && unit.CellY >= minY && unit.CellY <= maxY;
    }

    private bool TryAdjacent(SimEntity building, SimEntity unit, out int ax, out int ay)
    {
        bool found = false;
        int best = int.MaxValue;
        ax = building.CellX;
        ay = building.CellY;
        int f = building.Footprint;
        for (int y = building.CellY - 1; y <= building.CellY + f; y++)
        {
            for (int x = building.CellX - 1; x <= building.CellX + f; x++)
            {
                bool inside = x >= building.CellX && x < building.CellX + f && y >= building.CellY && y < building.CellY + f;
                if (inside || !Map.Walkable(x, y, unit.Domain))
                    continue;
                int dx = x - unit.CellX;
                int dy = y - unit.CellY;
                int d = dx * dx + dy * dy;
                if (!found || d < best || (d == best && (y < ay || (y == ay && x < ax))))
                {
                    found = true;
                    best = d;
                    ax = x;
                    ay = y;
                }
            }
        }
        return found;
    }
}
