namespace FirstClnkrWar.Sim;

public sealed partial class Simulation
{
    private void TickSpecial()
    {
        ExpireDistractions();
        TickConstruction();
        TickHackSites();
    }

    private void ExpireDistractions()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var entity = _entities[i];
            if (!entity.Alive || entity.DistractedUntil <= 0 || Tick < entity.DistractedUntil)
                continue;
            entity.DistractedUntil = 0;
            entity.Order = OrderKind.Idle;
            entity.TargetId = 0;
            entity.SalvoLeft = 0;
            ClearPath(entity);
            if (entity.Role == Roles.Hauler)
                entity.Haul = HaulPhase.Auto;
        }
    }

    private void TickConstruction()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var builder = _entities[i];
            if (!builder.Alive || builder.Work != WorkKind.Build)
                continue;
            if (!builder.HasExactGoal && builder.Path.Count == 0 && !CloseTo(builder, builder.ExactX, builder.ExactY, DockContinueRaw))
            {
                if (TryDock(builder.BuildOriginX, builder.BuildOriginY, FootprintOf(builder.BuildTypeId), builder, out int cx, out int cy, out Fp dx, out Fp dy))
                    RequestPath(builder, cx, cy, true, dx, dy);
                continue;
            }
            if (!CloseTo(builder, builder.ExactX, builder.ExactY, DockContinueRaw))
                continue;
            builder.WorkTicks++;
            if (builder.WorkTicks < builder.WorkGoal)
                continue;
            SpawnUnit(builder.BuildTypeId, builder.PlayerId, builder.BuildOriginX, builder.BuildOriginY);
            builder.Work = WorkKind.None;
            builder.WorkTicks = 0;
            builder.WorkGoal = 0;
            builder.BuildCost = 0;
            builder.BuildTypeId = "";
            builder.HasExactGoal = false;
        }
    }

    private void TickHackSites()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var hacker = _entities[i];
            if (!hacker.Alive || hacker.Role != Roles.Hacker || hacker.LinkId == 0)
                continue;
            if (hacker.Work == WorkKind.None && hacker.AccessTicksLeft <= 0)
                continue;
            if (!_byId.TryGetValue(hacker.LinkId, out var centre) || !centre.Alive || centre.Role != Roles.ComputerCentre)
            {
                ClearHack(hacker, null);
                continue;
            }

            if (!TryDock(centre.CellX, centre.CellY, centre.Footprint, hacker, out int cx, out int cy, out Fp dockX, out Fp dockY))
                continue;
            if (!CloseTo(hacker, dockX, dockY, DockContinueRaw))
            {
                if (hacker.Work != WorkKind.None)
                    RequestPath(hacker, cx, cy, true, dockX, dockY);
                MirrorHack(hacker, centre);
                continue;
            }

            if (centre.PlayerId != hacker.PlayerId)
            {
                if (hacker.Work != WorkKind.Capture)
                {
                    hacker.Work = WorkKind.Capture;
                    hacker.WorkTicks = 0;
                    hacker.WorkGoal = SecondsToTicks(Data.Rules.CaptureSeconds);
                    hacker.AccessTicksLeft = 0;
                }
                hacker.WorkTicks++;
                if (hacker.WorkTicks >= hacker.WorkGoal)
                {
                    centre.PlayerId = hacker.PlayerId;
                    centre.OperatorId = hacker.Id;
                    hacker.Work = WorkKind.Infiltrate;
                    hacker.WorkTicks = 0;
                    hacker.WorkGoal = SecondsToTicks(Data.Rules.InfiltrateSeconds);
                }
            }
            else if (hacker.AccessTicksLeft > 0)
            {
                TickAccess(hacker, centre);
            }
            else
            {
                if (hacker.Work != WorkKind.Infiltrate)
                {
                    hacker.Work = WorkKind.Infiltrate;
                    hacker.WorkTicks = 0;
                    hacker.WorkGoal = SecondsToTicks(Data.Rules.InfiltrateSeconds);
                }
                hacker.WorkTicks++;
                if (hacker.WorkTicks >= hacker.WorkGoal)
                {
                    hacker.Work = WorkKind.None;
                    hacker.WorkTicks = 0;
                    hacker.WorkGoal = 0;
                    hacker.AccessWindow = SecondsToTicks(Data.Rules.AccessWindowSeconds);
                    hacker.AccessTicksLeft = hacker.AccessWindow;
                    hacker.AbilityCooldown = 0;
                    centre.OperatorId = hacker.Id;
                }
            }

            MirrorHack(hacker, centre);
        }
    }

    private void TickAccess(SimEntity hacker, SimEntity centre)
    {
        if (hacker.AbilityCooldown > 0)
            hacker.AbilityCooldown--;
        else if (TryScramble(hacker))
            hacker.AbilityCooldown = SecondsToTicks(Data.Rules.AbilityCooldownSeconds);
        hacker.AccessTicksLeft--;
        if (hacker.AccessTicksLeft <= 0)
        {
            hacker.AccessTicksLeft = 0;
            hacker.AccessWindow = 0;
            hacker.AbilityCooldown = 0;
            hacker.Work = WorkKind.None;
        }
        centre.OperatorId = hacker.Id;
    }

    private bool TryScramble(SimEntity hacker)
    {
        SimEntity? target = null;
        int seen = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var other = _entities[i];
            if (!other.Alive || other.Kind != EntityKind.Unit || other.PlayerId < 0 || other.PlayerId == hacker.PlayerId)
                continue;
            seen++;
            int pick = PositiveMod(Tick * 17 + hacker.Id * 3, seen);
            if (pick == 0)
                target = other;
        }
        if (target == null)
            return false;

        int span = Data.Rules.ScrambleMaxTiles - Data.Rules.ScrambleMinTiles + 1;
        int tiles = Data.Rules.ScrambleMinTiles + PositiveMod(Tick * 13 + target.Id * 5, span);
        int dir = PositiveMod(Tick + target.Id * 9, 8);
        int dx = DirX[dir] * tiles;
        int dy = DirY[dir] * tiles;
        target.DistractedUntil = Tick + SecondsToTicks(Data.Rules.ScrambleSeconds);
        target.Order = OrderKind.Move;
        target.TargetId = 0;
        target.SalvoLeft = 0;
        target.Work = WorkKind.None;
        if (target.Role == Roles.Hauler)
            target.Haul = HaulPhase.Manual;
        RequestPath(target, target.CellX + dx, target.CellY + dy);
        return true;
    }

    private static readonly int[] DirX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] DirY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    private static int PositiveMod(int value, int mod)
    {
        if (mod <= 1)
            return 0;
        int r = value % mod;
        return r < 0 ? r + mod : r;
    }

    private void MirrorHack(SimEntity hacker, SimEntity centre)
    {
        centre.Work = hacker.Work;
        centre.WorkTicks = hacker.WorkTicks;
        centre.WorkGoal = hacker.WorkGoal;
        centre.AccessTicksLeft = hacker.AccessTicksLeft;
        centre.AccessWindow = hacker.AccessWindow;
        centre.AbilityCooldown = hacker.AbilityCooldown;
        centre.OperatorId = hacker.Id;
    }

    private void ClearHack(SimEntity hacker, SimEntity? centre)
    {
        hacker.Work = WorkKind.None;
        hacker.WorkTicks = 0;
        hacker.WorkGoal = 0;
        hacker.AccessTicksLeft = 0;
        hacker.LinkId = 0;
        if (centre != null && centre.OperatorId == hacker.Id)
        {
            centre.Work = WorkKind.None;
            centre.WorkTicks = 0;
            centre.WorkGoal = 0;
            centre.AccessTicksLeft = 0;
            centre.OperatorId = 0;
        }
    }

    private bool TryBeginBuild(SimEntity builder, SimCommand command)
    {
        if (string.IsNullOrEmpty(command.BuildingTypeId) || !Data.Units.TryGetValue(command.BuildingTypeId, out var def) || !def.IsBuilding)
            return false;
        if ((uint)builder.PlayerId >= (uint)Players.Length)
            return false;
        int cost = def.Cost;
        if (Players[builder.PlayerId].Credits < cost)
            return false;
        int originX = WorldScale.CellOf(command.X);
        int originY = WorldScale.CellOf(command.Y);
        if (!FootprintClear(originX, originY, def.Footprint))
            return false;
        if (!TryDock(originX, originY, def.Footprint, builder, out int cx, out int cy, out Fp dockX, out Fp dockY))
            return false;

        Players[builder.PlayerId].Credits -= cost;
        int seconds = def.BuildSeconds > 0 ? def.BuildSeconds : Data.Rules.DefaultBuildSeconds;
        builder.Work = WorkKind.Build;
        builder.WorkTicks = 0;
        builder.WorkGoal = SecondsToTicks(Fp.FromInt(seconds));
        builder.BuildTypeId = def.Id;
        builder.BuildCost = cost;
        builder.BuildOriginX = originX;
        builder.BuildOriginY = originY;
        builder.Order = OrderKind.Move;
        builder.TargetId = 0;
        builder.ExactX = dockX;
        builder.ExactY = dockY;
        RequestPath(builder, cx, cy, true, dockX, dockY);
        return true;
    }

    private void TryBeginHack(SimEntity hacker, SimCommand command)
    {
        if (!_byId.TryGetValue(command.TargetId, out var centre) || !centre.Alive || centre.Role != Roles.ComputerCentre)
            return;
        if (!TryDock(centre.CellX, centre.CellY, centre.Footprint, hacker, out int cx, out int cy, out Fp dockX, out Fp dockY))
            return;
        hacker.LinkId = centre.Id;
        hacker.Order = OrderKind.Move;
        hacker.TargetId = 0;
        hacker.AccessTicksLeft = 0;
        if (centre.PlayerId == hacker.PlayerId)
        {
            hacker.Work = WorkKind.Infiltrate;
            hacker.WorkGoal = SecondsToTicks(Data.Rules.InfiltrateSeconds);
        }
        else
        {
            hacker.Work = WorkKind.Capture;
            hacker.WorkGoal = SecondsToTicks(Data.Rules.CaptureSeconds);
        }
        hacker.WorkTicks = 0;
        hacker.ExactX = dockX;
        hacker.ExactY = dockY;
        RequestPath(hacker, cx, cy, true, dockX, dockY);
        MirrorHack(hacker, centre);
    }

    private void CancelWork(SimEntity entity)
    {
        if (entity.Work == WorkKind.Build && entity.BuildCost > 0 && (uint)entity.PlayerId < (uint)Players.Length)
            Players[entity.PlayerId].Credits += entity.BuildCost;
        if (entity.Role == Roles.Hacker && entity.LinkId != 0 && _byId.TryGetValue(entity.LinkId, out var centre))
            ClearHack(entity, centre);
        entity.Work = WorkKind.None;
        entity.WorkTicks = 0;
        entity.WorkGoal = 0;
        entity.BuildCost = 0;
        entity.BuildTypeId = "";
        entity.AccessTicksLeft = 0;
        entity.AccessWindow = 0;
        entity.LinkId = entity.Role == Roles.Hauler ? entity.LinkId : 0;
    }

    private int FootprintOf(string typeId)
    {
        return Data.Units.TryGetValue(typeId, out var def) ? def.Footprint : 1;
    }

    private bool FootprintClear(int originX, int originY, int footprint)
    {
        for (int y = 0; y < footprint; y++)
        {
            for (int x = 0; x < footprint; x++)
            {
                int cx = originX + x;
                int cy = originY + y;
                if (!Map.InBounds(cx, cy))
                    return false;
                int i = Map.Index(cx, cy);
                if (Map.Terrain[i] != TerrainKind.Land || Map.BlockedBy[i] != 0)
                    return false;
            }
        }
        return true;
    }
}
