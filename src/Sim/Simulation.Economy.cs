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
            if (!_byId.TryGetValue(hauler.LinkId, out var site) || !site.Alive || !StillDocked(hauler, site))
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
            if (!_byId.TryGetValue(hauler.HomeId, out var home) || !home.Alive || !StillDocked(hauler, home))
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
        if (!TryDock(building.CellX, building.CellY, building.Footprint, hauler, out int ax, out int ay, out Fp dockX, out Fp dockY))
            return;
        long dockRadius = hauler.Haul == arrive ? DockContinueRaw : DockArriveRaw;
        if (CloseTo(hauler, dockX, dockY, dockRadius))
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
            hauler.HasExactGoal = false;
            return;
        }

        if (hauler.Haul != phase)
        {
            hauler.Haul = phase;
            hauler.RateAcc = 0;
        }

        RequestPath(hauler, ax, ay, true, dockX, dockY);
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

    private const long DockStandOffRaw = 160L * Fp.Scale;
    // Tight radius so economy does not cancel the approach while the hauler
    // is still a large fraction of a tile away from the dock point.
    private const long DockArriveRaw = 48L * Fp.Scale;
    private const long DockContinueRaw = 96L * Fp.Scale;

    private static bool CloseTo(SimEntity unit, Fp x, Fp y, long radiusRaw)
    {
        long dx = unit.X.Raw - x.Raw;
        long dy = unit.Y.Raw - y.Raw;
        return dx * dx + dy * dy <= radiusRaw * radiusRaw;
    }

    private bool StillDocked(SimEntity unit, SimEntity building)
    {
        return TryDock(building.CellX, building.CellY, building.Footprint, unit, out _, out _, out Fp dockX, out Fp dockY)
            && CloseTo(unit, dockX, dockY, DockContinueRaw);
    }

    private bool TryDock(int originX, int originY, int footprint, SimEntity unit, out int cellX, out int cellY, out Fp pointX, out Fp pointY)
    {
        bool found = false;
        long best = long.MaxValue;
        int bestX = originX;
        int bestY = originY;
        Fp bestPx = Fp.Zero;
        Fp bestPy = Fp.Zero;
        int f = Math.Max(1, footprint);
        for (int i = 0; i < f; i++)
        {
            Consider(originX - 1, originY + i, -1, 0);
            Consider(originX + f, originY + i, 1, 0);
            Consider(originX + i, originY - 1, 0, -1);
            Consider(originX + i, originY + f, 0, 1);
        }

        cellX = bestX;
        cellY = bestY;
        pointX = bestPx;
        pointY = bestPy;
        return found;

        void Consider(int x, int y, int nx, int ny)
        {
            if (!Map.Walkable(x, y, unit.Domain))
                return;
            long edgeX;
            long edgeY;
            if (nx < 0)
            {
                edgeX = (long)originX * WorldScale.TileWorldUnits * Fp.Scale - DockStandOffRaw;
                edgeY = WorldScale.CellCenter(y).Raw;
            }
            else if (nx > 0)
            {
                edgeX = (long)(originX + f) * WorldScale.TileWorldUnits * Fp.Scale + DockStandOffRaw;
                edgeY = WorldScale.CellCenter(y).Raw;
            }
            else if (ny < 0)
            {
                edgeY = (long)originY * WorldScale.TileWorldUnits * Fp.Scale - DockStandOffRaw;
                edgeX = WorldScale.CellCenter(x).Raw;
            }
            else
            {
                edgeY = (long)(originY + f) * WorldScale.TileWorldUnits * Fp.Scale + DockStandOffRaw;
                edgeX = WorldScale.CellCenter(x).Raw;
            }

            long dx = edgeX - unit.X.Raw;
            long dy = edgeY - unit.Y.Raw;
            long score = dx * dx + dy * dy;
            if (found && (score > best || score == best && (y > bestY || y == bestY && x >= bestX)))
                return;
            found = true;
            best = score;
            bestX = x;
            bestY = y;
            bestPx = new Fp(edgeX);
            bestPy = new Fp(edgeY);
        }
    }
}
