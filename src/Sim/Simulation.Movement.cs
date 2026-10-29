namespace FirstClnkrWar.Sim;

public sealed partial class Simulation
{
    private void RequestPath(SimEntity entity, int gx, int gy)
    {
        RequestPath(entity, gx, gy, false, Fp.Zero, Fp.Zero);
    }

    private void RequestPath(SimEntity entity, int gx, int gy, bool exact, Fp exactX, Fp exactY)
    {
        if (!Map.InBounds(entity.CellX, entity.CellY))
            return;
        if (gx < 0)
            gx = 0;
        if (gy < 0)
            gy = 0;
        if (gx >= Map.Width)
            gx = Map.Width - 1;
        if (gy >= Map.Height)
            gy = Map.Height - 1;

        bool sameExact = !exact && !entity.HasExactGoal
            || exact && entity.HasExactGoal && entity.ExactX.Raw == exactX.Raw && entity.ExactY.Raw == exactY.Raw;
        if (sameExact && entity.PathPending && entity.GoalCellX == gx && entity.GoalCellY == gy)
            return;
        if (sameExact && !entity.PathPending && entity.GoalCellX == gx && entity.GoalCellY == gy && entity.Waypoint < entity.Path.Count)
            return;

        entity.GoalCellX = gx;
        entity.GoalCellY = gy;
        entity.HasExactGoal = exact;
        if (exact)
        {
            entity.ExactX = exactX;
            entity.ExactY = exactY;
        }
        entity.PathToken++;
        entity.PathPending = true;
        entity.Path.Clear();
        entity.Waypoint = 0;
        _paths.Enqueue(new PathRequest(entity.Id, entity.PathToken, gx, gy));
    }

    private void ProcessPathQueue()
    {
        int budget = Data.Rules.PathRequestsPerTick;
        while (budget-- > 0 && _paths.Count > 0)
        {
            var request = _paths.Dequeue();
            if (!_byId.TryGetValue(request.EntityId, out var entity))
                continue;
            if (!entity.Alive || entity.PathToken != request.Token)
                continue;
            var path = Pathfinder.Find(Map, entity.Domain, entity.CellX, entity.CellY, request.X, request.Y);
            if (entity.PathToken != request.Token)
                continue;
            entity.Path = path;
            entity.Waypoint = 0;
            entity.PathPending = false;
        }
    }

