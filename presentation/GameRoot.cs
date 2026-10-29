using FirstClnkrWar.Sim;
using Godot;

namespace FirstClnkrWar;

public partial class GameRoot : Node3D
{
    private DataCatalog _data = null!;
    private Simulation _sim = null!;
    private InputBindings _keys = null!;
    private RtsCamera _camera = null!;
    private BattlefieldView _field = null!;
    private DebugHud _hud = null!;
    private ColorRect _box = null!;
    private readonly HashSet<int> _selected = new();
    private readonly List<int>[][] _groupsByPlayer = CreateGroups();
    private double _accum;
    private int _player;
    private bool _paused;
    private bool _dragging;
    private Vector2 _dragStart;
    private bool _mmb;
    private Vector2 _mmbLast;
    private int _lastGroup = -1;
    private ulong _lastGroupMs;

    public override void _Ready()
    {
        GetWindow().Title = "The First C.L.N.K.R. War — Phase 1";
        _data = CatalogLoader.Load(ReadDocuments());
        _keys = InputBindings.Load(ReadText("res://data/controls.yaml"));
        _sim = PrototypeScenario.Create(_data);

        _camera = new RtsCamera
        {
            PitchDegrees = _keys.StartPitch,
            Distance = _keys.StartZoom,
        };
        AddChild(_camera);

        _field = new BattlefieldView();
        AddChild(_field);
        _field.Build(_sim, _camera.Camera);

        var layer = new CanvasLayer();
        AddChild(layer);
        _box = new ColorRect
        {
            Color = new Color(0.4f, 0.75f, 1f, 0.18f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        layer.AddChild(_box);

        _hud = new DebugHud();
        _hud.PlayerChanged += SetPlayer;
        AddChild(_hud);

        SetPlayer(0);
        GD.Print($"Phase 1 ready. Tick {_data.Rules.TickHz} Hz. Entities {_sim.Entities.Count}.");
    }

    public override void _Process(double delta)
    {
        UpdateCamera((float)delta);
        if (!_paused)
        {
            double step = 1.0 / _sim.Data.Rules.TickHz;
            _accum += delta;
            int guard = 0;
            while (_accum >= step && guard < 5)
            {
                _sim.Step();
                _accum -= step;
                guard++;
            }
            if (guard == 5)
                _accum = 0;
        }

        float alpha = 1f;
        if (!_paused)
        {
            double step = 1.0 / _sim.Data.Rules.TickHz;
            alpha = (float)(_accum / step);
        }

        PruneSelection();
        _field.Sync(_sim, alpha, _player, _selected);
        _hud.SetText(HudText());
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse)
        {
            HandleMouseButton(mouse);
            return;
        }

        if (@event is InputEventMouseMotion motion && _mmb)
        {
            var delta = motion.Position - _mmbLast;
            _mmbLast = motion.Position;
            if (motion.AltPressed)
            {
                _camera.YawDegrees -= delta.X * 0.25f;
                _camera.PitchDegrees = Mathf.Clamp(_camera.PitchDegrees + delta.Y * 0.15f, _keys.MinPitch, _keys.MaxPitch);
                _camera.ApplyRig();
            }
            else
            {
                _camera.Pan(-delta.X * 0.06f, -delta.Y * 0.06f, 1f);
                _camera.ClampToMap(PrototypeScenario.MapSize * VisualScale.Tile);
            }
            return;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
            HandleKey(key);
    }

    private void HandleKey(InputEventKey key)
    {
        if (key.PhysicalKeycode == _keys.SwitchPlayer)
        {
            SetPlayer((_player + 1) % _sim.Players.Length);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.PhysicalKeycode == _keys.Pause)
        {
            _paused = !_paused;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.PhysicalKeycode == _keys.FocusBase)
        {
            FocusPlayer(_player);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.PhysicalKeycode == _keys.Stop)
        {
            Issue(CommandKind.Stop, _selected, Fp.Zero, Fp.Zero, 0);
            GetViewport().SetInputAsHandled();
            return;
        }

        int digit = Digit(key.PhysicalKeycode);
        if (digit < 0)
            return;
        var groups = GroupsFor(_player);
        if (key.CtrlPressed)
        {
            groups[digit].Clear();
            groups[digit].AddRange(_selected);
        }
        else
        {
            _selected.Clear();
            for (int i = 0; i < groups[digit].Count; i++)
            {
                int id = groups[digit][i];
                var entity = _sim.Find(id);
                if (entity != null && entity.Alive && entity.PlayerId == _player)
                    _selected.Add(id);
            }
            ulong now = Time.GetTicksMsec();
            if (_lastGroup == digit && now - _lastGroupMs < 350)
                FocusSelection();
            _lastGroup = digit;
            _lastGroupMs = now;
        }
        GetViewport().SetInputAsHandled();
    }

    private void HandleMouseButton(InputEventMouseButton mouse)
    {
        if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
        {
            _camera.Distance = Mathf.Max(_keys.MinZoom, _camera.Distance - _keys.ZoomStep);
            _camera.ApplyRig();
            return;
        }
        if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
        {
            _camera.Distance = Mathf.Min(_keys.MaxZoom, _camera.Distance + _keys.ZoomStep);
            _camera.ApplyRig();
            return;
        }
        if (mouse.ButtonIndex == MouseButton.Middle)
        {
            _mmb = mouse.Pressed;
            _mmbLast = mouse.Position;
            return;
        }
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed)
            {
                _dragging = true;
                _dragStart = mouse.Position;
                _box.Visible = false;
            }
            else if (_dragging)
            {
                _dragging = false;
                _box.Visible = false;
                FinishSelect(mouse.Position, mouse.ShiftPressed);
            }
            return;
        }
        if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
            IssueOrder(mouse.Position);
    }

