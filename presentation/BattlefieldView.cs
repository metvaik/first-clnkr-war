using FirstClnkrWar.Sim;
using Godot;

namespace FirstClnkrWar;

public partial class BattlefieldView : Node3D
{
    private const float PickPixels = 22f;

    private Simulation _sim = null!;
    private Camera3D _camera = null!;
    private readonly Dictionary<int, UnitVisual> _visuals = new();
    private readonly List<int> _drop = new();
    private MultiMesh _fog = null!;
    private int _fogPlayer = -1;
    private int _fogTick = -1;
    private StandardMaterial3D _ringMaterial = null!;
    private readonly Dictionary<int, StandardMaterial3D> _factionMaterials = new();
    private StandardMaterial3D _tracerMaterial = null!;
    private StandardMaterial3D _hpBack = null!;
    private StandardMaterial3D _channelMaterial = null!;
    private float[] _cornerH = Array.Empty<float>();
    private int _cornerW;

    public void Build(Simulation sim, Camera3D camera)
    {
        _sim = sim;
        _camera = camera;
        _ringMaterial = Unshaded(new Color(0.95f, 0.9f, 0.45f, 0.9f));
        _tracerMaterial = Unshaded(new Color(1f, 0.95f, 0.7f));
        _hpBack = Unshaded(new Color(0.02f, 0.02f, 0.02f, 0.85f), billboard: true);
        _channelMaterial = Unshaded(new Color(0.35f, 0.75f, 1f), billboard: true);
        BuildCorners(sim.Map);

        AddChild(MakeEnvironment());
        AddChild(MakeSun());
        AddChild(MakeTerrain(sim.Map));
        AddChild(MakeFog(sim.Map));
    }

    public void Sync(Simulation sim, float alpha, int player, HashSet<int> selected)
    {
        alpha = Mathf.Clamp(alpha, 0f, 1f);
        SyncFog(sim, player);
        _drop.Clear();
        foreach (var pair in _visuals)
            _drop.Add(pair.Key);

        for (int i = 0; i < sim.Entities.Count; i++)
        {
            var entity = sim.Entities[i];
            if (!ShouldDraw(sim, entity, player))
                continue;
            _drop.Remove(entity.Id);
            if (!_visuals.TryGetValue(entity.Id, out var visual))
            {
                visual = CreateVisual(sim, entity);
                _visuals[entity.Id] = visual;
                AddChild(visual.Root);
            }

            float x = Mathf.Lerp(VisualScale.FromRaw(entity.PrevX.Raw), VisualScale.FromRaw(entity.X.Raw), alpha);
            float z = Mathf.Lerp(VisualScale.FromRaw(entity.PrevY.Raw), VisualScale.FromRaw(entity.Y.Raw), alpha);
            float ground = GroundHeight(sim.Map, x, z);
            float hover = entity.Domain == UnitDomain.Air ? 4.6f : 0f;
            visual.Root.Position = new Vector3(x, ground + hover, z);

            float dx = VisualScale.FromRaw(entity.X.Raw) - VisualScale.FromRaw(entity.PrevX.Raw);
            float dz = VisualScale.FromRaw(entity.Y.Raw) - VisualScale.FromRaw(entity.PrevY.Raw);
            var facing = new Vector3(dx, 0f, dz);
            if (facing.LengthSquared() > 0.0004f)
            {
                var lookTarget = visual.Root.GlobalPosition + facing;
                if (!visual.Root.GlobalPosition.IsEqualApprox(lookTarget))
                    visual.Root.LookAt(lookTarget, Vector3.Up);
            }

            float ratio = entity.MaxHp <= 0 ? 0f : Mathf.Clamp((float)entity.Hp / entity.MaxHp, 0f, 1f);
            SetLeftAnchoredBar(visual.HpFill, ratio, visual.BarY);
            visual.HpFill.MaterialOverride = HpColor(ratio);
            visual.Ring.Visible = selected.Contains(entity.Id) && entity.Kind != EntityKind.Projectile;
            UpdateChannelBar(visual, entity);
        }

        for (int i = 0; i < _drop.Count; i++)
        {
            int id = _drop[i];
            if (_visuals.TryGetValue(id, out var visual))
            {
                visual.Root.QueueFree();
                _visuals.Remove(id);
            }
        }
    }