    private void IssueChasePaths()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var entity = _entities[i];
            if (!entity.Alive || entity.Kind != EntityKind.Unit || entity.Order != OrderKind.Attack)
                continue;
            if (!_byId.TryGetValue(entity.TargetId, out var target) || !target.Alive)
                continue;
            if (InAnyWeaponRange(entity, target))
                continue;
            if (entity.PathPending && entity.GoalCellX == target.CellX && entity.GoalCellY == target.CellY)
                continue;
            if (!entity.PathPending && entity.GoalCellX == target.CellX && entity.GoalCellY == target.CellY && entity.Waypoint < entity.Path.Count)
                continue;
            RequestPath(entity, target.CellX, target.CellY);
        }
    }

    private void TickMovement()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var entity = _entities[i];
            if (!entity.Alive || entity.Kind != EntityKind.Unit)
                continue;
            if (entity.PathPending)
                continue;
            if (entity.Order == OrderKind.Attack && _byId.TryGetValue(entity.TargetId, out var chase) && chase.Alive && InAnyWeaponRange(entity, chase))
                continue;
            if (entity.Order == OrderKind.AttackMove && _byId.TryGetValue(entity.TargetId, out var engaged) && engaged.Alive && InAnyWeaponRange(entity, engaged))
                continue;

            SkipOccupiedWaypoints(entity);
            if (entity.Waypoint >= entity.Path.Count)
                continue;

            var cell = entity.Path[entity.Waypoint];
            bool last = entity.Waypoint == entity.Path.Count - 1;
            Fp tx;
            Fp ty;
            if (last && entity.HasExactGoal)
            {
                tx = entity.ExactX;
                ty = entity.ExactY;
            }
            else
            {
                tx = WorldScale.CellCenter(cell.X);
                ty = WorldScale.CellCenter(cell.Y);
            }

            long step = BoostedSpeedRaw(Def(entity), entity.PlayerId);
            if (step < 0)
                step = 0;
            long dx = tx.Raw - entity.X.Raw;
            long dy = ty.Raw - entity.Y.Raw;
            long dist2 = dx * dx + dy * dy;
            if (step == 0 || dist2 <= step * step)
            {
                entity.X = tx;
                entity.Y = ty;
                entity.Waypoint++;
                ClampToMap(entity);
                SyncCell(entity);
                if (entity.Waypoint >= entity.Path.Count)
                    Arrive(entity);
            }
            else
            {
                long dist = IntMath.Isqrt(dist2);
                if (dist < 1)
                    dist = 1;
                entity.X = new Fp(entity.X.Raw + dx * step / dist);
                entity.Y = new Fp(entity.Y.Raw + dy * step / dist);
                ClampToMap(entity);
                SyncCell(entity);
            }
        }
    }

    /// <summary>
    /// A cell the unit is already standing in is done. Requiring the exact
    /// centre let separation hold a pair just outside one step of that centre,
    /// so waypoint 0 never advanced and later orders looked ignored.
    /// </summary>
    private void SkipOccupiedWaypoints(SimEntity entity)
    {
        while (entity.Waypoint < entity.Path.Count)
        {
            bool last = entity.Waypoint == entity.Path.Count - 1;
            if (last && entity.HasExactGoal)
                break;
            var cell = entity.Path[entity.Waypoint];
            if (entity.CellX != cell.X || entity.CellY != cell.Y)
                break;
            entity.Waypoint++;
        }
        if (entity.Waypoint >= entity.Path.Count && entity.Path.Count > 0)
            Arrive(entity);
    }

    private void Arrive(SimEntity entity)
    {
        entity.Path.Clear();
        entity.Waypoint = 0;
        entity.GoalCellX = -1;
        entity.GoalCellY = -1;
        entity.PathPending = false;
        entity.HasExactGoal = false;
        if (entity.Order is OrderKind.Move or OrderKind.AttackMove && entity.DistractedUntil <= Tick)
            entity.Order = OrderKind.Idle;
    }

    private void Separate()
    {
        long min = Data.Rules.SeparationTiles.Raw * WorldScale.TileWorldUnits;
        if (min < 1)
            return;
        long min2 = min * min;
        int count = _entities.Count;
        for (int i = 0; i < count; i++)
        {
            var a = _entities[i];
            if (!CanSeparate(a))
                continue;
            for (int j = i + 1; j < count; j++)
            {
                var b = _entities[j];
                if (!CanSeparate(b) || a.Domain != b.Domain)
                    continue;
                long dx = b.X.Raw - a.X.Raw;
                long dy = b.Y.Raw - a.Y.Raw;
                long dist2 = dx * dx + dy * dy;
                if (dist2 >= min2)
                    continue;
                if (dist2 == 0)
                {
                    a.X = new Fp(a.X.Raw - 1000);
                    b.X = new Fp(b.X.Raw + 1000);
                }
                else
                {
                    long dist = IntMath.Isqrt(dist2);
                    if (dist < 1)
                        dist = 1;
                    long push = (min - dist) / 2;
                    long ox = dx * push / dist;
                    long oy = dy * push / dist;
                    TryPush(a, -ox, -oy);
                    TryPush(b, ox, oy);
                }
                ClampToMap(a);
                ClampToMap(b);
                SyncCell(a);
                SyncCell(b);
            }
        }
    }

    private static bool CanSeparate(SimEntity entity)
    {
        return entity.Alive && entity.Kind == EntityKind.Unit;
    }

    private void TryPush(SimEntity entity, long ox, long oy)
    {
        long nx = entity.X.Raw + ox;
        long ny = entity.Y.Raw + oy;
        int cx = WorldScale.CellOf(new Fp(nx));
        int cy = WorldScale.CellOf(new Fp(ny));
        if (entity.Domain != UnitDomain.Air && !Map.Walkable(cx, cy, entity.Domain) && (cx != entity.CellX || cy != entity.CellY))
            return;
        entity.X = new Fp(nx);
        entity.Y = new Fp(ny);
    }
}