    private void FinishSelect(Vector2 end, bool shift)
    {
        var rect = new Rect2(
            Mathf.Min(_dragStart.X, end.X),
            Mathf.Min(_dragStart.Y, end.Y),
            Mathf.Abs(_dragStart.X - end.X),
            Mathf.Abs(_dragStart.Y - end.Y));
        if (!shift)
            _selected.Clear();
        if (rect.Size.Length() < 8f)
        {
            var hit = _field.PickEntity(end, _player, enemies: false);
            if (hit != null)
            {
                if (shift && _selected.Contains(hit.Id))
                    _selected.Remove(hit.Id);
                else
                    _selected.Add(hit.Id);
            }
        }
        else
        {
            var ids = _field.UnitsInRect(rect, _player, _data.Rules.SelectionMax);
            for (int i = 0; i < ids.Count; i++)
                _selected.Add(ids[i]);
        }
        while (_selected.Count > _data.Rules.SelectionMax)
            _selected.Remove(_selected.First());
    }

    private void IssueOrder(Vector2 screen)
    {
        if (_selected.Count == 0)
            return;
        bool attackMove = _keys.IsDown(_keys.AttackMove);
        var enemy = attackMove ? null : _field.PickEntity(screen, _player, enemies: true);
        if (enemy != null)
        {
            Issue(CommandKind.Attack, _selected, Fp.Zero, Fp.Zero, enemy.Id);
            return;
        }
        if (!_field.TryGround(screen, out var x, out var y))
            return;
        Issue(attackMove ? CommandKind.AttackMove : CommandKind.Move, _selected, x, y, 0);
    }

    private void Issue(CommandKind kind, IEnumerable<int> ids, Fp x, Fp y, int target)
    {
        var list = new List<int>();
        foreach (var id in ids)
        {
            var entity = _sim.Find(id);
            if (entity != null && entity.Alive && entity.PlayerId == _player && entity.Kind == EntityKind.Unit)
                list.Add(id);
        }
        if (list.Count == 0)
            return;
        _sim.Enqueue(new SimCommand
        {
            ExecuteTick = _sim.Tick + 1,
            PlayerId = _player,
            Kind = kind,
            UnitIds = list.ToArray(),
            X = x,
            Y = y,
            TargetId = target,
        });
    }

    private void UpdateCamera(float delta)
    {
        float right = 0f;
        float back = 0f;
        if (_keys.IsDown(_keys.PanRight) || Input.IsPhysicalKeyPressed(Key.Right))
            right += 1f;
        if (_keys.IsDown(_keys.PanLeft) || Input.IsPhysicalKeyPressed(Key.Left))
            right -= 1f;
        if (_keys.IsDown(_keys.PanDown) || Input.IsPhysicalKeyPressed(Key.Down))
            back += 1f;
        if (_keys.IsDown(_keys.PanUp) || Input.IsPhysicalKeyPressed(Key.Up))
            back -= 1f;
        if (_keys.IsDown(_keys.RotateLeft))
            _camera.YawDegrees -= _keys.RotateSpeed * delta;
        if (_keys.IsDown(_keys.RotateRight))
            _camera.YawDegrees += _keys.RotateSpeed * delta;
        if (_keys.IsDown(_keys.TiltUp))
            _camera.PitchDegrees = Mathf.Clamp(_camera.PitchDegrees - _keys.TiltSpeed * delta, _keys.MinPitch, _keys.MaxPitch);
        if (_keys.IsDown(_keys.TiltDown))
            _camera.PitchDegrees = Mathf.Clamp(_camera.PitchDegrees + _keys.TiltSpeed * delta, _keys.MinPitch, _keys.MaxPitch);

        if (_keys.EdgeScroll)
        {
            var mouse = GetViewport().GetMousePosition();
            var size = GetViewport().GetVisibleRect().Size;
            const float margin = 10f;
            if (mouse.X >= 0f && mouse.Y >= 0f && mouse.X <= size.X && mouse.Y <= size.Y)
            {
                if (mouse.X < margin)
                    right -= 1f;
                else if (mouse.X > size.X - margin)
                    right += 1f;
                if (mouse.Y < margin)
                    back -= 1f;
                else if (mouse.Y > size.Y - margin)
                    back += 1f;
            }
        }

        if (right != 0f || back != 0f)
            _camera.Pan(right, back, _keys.PanSpeed * delta);
        _camera.ClampToMap(PrototypeScenario.MapSize * VisualScale.Tile);
        _camera.ApplyRig();

        if (_dragging)
        {
            var mouse = GetViewport().GetMousePosition();
            _box.Visible = true;
            _box.Position = new Vector2(Mathf.Min(_dragStart.X, mouse.X), Mathf.Min(_dragStart.Y, mouse.Y));
            _box.Size = new Vector2(Mathf.Abs(_dragStart.X - mouse.X), Mathf.Abs(_dragStart.Y - mouse.Y));
        }
    }