    public SimEntity? PickAny(Vector2 screen, int player)
    {
        return PickEntity(screen, player, enemies: false, anyOwner: true);
    }

    public SimEntity? PickEntity(Vector2 screen, int player, bool enemies)
    {
        return PickEntity(screen, player, enemies, anyOwner: false);
    }

    private SimEntity? PickEntity(Vector2 screen, int player, bool enemies, bool anyOwner)
    {
        SimEntity? best = null;
        float bestDist = PickPixels;
        for (int i = 0; i < _sim.Entities.Count; i++)
        {
            var entity = _sim.Entities[i];
            if (!ShouldDraw(_sim, entity, player) || entity.Kind == EntityKind.Projectile)
                continue;
            if (!anyOwner)
            {
                bool enemy = entity.PlayerId != player;
                if (enemies != enemy)
                    continue;
            }
            if (!_visuals.TryGetValue(entity.Id, out var visual))
                continue;
            var point = _camera.UnprojectPosition(visual.Root.GlobalPosition);
            float dist = point.DistanceTo(screen);
            float limit = entity.Kind == EntityKind.Building ? 32f : PickPixels;
            if (dist < limit && (best == null || dist < bestDist))
            {
                best = entity;
                bestDist = dist;
            }
        }
        return best;
    }

    public List<int> UnitsInRect(Rect2 rect, int player, int max)
    {
        var ids = new List<int>();
        for (int i = 0; i < _sim.Entities.Count; i++)
        {
            var entity = _sim.Entities[i];
            if (entity.PlayerId != player || entity.Kind != EntityKind.Unit || !entity.Alive)
                continue;
            if (!ShouldDraw(_sim, entity, player))
                continue;
            if (!_visuals.TryGetValue(entity.Id, out var visual))
                continue;
            if (!rect.HasPoint(_camera.UnprojectPosition(visual.Root.GlobalPosition)))
                continue;
            ids.Add(entity.Id);
            if (ids.Count >= max)
                break;
        }
        return ids;
    }

    public bool TryGround(Vector2 screen, out Fp x, out Fp y)
    {
        var origin = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        var space = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + dir * 4000f);
        var hit = space.IntersectRay(query);
        Vector3 point;
        if (hit.Count > 0)
        {
            point = hit["position"].AsVector3();
        }
        else if (Mathf.Abs(dir.Y) > 0.0001f)
        {
            float t = -origin.Y / dir.Y;
            if (t < 0f)
            {
                x = Fp.Zero;
                y = Fp.Zero;
                return false;
            }
            point = origin + dir * t;
        }
        else
        {
            x = Fp.Zero;
            y = Fp.Zero;
            return false;
        }

