namespace FirstClnkrWar.Sim;

public sealed partial class Simulation
{
    private readonly List<SimEntity> _entities = new();
    private readonly Dictionary<int, SimEntity> _byId = new();
    private readonly List<SimCommand> _commands = new();
    private readonly Queue<PathRequest> _paths = new();
    private int _nextId = 1;
    private int _sequence;
    private int _projectileCount;

    public int Tick { get; private set; }
    public DataCatalog Data { get; }
    public SimMap Map { get; }
    public PlayerState[] Players { get; }
    public byte[][] Fog { get; }
    public IReadOnlyList<SimEntity> Entities => _entities;

    public Simulation(DataCatalog data, SimMap map, int playerCount)
    {
        Data = data;
        Map = map;
        if (playerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        Players = new PlayerState[playerCount];
        Fog = new byte[playerCount][];
        int cells = map.Width * map.Height;
        for (int i = 0; i < playerCount; i++)
        {
            Players[i] = new PlayerState
            {
                Id = i,
                Credits = data.Rules.StartingCredits,
                Firepower = data.Rules.StartingFirepower,
                Armour = data.Rules.StartingArmour,
                SpeedLevel = data.Rules.StartingSpeed,
            };
            Fog[i] = new byte[cells];
        }
    }

    public SimEntity? Find(int id) => _byId.TryGetValue(id, out var e) ? e : null;

    public UnitDef Def(SimEntity entity) => Data.Require(entity.TypeId);

    public int StorageCap(int playerId)
    {
        int cap = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.PlayerId != playerId || e.Role != Roles.Refinery)
                continue;
            var def = Def(e);
            cap += def.StorageCap > 0 ? def.StorageCap : Data.Rules.RefineryStorageCap;
        }
        return cap;
    }

    public SimEntity SpawnUnit(string typeId, int playerId, int cellX, int cellY)
    {
        var def = Data.Require(typeId);
        if (def.Footprint > 1)
        {
            if (cellX < 0 || cellY < 0 || cellX + def.Footprint > Map.Width || cellY + def.Footprint > Map.Height)
                throw new InvalidOperationException($"Footprint of '{typeId}' does not fit at {cellX},{cellY}.");
        }
        else if (!Map.InBounds(cellX, cellY))
        {
            throw new InvalidOperationException($"Cell {cellX},{cellY} is outside the map.");
        }

        var entity = new SimEntity
        {
            Id = _nextId++,
            Kind = def.IsBuilding ? EntityKind.Building : EntityKind.Unit,
            PlayerId = playerId,
            TypeId = def.Id,
            Domain = def.Domain,
            Role = def.Role,
            Shape = def.Shape,
            Hp = def.Hp,
            MaxHp = def.Hp,
            Alive = true,
            Footprint = def.Footprint,
            CellX = cellX,
            CellY = cellY,
            WeaponCooldown = new int[Math.Max(1, def.Weapons.Count)],
            CargoValueEach = 1,
            Haul = def.Role == Roles.Hauler ? HaulPhase.Auto : HaulPhase.Manual,
        };
        PlaceOnFootprint(entity, cellX, cellY, def.Footprint);
        if (def.IsBuilding)
            BlockFootprint(entity);
        _entities.Add(entity);
        _byId[entity.Id] = entity;
        return entity;
    }

    public SimEntity SpawnDeposit(int cellX, int cellY, int amount, bool rare)
    {
        var entity = new SimEntity
        {
            Id = _nextId++,
            Kind = EntityKind.Deposit,
            PlayerId = -1,
            TypeId = rare ? "deposit_rare" : "deposit_ore",
            Role = "deposit",
            Shape = "deposit",
            Domain = UnitDomain.Ground,
            Hp = 1,
            MaxHp = 1,
            Alive = true,
            CellX = cellX,
            CellY = cellY,
            DepositRemaining = amount,
            Rare = rare,
            Footprint = 1,
        };
        PlaceOnFootprint(entity, cellX, cellY, 1);
        _entities.Add(entity);
        _byId[entity.Id] = entity;
        return entity;
    }

    public void Enqueue(SimCommand command)
    {
        var ids = command.UnitIds ?? Array.Empty<int>();
        var copy = new int[ids.Length];
        Array.Copy(ids, copy, ids.Length);
        Array.Sort(copy);
        command.UnitIds = copy;
        command.Sequence = ++_sequence;
        if (command.ExecuteTick < Tick)
            command.ExecuteTick = Tick;
        _commands.Add(command);
    }

    public void Step()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            e.PrevX = e.X;
            e.PrevY = e.Y;
        }

        ExecuteCommands();
        TickEconomy();
        IssueChasePaths();
        ProcessPathQueue();
        TickMovement();
        Separate();
        TickSpecial();
        if (Tick % Data.Rules.FogUpdateIntervalTicks == 0)
            UpdateFog();
        TickProjectiles();
        TickWeapons();
        RemoveDeadProjectiles();
        Tick++;
    }

    public void UpdateFog()
    {
        int intervalCells = Map.Width * Map.Height;
        for (int p = 0; p < Players.Length; p++)
        {
            var fog = Fog[p];
            for (int i = 0; i < intervalCells; i++)
            {
                if (fog[i] == 2)
                    fog[i] = 1;
            }
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            var viewer = _entities[i];
            if (!viewer.Alive || viewer.PlayerId < 0 || viewer.Kind == EntityKind.Projectile || viewer.Kind == EntityKind.Deposit)
                continue;
            if (viewer.TypeId.Length == 0 || !Data.Units.ContainsKey(viewer.TypeId))
                continue;
            RevealCircle(viewer, SightOf(Def(viewer), null));
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            var target = _entities[i];
            if (!target.Alive || target.PlayerId < 0 || target.Kind == EntityKind.Projectile || target.Kind == EntityKind.Deposit)
                continue;
            for (int v = 0; v < _entities.Count; v++)
            {
                var viewer = _entities[v];
                if (!viewer.Alive || viewer.PlayerId < 0 || viewer.PlayerId == target.PlayerId)
                    continue;
                if (viewer.Kind == EntityKind.Projectile || viewer.Kind == EntityKind.Deposit)
                    continue;
                if (viewer.TypeId.Length == 0 || !Data.Units.ContainsKey(viewer.TypeId))
                    continue;
                var def = Def(viewer);
                Fp sight = SightOf(def, target.Domain);
                Fp normal = SightOf(def, null);
                if (sight.Raw <= normal.Raw)
                    continue;
                if (WorldScale.WithinTiles(viewer.X, viewer.Y, target.X, target.Y, sight))
                    Fog[viewer.PlayerId][Map.Index(target.CellX, target.CellY)] = 2;
            }
        }
    }

    public bool CellVisible(int playerId, int x, int y)
    {
        if ((uint)playerId >= (uint)Players.Length || !Map.InBounds(x, y))
            return false;
        return Fog[playerId][Map.Index(x, y)] == 2;
    }

    public bool CanSee(int playerId, SimEntity target)
    {
        if (target.PlayerId == playerId)
            return true;
        return CellVisible(playerId, target.CellX, target.CellY);
    }

    public ulong Hash()
    {
        var hash = new Fnv64();
        hash.Add(Tick);
        for (int i = 0; i < Players.Length; i++)
        {
            var p = Players[i];
            hash.Add(p.Id);
            hash.Add(p.Credits);
            hash.Add(p.Firepower);
            hash.Add(p.Armour);
            hash.Add(p.SpeedLevel);
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            hash.Add(e.Id);
            hash.Add((int)e.Kind);
            hash.Add(e.Alive ? 1 : 0);
            hash.Add(e.Hp);
            hash.Add(e.X.Raw);
            hash.Add(e.Y.Raw);
            hash.Add(e.CargoUnits);
            hash.Add(e.CargoValueEach);
            hash.Add(e.Buffer);
            hash.Add(e.DepositRemaining);
            hash.Add((int)e.Order);
            hash.Add((int)e.Haul);
            hash.Add(e.TargetId);
            hash.Add(e.CellX);
            hash.Add(e.CellY);
            hash.Add((int)e.Work);
            hash.Add(e.WorkTicks);
            hash.Add(e.DistractedUntil);
            hash.Add(e.AccessTicksLeft);
        }

        for (int p = 0; p < Fog.Length; p++)
            hash.Add(Fog[p]);
        return hash.Value;
    }

    private void ExecuteCommands()
    {
        if (_commands.Count > 1)
        {
            _commands.Sort(static (a, b) =>
            {
                int c = a.ExecuteTick.CompareTo(b.ExecuteTick);
                return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
            });
        }

        int kept = 0;
        for (int i = 0; i < _commands.Count; i++)
        {
            var command = _commands[i];
            if (command.ExecuteTick > Tick)
            {
                _commands[kept++] = command;
                continue;
            }
            Apply(command);
        }
        if (kept < _commands.Count)
            _commands.RemoveRange(kept, _commands.Count - kept);
    }

    private void Apply(SimCommand command)
    {
        if ((uint)command.PlayerId >= (uint)Players.Length)
            return;
        if (command.Kind is CommandKind.Move or CommandKind.AttackMove)
        {
            OrderGroupMove(command);
            return;
        }

        bool buildStarted = false;
        for (int i = 0; i < command.UnitIds.Length; i++)
        {
            if (!_byId.TryGetValue(command.UnitIds[i], out var entity))
                continue;
            if (!entity.Alive || entity.PlayerId != command.PlayerId || entity.Kind != EntityKind.Unit)
                continue;
            if (entity.DistractedUntil > Tick)
                continue;

            switch (command.Kind)
            {
                case CommandKind.Stop:
                    CancelWork(entity);
                    entity.Order = OrderKind.Idle;
                    entity.TargetId = 0;
                    ClearPath(entity);
                    if (entity.Role == Roles.Hauler)
                        entity.Haul = HaulPhase.Auto;
                    break;
                case CommandKind.Attack:
                    if (!_byId.TryGetValue(command.TargetId, out var target) || !target.Alive || target.PlayerId == entity.PlayerId)
                        break;
                    CancelWork(entity);
                    if (Def(entity).Weapons.Count == 0)
                    {
                        entity.Order = OrderKind.Move;
                        entity.TargetId = 0;
                        if (entity.Role == Roles.Hauler)
                            entity.Haul = HaulPhase.Manual;
                        RequestPath(entity, target.CellX, target.CellY);
                        break;
                    }
                    entity.Order = OrderKind.Attack;
                    entity.TargetId = target.Id;
                    if (entity.Role == Roles.Hauler)
                        entity.Haul = HaulPhase.Manual;
                    break;
                case CommandKind.Build:
                    if (buildStarted || entity.Role != Roles.Constructor)
                        break;
                    CancelWork(entity);
                    buildStarted = TryBeginBuild(entity, command);
                    break;
                case CommandKind.Hack:
                    if (entity.Role != Roles.Hacker)
                        break;
                    CancelWork(entity);
                    TryBeginHack(entity, command);
                    break;
            }
        }
    }

    private void OrderGroupMove(SimCommand command)
    {
        var units = new List<SimEntity>();
        for (int i = 0; i < command.UnitIds.Length; i++)
        {
            if (!_byId.TryGetValue(command.UnitIds[i], out var entity))
                continue;
            if (!entity.Alive || entity.PlayerId != command.PlayerId || entity.Kind != EntityKind.Unit)
                continue;
            if (entity.DistractedUntil > Tick)
                continue;
            CancelWork(entity);
            units.Add(entity);
        }
        if (units.Count == 0)
            return;

        int originX = WorldScale.CellOf(command.X);
        int originY = WorldScale.CellOf(command.Y);
        int cols = (int)Math.Ceiling(Math.Sqrt(units.Count));
        if (cols < 1)
            cols = 1;
        var order = command.Kind == CommandKind.AttackMove ? OrderKind.AttackMove : OrderKind.Move;
        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            int col = i % cols;
            int row = i / cols;
            int dx = col - (cols - 1) / 2;
            int dy = row - (cols - 1) / 2;
            unit.Order = order;
            unit.TargetId = 0;
            if (unit.Role == Roles.Hauler)
                unit.Haul = HaulPhase.Manual;
            if (!FindFormationCell(unit, originX + dx, originY + dy, out int gx, out int gy))
                continue;
            RequestPath(unit, gx, gy);
        }
    }

    private bool FindFormationCell(SimEntity unit, int ox, int oy, out int gx, out int gy)
    {
        for (int radius = 0; radius < 8; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (radius != 0 && Math.Max(Math.Abs(x), Math.Abs(y)) != radius)
                        continue;
                    int cx = ox + x;
                    int cy = oy + y;
                    if (!Map.Walkable(cx, cy, unit.Domain))
                        continue;
                    gx = cx;
                    gy = cy;
                    return true;
                }
            }
        }

        gx = ox;
        gy = oy;
        return false;
    }

    private void ClearPath(SimEntity entity)
    {
        entity.Path.Clear();
        entity.Waypoint = 0;
        entity.PathPending = false;
        entity.PathToken++;
        entity.GoalCellX = -1;
        entity.GoalCellY = -1;
        entity.HasExactGoal = false;
    }

    private void PlaceOnFootprint(SimEntity entity, int cellX, int cellY, int footprint)
    {
        entity.CellX = cellX;
        entity.CellY = cellY;
        long x0 = WorldScale.CellCenter(cellX).Raw;
        long x1 = WorldScale.CellCenter(cellX + footprint - 1).Raw;
        long y0 = WorldScale.CellCenter(cellY).Raw;
        long y1 = WorldScale.CellCenter(cellY + footprint - 1).Raw;
        entity.X = new Fp((x0 + x1) / 2);
        entity.Y = new Fp((y0 + y1) / 2);
        entity.PrevX = entity.X;
        entity.PrevY = entity.Y;
    }

    private void BlockFootprint(SimEntity entity)
    {
        for (int dy = 0; dy < entity.Footprint; dy++)
        {
            for (int dx = 0; dx < entity.Footprint; dx++)
            {
                int x = entity.CellX + dx;
                int y = entity.CellY + dy;
                if (Map.InBounds(x, y))
                    Map.BlockedBy[Map.Index(x, y)] = entity.Id;
            }
        }
    }

    private void Unblock(SimEntity entity)
    {
        if (entity.Kind != EntityKind.Building)
            return;
        for (int dy = 0; dy < entity.Footprint; dy++)
        {
            for (int dx = 0; dx < entity.Footprint; dx++)
            {
                int x = entity.CellX + dx;
                int y = entity.CellY + dy;
                if (!Map.InBounds(x, y))
                    continue;
                int i = Map.Index(x, y);
                if (Map.BlockedBy[i] == entity.Id)
                    Map.BlockedBy[i] = 0;
            }
        }
    }

    private void SyncCell(SimEntity entity)
    {
        int cx = WorldScale.CellOf(entity.X);
        int cy = WorldScale.CellOf(entity.Y);
        if (cx < 0)
            cx = 0;
        if (cy < 0)
            cy = 0;
        if (cx >= Map.Width)
            cx = Map.Width - 1;
        if (cy >= Map.Height)
            cy = Map.Height - 1;
        entity.CellX = cx;
        entity.CellY = cy;
    }

    private void ClampToMap(SimEntity entity)
    {
        long maxX = (long)Map.Width * WorldScale.TileWorldUnits * Fp.Scale - 1;
        long maxY = (long)Map.Height * WorldScale.TileWorldUnits * Fp.Scale - 1;
        if (entity.X.Raw < 0)
            entity.X = Fp.Zero;
        if (entity.Y.Raw < 0)
            entity.Y = Fp.Zero;
        if (entity.X.Raw > maxX)
            entity.X = new Fp(maxX);
        if (entity.Y.Raw > maxY)
            entity.Y = new Fp(maxY);
    }

    private Fp SightOf(UnitDef def, UnitDomain? domain)
    {
        Fp best = def.Sight;
        if (domain == UnitDomain.Air && def.SightAir.Raw > best.Raw)
            best = def.SightAir;
        else if (domain == UnitDomain.Sea && def.SightSea.Raw > best.Raw)
            best = def.SightSea;
        else if (domain is UnitDomain.Ground or UnitDomain.Troop && def.SightGround.Raw > best.Raw)
            best = def.SightGround;
        if (best.Raw > Data.Rules.SightMax.Raw)
            best = Data.Rules.SightMax;
        return best;
    }

    private void RevealCircle(SimEntity viewer, Fp sightTiles)
    {
        var fog = Fog[viewer.PlayerId];
        int radius = (int)((sightTiles.Raw + Fp.Scale - 1) / Fp.Scale) + 1;
        long limit = sightTiles.Raw * WorldScale.TileWorldUnits;
        long limit2 = limit * limit;
        int x0 = Math.Max(0, viewer.CellX - radius);
        int x1 = Math.Min(Map.Width - 1, viewer.CellX + radius);
        int y0 = Math.Max(0, viewer.CellY - radius);
        int y1 = Math.Min(Map.Height - 1, viewer.CellY + radius);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                Fp cx = WorldScale.CellCenter(x);
                Fp cy = WorldScale.CellCenter(y);
                if (WorldScale.DistanceSquared(viewer.X, viewer.Y, cx, cy) <= limit2)
                    fog[Map.Index(x, y)] = 2;
            }
        }
    }

    private int SecondsToTicks(Fp seconds)
    {
        return WorldScale.DurationTicks(seconds, Data.Rules.TickHz, Fp.FromInt(1));
    }

    private int WeaponTicks(Fp seconds)
    {
        return WorldScale.DurationTicks(seconds, Data.Rules.TickHz, Data.Rules.FireRateScale);
    }

    private long BoostedSpeedRaw(UnitDef def, int playerId)
    {
        int level = 0;
        if ((uint)playerId < (uint)Players.Length)
            level = Math.Clamp(Players[playerId].SpeedLevel, -3, 3);
        long speed = def.Speed.Raw * (5 + level) / 5;
        long scale = Data.Rules.SpeedScale.Raw < 1 ? Fp.Scale : Data.Rules.SpeedScale.Raw;
        speed = speed * scale / Fp.Scale;
        return WorldScale.WorldPerTickRaw(new Fp(speed), Data.Rules.TickHz);
    }

    private readonly struct PathRequest
    {
        public readonly int EntityId;
        public readonly int Token;
        public readonly int X;
        public readonly int Y;

        public PathRequest(int entityId, int token, int x, int y)
        {
            EntityId = entityId;
            Token = token;
            X = x;
            Y = y;
        }
    }

    private sealed class Fnv64
    {
        public ulong Value = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public void Add(int value)
        {
            unchecked
            {
                AddByte((byte)value);
                AddByte((byte)(value >> 8));
                AddByte((byte)(value >> 16));
                AddByte((byte)(value >> 24));
            }
        }

        public void Add(long value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                    AddByte((byte)(value >> (8 * i)));
            }
        }

        public void Add(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                AddByte(bytes[i]);
        }

        private void AddByte(byte b)
        {
            unchecked
            {
                Value ^= b;
                Value *= Prime;
            }
        }
    }
}