    private void SetPlayer(int player)
    {
        _player = player;
        _selected.Clear();
        _hud.SetPlayerSilent(player);
        FocusPlayer(player);
    }

    private void FocusPlayer(int player)
    {
        var focus = _field.FocusOf(player);
        _camera.Position = new Vector3(focus.X, 0f, focus.Z);
        _camera.ClampToMap(PrototypeScenario.MapSize * VisualScale.Tile);
    }

    private void FocusSelection()
    {
        Vector3 sum = Vector3.Zero;
        int count = 0;
        foreach (var id in _selected)
        {
            var entity = _sim.Find(id);
            if (entity == null || !entity.Alive)
                continue;
            sum += new Vector3(VisualScale.FromRaw(entity.X.Raw), 0f, VisualScale.FromRaw(entity.Y.Raw));
            count++;
        }
        if (count == 0)
            return;
        var p = sum / count;
        _camera.Position = new Vector3(p.X, 0f, p.Z);
    }

    private void PruneSelection()
    {
        _dropIds.Clear();
        foreach (var id in _selected)
        {
            var entity = _sim.Find(id);
            if (entity == null || !entity.Alive || entity.PlayerId != _player)
                _dropIds.Add(id);
        }
        for (int i = 0; i < _dropIds.Count; i++)
            _selected.Remove(_dropIds[i]);
    }

    private readonly List<int> _dropIds = new();

    private static List<int>[][] CreateGroups()
    {
        var groups = new List<int>[2][];
        for (int p = 0; p < groups.Length; p++)
        {
            groups[p] = new List<int>[10];
            for (int i = 0; i < 10; i++)
                groups[p][i] = new List<int>();
        }
        return groups;
    }

    private List<int>[] GroupsFor(int player)
    {
        if ((uint)player >= (uint)_groupsByPlayer.Length)
            return _groupsByPlayer[0];
        return _groupsByPlayer[player];
    }

    private string HudText()
    {
        var tdo = _sim.Players[0];
        var clnkr = _sim.Players[1];
        int cap = _sim.StorageCap(_player);
        string paused = _paused ? "PAUSED\n" : "";
        string detail = "Selected: 0";
        if (_selected.Count == 1)
        {
            var entity = _sim.Find(_selected.First());
            if (entity != null && _data.Units.TryGetValue(entity.TypeId, out var def))
            {
                detail = $"{def.Name}   HP {entity.Hp}/{entity.MaxHp}   {entity.Order}";
                if (entity.Role == Roles.Hauler)
                    detail += $"   {entity.Haul}   cargo {entity.CargoUnits}";
            }
        }
        else if (_selected.Count > 1)
        {
            detail = $"Selected: {_selected.Count}";
        }

        double fps = Engine.GetFramesPerSecond();
        return paused
            + $"TDO credits {tdo.Credits}    C.L.N.K.R. credits {clnkr.Credits}\n"
            + $"Active cap {cap}    Tick {_sim.Tick}    FPS {fps:0}\n"
            + detail + "\n"
            + "LMB select / box    RMB move or attack    Z+RMB attack-move    H stop\n"
            + "Ctrl+0-9 assign group    0-9 recall    Tab switch slot    Space pause\n"
            + "WASD or arrows pan    Q/E rotate    R/F tilt    wheel zoom    MMB pan    Alt+MMB orbit";
    }

    private static int Digit(Key key)
    {
        if (key >= Key.Key0 && key <= Key.Key9)
            return (int)key - (int)Key.Key0;
        return -1;
    }

    private static List<(string Name, string Yaml)> ReadDocuments()
    {
        var docs = new List<(string, string)>
        {
            ("rules.yaml", ReadText("res://data/rules.yaml")),
            ("factions.yaml", ReadText("res://data/factions.yaml")),
            ("buildings.yaml", ReadText("res://data/buildings.yaml")),
        };
        var names = new List<string>();
        using var dir = DirAccess.Open("res://data/units");
        if (dir == null)
            throw new FileNotFoundException("res://data/units is missing.");
        dir.ListDirBegin();
        while (true)
        {
            string name = dir.GetNext();
            if (name.Length == 0)
                break;
            if (!dir.CurrentIsDir() && name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                names.Add(name);
        }
        dir.ListDirEnd();
        names.Sort(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++)
            docs.Add(("units/" + names[i], ReadText("res://data/units/" + names[i])));
        return docs;
    }

    private static string ReadText(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
            throw new FileNotFoundException(path, Godot.FileAccess.GetOpenError().ToString());
        return file.GetAsText();
    }
}