        x = VisualScale.ToWorld(point.X);
        y = VisualScale.ToWorld(point.Z);
        return true;
    }

    public Vector3 FocusOf(int player)
    {
        Vector3 sum = Vector3.Zero;
        int count = 0;
        Vector3? home = null;
        for (int i = 0; i < _sim.Entities.Count; i++)
        {
            var entity = _sim.Entities[i];
            if (!entity.Alive || entity.PlayerId != player || entity.Kind == EntityKind.Deposit)
                continue;
            var p = new Vector3(VisualScale.FromRaw(entity.X.Raw), 0f, VisualScale.FromRaw(entity.Y.Raw));
            if (entity.Role == Roles.Refinery)
                home = p;
            if (entity.Kind == EntityKind.Projectile)
                continue;
            sum += p;
            count++;
        }
        if (home.HasValue)
            return home.Value;
        return count == 0 ? new Vector3(40f, 0f, 40f) : sum / count;
    }

    private bool ShouldDraw(Simulation sim, SimEntity entity, int player)
    {
        if (!entity.Alive || entity.Kind == EntityKind.Deposit)
            return false;
        if (entity.PlayerId == player)
            return true;
        return sim.CellVisible(player, entity.CellX, entity.CellY);
    }

    private void SyncFog(Simulation sim, int player)
    {
        if (player == _fogPlayer && sim.Tick == _fogTick)
            return;
        _fogPlayer = player;
        _fogTick = sim.Tick;
        var fog = sim.Fog[player];
        int n = sim.Map.Width * sim.Map.Height;
        for (int i = 0; i < n; i++)
        {
            _fog.SetInstanceColor(i, fog[i] switch
            {
                2 => new Color(0f, 0f, 0f, 0f),
                1 => new Color(0f, 0f, 0f, 0.42f),
                _ => new Color(0f, 0f, 0f, 0.94f),
            });
        }
    }

    private UnitVisual CreateVisual(Simulation sim, SimEntity entity)
    {
        var color = FactionColor(sim, entity.PlayerId);
        var root = new Node3D { Name = $"e{entity.Id}" };
        float bar = 1.6f;
        if (entity.Kind == EntityKind.Projectile)
        {
            var tracer = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.55f) },
                MaterialOverride = _tracerMaterial,
                Position = new Vector3(0f, 0.4f, 0f),
            };
            root.AddChild(tracer);
            bar = 0.8f;
        }
        else
        {
            bar = AddShape(root, entity.Shape, color, entity.Footprint);
        }

        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.72f, OuterRadius = 0.9f, Rings = 6, RingSegments = 12 },
            MaterialOverride = _ringMaterial,
            Visible = false,
            Position = new Vector3(0f, 0.08f, 0f),
        };
        root.AddChild(ring);

        var hpBack = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.25f, 0.14f) },
            MaterialOverride = _hpBack,
            Position = new Vector3(0f, bar, 0f),
        };
        var hpFill = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.2f, 0.09f) },
            MaterialOverride = HpColor(1f),
            Position = new Vector3(0f, bar + 0.001f, 0f),
        };
        var channelBack = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.2f, 0.08f) },
            MaterialOverride = _hpBack,
            Position = new Vector3(0f, bar + 0.22f, 0f),
            Visible = false,
        };
        var channelFill = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.2f, 0.06f) },
            MaterialOverride = _channelMaterial,
            Position = new Vector3(0f, bar + 0.22f, 0.02f),
            Visible = false,
        };
        if (entity.Kind == EntityKind.Projectile)
        {
            hpBack.Visible = false;
            hpFill.Visible = false;
            ring.Visible = false;
        }
        root.AddChild(hpBack);
        root.AddChild(hpFill);
        root.AddChild(channelBack);
        root.AddChild(channelFill);
        return new UnitVisual(root, hpFill, ring, channelFill, channelBack, bar + 0.001f);
    }

    private float AddShape(Node3D root, string shape, Color color, int footprint)
    {
        var body = FactionMaterial(color);
        var dark = FactionMaterial(color.Darkened(0.25f));
        switch (shape)
        {
            case "tank":
                AddBox(root, new Vector3(1.15f, 0.38f, 1.7f), body, new Vector3(0f, 0.28f, 0f));
                AddBox(root, new Vector3(0.55f, 0.32f, 0.7f), dark, new Vector3(0f, 0.58f, -0.05f));
                AddBox(root, new Vector3(0.12f, 0.12f, 0.85f), dark, new Vector3(0f, 0.62f, -0.7f));
                return 1.5f;
            case "capsule":
                var capsule = new MeshInstance3D
                {
                    Mesh = new CapsuleMesh { Radius = 0.28f, Height = 1.25f },
                    MaterialOverride = body,
                    RotationDegrees = new Vector3(90f, 0f, 0f),
                    Position = new Vector3(0f, 0.4f, 0f),
                };
                root.AddChild(capsule);
                return 1.35f;
            case "air":
                AddBox(root, new Vector3(1.5f, 0.16f, 0.38f), body, new Vector3(0f, 0.45f, 0f));
                AddBox(root, new Vector3(0.38f, 0.28f, 1.35f), dark, new Vector3(0f, 0.48f, 0f));
                AddBox(root, new Vector3(0.16f, 0.1f, 0.55f), body, new Vector3(0f, 0.62f, -0.15f));
                return 1.5f;
            case "hauler":
                AddBox(root, new Vector3(0.7f, 0.45f, 0.55f), dark, new Vector3(0f, 0.4f, -0.45f));
                AddBox(root, new Vector3(1.05f, 0.45f, 1.15f), body, new Vector3(0f, 0.48f, 0.35f));
                return 1.6f;
            case "sam":
                AddBox(root, new Vector3(1.05f, 0.4f, 1.45f), body, new Vector3(0f, 0.3f, 0f));
                AddBox(root, new Vector3(0.18f, 0.7f, 0.18f), dark, new Vector3(0f, 0.8f, 0f));
                AddBox(root, new Vector3(0.55f, 0.12f, 0.12f), dark, new Vector3(0f, 1.1f, 0f));
                return 1.9f;
            case "building":
                float span = Math.Max(1, footprint) * VisualScale.Tile * 0.98f;
                AddBox(root, new Vector3(span, 1.1f, span), body, new Vector3(0f, 0.55f, 0f));
                AddBox(root, new Vector3(span * 0.28f, 0.4f, span * 0.28f), dark, new Vector3(0f, 1.25f, 0f));
                return 1.7f + span * 0.05f;
            default:
                AddBox(root, new Vector3(0.9f, 0.55f, 1.3f), body, new Vector3(0f, 0.35f, 0f));
                return 1.4f;
        }
    }

    private static void AddBox(Node3D root, Vector3 size, Material material, Vector3 position)
    {
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            Position = position,
        });
    }

    private Node MakeEnvironment()
    {
        var world = new WorldEnvironment();
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.53f, 0.66f, 0.8f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.66f, 0.72f),
            AmbientLightEnergy = 0.75f,
        };
        world.Environment = env;
        return world;
    }

    private static DirectionalLight3D MakeSun()
    {
        return new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-48f, -32f, 0f),
            ShadowEnabled = true,
            LightEnergy = 1.2f,
        };
    }

    private Node3D MakeTerrain(SimMap map)
    {
        int vw = map.Width + 1;
        var colors = new Color[vw * (map.Height + 1)];
        for (int y = 0; y <= map.Height; y++)
        {
            for (int x = 0; x <= map.Width; x++)
                colors[y * vw + x] = CornerColor(map, x, y);
        }

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int y = 0; y <= map.Height; y++)
        {
            for (int x = 0; x <= map.Width; x++)
            {
                int i = y * vw + x;
                tool.SetColor(colors[i]);
                tool.AddVertex(new Vector3(x * VisualScale.Tile, _cornerH[i], y * VisualScale.Tile));
            }
        }
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i00 = y * vw + x;
                tool.AddIndex(i00);
                tool.AddIndex(i00 + 1);
                tool.AddIndex(i00 + vw + 1);
                tool.AddIndex(i00);
                tool.AddIndex(i00 + vw + 1);
                tool.AddIndex(i00 + vw);
            }
        }
        tool.GenerateNormals();
        var mesh = tool.Commit();
        var visual = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Roughness = 1f,
            },
        };
        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        var root = new Node3D { Name = "Terrain" };
        root.AddChild(visual);
        root.AddChild(body);
        return root;
    }

    private void BuildCorners(SimMap map)
    {
        _cornerW = map.Width + 1;
        int vh = map.Height + 1;
        _cornerH = new float[_cornerW * vh];
        for (int y = 0; y < vh; y++)
        {
            for (int x = 0; x < _cornerW; x++)
            {
                float sum = 0f;
                int n = 0;
                AccHeight(map, x - 1, y - 1, ref sum, ref n);
                AccHeight(map, x, y - 1, ref sum, ref n);
                AccHeight(map, x - 1, y, ref sum, ref n);
                AccHeight(map, x, y, ref sum, ref n);
                _cornerH[y * _cornerW + x] = n == 0 ? 0f : sum / n;
            }
        }
    }

    private static void AccHeight(SimMap map, int x, int y, ref float sum, ref int n)
    {
        if (!map.InBounds(x, y))
            return;
        sum += VisualScale.Height(map.Heights[map.Index(x, y)]);
        n++;
    }

    private Color CornerColor(SimMap map, int x, int y)
    {
        float r = 0f, g = 0f, b = 0f;
        int n = 0;
        AccColor(map, x - 1, y - 1, ref r, ref g, ref b, ref n);
        AccColor(map, x, y - 1, ref r, ref g, ref b, ref n);
        AccColor(map, x - 1, y, ref r, ref g, ref b, ref n);
        AccColor(map, x, y, ref r, ref g, ref b, ref n);
        if (n == 0)
            return new Color(0.3f, 0.4f, 0.25f);
        return new Color(r / n, g / n, b / n);
    }

    private static void AccColor(SimMap map, int x, int y, ref float r, ref float g, ref float b, ref int n)
    {
        if (!map.InBounds(x, y))
            return;
        int i = map.Index(x, y);
        var color = TerrainColor(map.Terrain[i], map.Tint[i]);
        r += color.R;
        g += color.G;
        b += color.B;
        n++;
    }

    private static Color TerrainColor(TerrainKind terrain, byte tint)
    {
        if (tint == 1)
            return new Color(0.62f, 0.48f, 0.18f);
        if (tint == 2)
            return new Color(0.45f, 0.28f, 0.62f);
        return terrain switch
        {
            TerrainKind.Water => new Color(0.16f, 0.32f, 0.52f),
            TerrainKind.Rock => new Color(0.38f, 0.36f, 0.34f),
            _ => new Color(0.34f, 0.46f, 0.28f),
        };
    }

    private MultiMeshInstance3D MakeFog(SimMap map)
    {
        var quad = new QuadMesh
        {
            Size = new Vector2(VisualScale.Tile, VisualScale.Tile),
            Orientation = PlaneMesh.OrientationEnum.Y,
        };
        int n = map.Width * map.Height;
        _fog = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = quad,
            InstanceCount = n,
        };
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = map.Index(x, y);
                float h = CornerMax(x, y) + 0.2f;
                var xf = new Transform3D(Basis.Identity, new Vector3(VisualScale.CellX(x), h, VisualScale.CellX(y)));
                _fog.SetInstanceTransform(i, xf);
                _fog.SetInstanceColor(i, new Color(0f, 0f, 0f, 0.94f));
            }
        }

        return new MultiMeshInstance3D
        {
            Name = "Fog",
            Multimesh = _fog,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = Colors.White,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
    }

    private float GroundHeight(SimMap map, float visualX, float visualZ)
    {
        if (_cornerH.Length == 0)
            return 0f;
        float gx = visualX / VisualScale.Tile;
        float gz = visualZ / VisualScale.Tile;
        int x0 = Mathf.Clamp((int)Mathf.Floor(gx), 0, map.Width - 1);
        int z0 = Mathf.Clamp((int)Mathf.Floor(gz), 0, map.Height - 1);
        float tx = Mathf.Clamp(gx - x0, 0f, 1f);
        float tz = Mathf.Clamp(gz - z0, 0f, 1f);
        float h00 = _cornerH[z0 * _cornerW + x0];
        float h10 = _cornerH[z0 * _cornerW + x0 + 1];
        float h01 = _cornerH[(z0 + 1) * _cornerW + x0];
        float h11 = _cornerH[(z0 + 1) * _cornerW + x0 + 1];
        float hx0 = Mathf.Lerp(h00, h10, tx);
        float hx1 = Mathf.Lerp(h01, h11, tx);
        return Mathf.Lerp(hx0, hx1, tz);
    }

    private float CornerMax(int cellX, int cellY)
    {
        float max = 0f;
        for (int y = cellY; y <= cellY + 1; y++)
        {
            for (int x = cellX; x <= cellX + 1; x++)
            {
                int i = y * _cornerW + x;
                if ((uint)i < (uint)_cornerH.Length)
                    max = Mathf.Max(max, _cornerH[i]);
            }
        }
        return max;
    }

    private static void SetLeftAnchoredBar(MeshInstance3D fill, float ratio, float y)
    {
        ratio = Mathf.Clamp(ratio, 0.001f, 1f);
        fill.Scale = new Vector3(ratio, 1f, 1f);
        fill.Position = new Vector3(-0.6f * (1f - ratio), y, fill.Position.Z);
    }

    private void UpdateChannelBar(UnitVisual visual, SimEntity entity)
    {
        bool capture = entity.WorkGoal > 0 && entity.Work != WorkKind.None;
        bool access = entity.AccessWindow > 0 && entity.AccessTicksLeft > 0;
        visual.ChannelBack.Visible = capture || access;
        visual.ChannelFill.Visible = capture || access;
        if (capture)
            SetLeftAnchoredBar(visual.ChannelFill, (float)entity.WorkTicks / entity.WorkGoal, visual.BarY + 0.22f);
        else if (access)
            SetLeftAnchoredBar(visual.ChannelFill, (float)entity.AccessTicksLeft / entity.AccessWindow, visual.BarY + 0.22f);
    }

    private Color FactionColor(Simulation sim, int player)
    {
        if ((uint)player >= (uint)sim.Players.Length)
            return new Color(0.8f, 0.8f, 0.8f);
        string faction = sim.Players[player].Faction;
        if (sim.Data.Factions.TryGetValue(faction, out var def))
            return new Color(def.R / 255f, def.G / 255f, def.B / 255f);
        return player == 0 ? new Color(0.22f, 0.48f, 0.95f) : new Color(0.92f, 0.38f, 0.12f);
    }

    private StandardMaterial3D FactionMaterial(Color color)
    {
        int key = unchecked((int)color.ToRgba32());
        if (_factionMaterials.TryGetValue(key, out var mat))
            return mat;
        mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.82f,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 0.18f,
        };
        _factionMaterials[key] = mat;
        return mat;
    }

    private readonly Dictionary<int, StandardMaterial3D> _hpColors = new();

    private StandardMaterial3D HpColor(float ratio)
    {
        int bucket = Mathf.Clamp((int)(ratio * 10f), 0, 10);
        if (_hpColors.TryGetValue(bucket, out var mat))
            return mat;
        var color = new Color(1f - ratio, 0.25f + 0.6f * ratio, 0.15f);
        mat = Unshaded(color, billboard: true);
        _hpColors[bucket] = mat;
        return mat;
    }

    private static StandardMaterial3D Unshaded(Color color, bool billboard = false)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = color.A < 0.99f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            BillboardMode = billboard ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled,
            NoDepthTest = billboard,
        };
    }

    private sealed class UnitVisual
    {
        public readonly Node3D Root;
        public readonly MeshInstance3D HpFill;
        public readonly MeshInstance3D Ring;
        public readonly MeshInstance3D ChannelFill;
        public readonly MeshInstance3D ChannelBack;
        public readonly float BarY;

        public UnitVisual(Node3D root, MeshInstance3D hpFill, MeshInstance3D ring, MeshInstance3D channelFill, MeshInstance3D channelBack, float barY)
        {
            Root = root;
            HpFill = hpFill;
            Ring = ring;
            ChannelFill = channelFill;
            ChannelBack = channelBack;
            BarY = barY;
        }
    }
}
