namespace FirstClnkrWar.Sim;

public sealed partial class Simulation
{
    private void RequestPath(SimEntity entity, int gx, int gy)
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

        if (entity.PathPending && entity.GoalCellX == gx && entity.GoalCellY == gy)
            return;
        if (!entity.PathPending && entity.GoalCellX == gx && entity.GoalCellY == gy && entity.Waypoint < entity.Path.Count)
            return;

        entity.GoalCellX = gx;
        entity.GoalCellY = gy;
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
            if (entity.PathPending || entity.Waypoint >= entity.Path.Count)
                continue;
            if (entity.Order == OrderKind.Attack && _byId.TryGetValue(entity.TargetId, out var chase) && chase.Alive && InAnyWeaponRange(entity, chase))
                continue;
            if (entity.Order == OrderKind.AttackMove && _byId.TryGetValue(entity.TargetId, out var engaged) && engaged.Alive && InAnyWeaponRange(entity, engaged))
                continue;

            var def = Def(entity);
            long step = BoostedSpeedRaw(def, entity.PlayerId);
            if (step < 0)
                step = 0;

            var cell = entity.Path[entity.Waypoint];
            Fp tx = WorldScale.CellCenter(cell.X);
            Fp ty = WorldScale.CellCenter(cell.Y);
            long dx = tx.Raw - entity.X.Raw;
            long dy = ty.Raw - entity.Y.Raw;
            long dist2 = dx * dx + dy * dy;
            if (step == 0 || dist2 <= step * step)
            {
                entity.X = tx;
                entity.Y = ty;
                entity.Waypoint++;
                if (entity.Waypoint >= entity.Path.Count)
                {
                    entity.Path.Clear();
                    entity.Waypoint = 0;
                    entity.GoalCellX = -1;
                    entity.GoalCellY = -1;
                    if (entity.Order is OrderKind.Move or OrderKind.AttackMove)
                        entity.Order = OrderKind.Idle;
                }
            }
            else
            {
                long dist = IntMath.Isqrt(dist2);
                if (dist < 1)
                    dist = 1;
                entity.X = new Fp(entity.X.Raw + dx * step / dist);
                entity.Y = new Fp(entity.Y.Raw + dy * step / dist);
            }

            ClampToMap(entity);
            SyncCell(entity);
        }
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
