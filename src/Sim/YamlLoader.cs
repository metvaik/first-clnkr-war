using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace FirstClnkrWar.Sim;

public static class CatalogLoader
{
    public static DataCatalog LoadFromDirectory(string directory)
    {
        var docs = new List<(string Name, string Yaml)>();
        docs.Add(("rules.yaml", File.ReadAllText(Path.Combine(directory, "rules.yaml"))));
        docs.Add(("factions.yaml", File.ReadAllText(Path.Combine(directory, "factions.yaml"))));
        docs.Add(("buildings.yaml", File.ReadAllText(Path.Combine(directory, "buildings.yaml"))));

        string unitsDir = Path.Combine(directory, "units");
        foreach (var file in Directory.GetFiles(unitsDir, "*.yaml").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = "units/" + Path.GetFileName(file);
            docs.Add((name, File.ReadAllText(file)));
        }

        // TODO: compile YAML to a cached binary catalog, rebuilt only when a source file changes.
        return Load(docs);
    }

    public static DataCatalog Load(IReadOnlyList<(string Name, string Yaml)> documents)
    {
        var catalog = new DataCatalog();
        foreach (var doc in documents)
        {
            if (IsName(doc.Name, "rules.yaml"))
                ApplyRules(catalog.Rules, Parse(doc.Yaml));
        }

        foreach (var doc in documents)
        {
            if (IsName(doc.Name, "factions.yaml"))
                ApplyFactions(catalog, Parse(doc.Yaml));
        }

        foreach (var doc in documents)
        {
            bool buildings = IsName(doc.Name, "buildings.yaml");
            bool units = doc.Name.Replace('\\', '/').Contains("units/", StringComparison.Ordinal)
                || IsName(doc.Name, "units.yaml");
            if (!buildings && !units)
                continue;
            ApplyUnits(catalog, Parse(doc.Yaml), buildings);
        }

        return catalog;
    }

    private static bool IsName(string name, string file)
    {
        string n = name.Replace('\\', '/');
        return n.Equals(file, StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase);
    }

    private static YamlMappingNode Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode map)
            throw new DataSchemaException("YAML root must be a mapping.");
        return map;
    }

    private static void ApplyRules(SimRules rules, YamlMappingNode root)
    {
        if (Child(root, "sim") is YamlMappingNode sim)
        {
            rules.TickHz = Int(sim, "tick_hz", rules.TickHz);
            if (rules.TickHz < 1)
                throw new DataSchemaException("sim.tick_hz must be >= 1.");
            if (Has(sim, "speed_scale"))
                rules.SpeedScale = Fixed(sim, "speed_scale");
        }

        if (Child(root, "limits") is YamlMappingNode limits)
        {
            if (Has(limits, "sight_max_tiles"))
                rules.SightMax = Fixed(limits, "sight_max_tiles");
            rules.RangeBonusMaxPct = Int(limits, "range_bonus_max_pct", rules.RangeBonusMaxPct);
            rules.ControlGroups = Int(limits, "control_groups", rules.ControlGroups);
            rules.SelectionMax = Int(limits, "selection_max", rules.SelectionMax);
            rules.ProjectilePool = Int(limits, "projectile_pool", rules.ProjectilePool);
            rules.PopulationCap = Int(limits, "population_cap", rules.PopulationCap);
        }

        if (Child(root, "combat") is YamlMappingNode combat)
        {
            rules.TargetSearchIntervalTicks = Math.Max(1, Int(combat, "target_search_interval_ticks", rules.TargetSearchIntervalTicks));
            rules.FogUpdateIntervalTicks = Math.Max(1, Int(combat, "fog_update_interval_ticks", rules.FogUpdateIntervalTicks));
            if (Has(combat, "fire_rate_scale"))
                rules.FireRateScale = Fixed(combat, "fire_rate_scale");
        }

        if (Child(root, "construction") is YamlMappingNode construction)
            rules.DefaultBuildSeconds = Math.Max(1, Int(construction, "default_build_s", rules.DefaultBuildSeconds));

        if (Child(root, "hacking") is YamlMappingNode hacking)
        {
            if (Has(hacking, "capture_s"))
                rules.CaptureSeconds = Fixed(hacking, "capture_s");
            if (Has(hacking, "infiltrate_s"))
                rules.InfiltrateSeconds = Fixed(hacking, "infiltrate_s");
            if (Has(hacking, "access_window_s"))
                rules.AccessWindowSeconds = Fixed(hacking, "access_window_s");
            if (Has(hacking, "ability_cooldown_s"))
                rules.AbilityCooldownSeconds = Fixed(hacking, "ability_cooldown_s");
            if (Has(hacking, "scramble_s"))
                rules.ScrambleSeconds = Fixed(hacking, "scramble_s");
            rules.ScrambleMinTiles = Int(hacking, "scramble_min_tiles", rules.ScrambleMinTiles);
            rules.ScrambleMaxTiles = Int(hacking, "scramble_max_tiles", rules.ScrambleMaxTiles);
            if (rules.ScrambleMaxTiles < rules.ScrambleMinTiles)
                rules.ScrambleMaxTiles = rules.ScrambleMinTiles;
        }

        if (Child(root, "movement") is YamlMappingNode movement && Has(movement, "separation_tiles"))
            rules.SeparationTiles = Fixed(movement, "separation_tiles");

        if (Child(root, "pathfinding") is YamlMappingNode path)
            rules.PathRequestsPerTick = Math.Max(1, Int(path, "requests_per_tick", rules.PathRequestsPerTick));

        if (Child(root, "economy") is YamlMappingNode economy)
        {
            rules.MiningRatePerSecond = Int(economy, "mining_rate_per_s", rules.MiningRatePerSecond);
            rules.LoadUnitsPerSecond = Int(economy, "load_units_per_s", rules.LoadUnitsPerSecond);
            rules.UnloadCreditsPerSecond = Int(economy, "unload_credits_per_s", rules.UnloadCreditsPerSecond);
            rules.HaulerCapacity = Int(economy, "hauler_capacity", rules.HaulerCapacity);
            rules.RefineryStorageCap = Int(economy, "refinery_storage_cap", rules.RefineryStorageCap);
            rules.SiteBuffer = Int(economy, "site_buffer", rules.SiteBuffer);
            rules.RareEarthMultiplier = Int(economy, "rare_earth_multiplier", rules.RareEarthMultiplier);
            rules.StartingCredits = Int(economy, "starting_credits", rules.StartingCredits);
        }

        if (Child(root, "players") is YamlMappingNode players)
        {
            rules.StartingFirepower = Int(players, "starting_firepower", rules.StartingFirepower);
            rules.StartingArmour = Int(players, "starting_armour", rules.StartingArmour);
            rules.StartingSpeed = Int(players, "starting_speed", rules.StartingSpeed);
        }

        if (Child(root, "schema") is YamlMappingNode schema && Has(schema, "transform_s_default"))
            rules.TransformSecondsDefault = Fixed(schema, "transform_s_default");
    }

    private static void ApplyFactions(DataCatalog catalog, YamlMappingNode root)
    {
        if (Child(root, "factions") is not YamlSequenceNode seq)
            return;
        foreach (var node in seq)
        {
            var map = (YamlMappingNode)node;
            var faction = new FactionDef
            {
                Id = Str(map, "id"),
                Name = Str(map, "name"),
            };
            if (Child(map, "color") is YamlSequenceNode color && color.Children.Count >= 3)
            {
                faction.R = (byte)int.Parse(Scalar(color[0]), CultureInfo.InvariantCulture);
                faction.G = (byte)int.Parse(Scalar(color[1]), CultureInfo.InvariantCulture);
                faction.B = (byte)int.Parse(Scalar(color[2]), CultureInfo.InvariantCulture);
            }
            catalog.Factions[faction.Id] = faction;
        }
    }

    private static void ApplyUnits(DataCatalog catalog, YamlMappingNode root, bool forceBuilding)
    {
        YamlSequenceNode? seq = Child(root, "units") as YamlSequenceNode
            ?? Child(root, "buildings") as YamlSequenceNode;
        if (seq == null)
            throw new DataSchemaException("Expected a units or buildings list.");

        foreach (var node in seq)
        {
            var map = (YamlMappingNode)node;
            bool building = forceBuilding || Bool(map, "building", false);
            bool transformable = Bool(map, "transformable", false);
            Fp speed = Has(map, "speed") ? Fixed(map, "speed") : Fp.Zero;

            if (!building && !transformable && speed.Raw <= 0)
                throw new DataSchemaException("make it a building or transformable");
            if (!building && speed.Raw < 0)
                throw new DataSchemaException("speed must be positive");

            string id = Str(map, "id");
            if (id.Length == 0)
                throw new DataSchemaException("unit is missing id");
            if (catalog.Units.ContainsKey(id))
                throw new DataSchemaException($"duplicate id '{id}'");

            var def = new UnitDef
            {
                Id = id,
                Name = Has(map, "name") ? Str(map, "name") : id,
                Faction = Has(map, "faction") ? Str(map, "faction") : "",
                IsBuilding = building,
                Transformable = transformable,
                Domain = ParseDomain(Has(map, "domain") ? Str(map, "domain") : "ground"),
                Role = Has(map, "role") ? Str(map, "role") : (building ? Roles.Refinery : Roles.Combat),
                Shape = Has(map, "shape") ? Str(map, "shape") : (building ? "building" : "box"),
                Cost = Int(map, "cost", 0),
                PerOrder = Int(map, "per_order", 1),
                BuildSeconds = Int(map, "build_s", 0),
                Hp = Int(map, "hp", 1),
                Speed = building ? Fp.Zero : speed,
                Sight = Has(map, "sight") ? Fixed(map, "sight") : Fp.FromInt(12),
                WeaponSlots = Int(map, "weapon_slots", 0),
                Cargo = Int(map, "cargo", 0),
                Footprint = Math.Max(1, Int(map, "footprint", 1)),
                StorageCap = Int(map, "storage_cap", 0),
                BufferCap = Int(map, "buffer", 0),
            };

            if (def.Hp <= 0)
                throw new DataSchemaException($"'{id}' needs hp > 0");

            if (Child(map, "sight_by_target") is YamlMappingNode sights)
            {
                if (Has(sights, "air"))
                    def.SightAir = Fixed(sights, "air");
                if (Has(sights, "ground"))
                    def.SightGround = Fixed(sights, "ground");
                if (Has(sights, "sea"))
                    def.SightSea = Fixed(sights, "sea");
            }

            if (transformable)
            {
                def.TransformSeconds = Has(map, "transform_s")
                    ? Fixed(map, "transform_s")
                    : catalog.Rules.TransformSecondsDefault;
                if (Child(map, "transformed") is YamlMappingNode transformed)
                {
                    def.Transformed = new TransformProfile
                    {
                        Speed = Has(transformed, "speed") ? Fixed(transformed, "speed") : def.Speed,
                        Sight = Has(transformed, "sight") ? Fixed(transformed, "sight") : def.Sight,
                    };
                }
            }

            if (Child(map, "weapons") is YamlSequenceNode weapons)
            {
                foreach (var wnode in weapons)
                {
                    var wmap = (YamlMappingNode)wnode;
                    int damage = Int(wmap, "damage", 0);
                    var weapon = new WeaponDef
                    {
                        Id = Has(wmap, "id") ? Str(wmap, "id") : "weapon",
                        Damage = damage,
                        DamageTroop = Has(wmap, "damage_troop") ? Int(wmap, "damage_troop", damage) : damage,
                        Salvo = Math.Max(1, Int(wmap, "salvo", 1)),
                        SalvoInterval = Has(wmap, "salvo_interval_s") ? Fixed(wmap, "salvo_interval_s") : Fp.Zero,
                        Cooldown = Has(wmap, "cooldown_s") ? Fixed(wmap, "cooldown_s") : Fp.FromInt(1),
                        Range = Has(wmap, "range") ? Fixed(wmap, "range") : Fp.Zero,
                        ProjectileSpeed = Has(wmap, "projectile_speed") ? Fixed(wmap, "projectile_speed") : Fp.FromInt(16),
                        Targets = ParseTargets(wmap),
                    };
                    def.Weapons.Add(weapon);
                }
            }

            if (def.Role == Roles.Hauler && def.Cargo <= 0)
                def.Cargo = catalog.Rules.HaulerCapacity;

            catalog.Units[id] = def;
        }
    }

    private static TargetMask ParseTargets(YamlMappingNode map)
    {
        if (Child(map, "targets") is not YamlSequenceNode seq)
            return TargetMask.Ground;
        TargetMask mask = TargetMask.None;
        foreach (var node in seq)
        {
            string t = Scalar(node).Trim().ToLowerInvariant();
            mask |= t switch
            {
                "ground" => TargetMask.Ground,
                "ship" or "ships" or "sea" => TargetMask.Sea,
                "air" => TargetMask.Air,
                "sub" or "subs" or "submarine" or "submarines" => TargetMask.Sub,
                "troop" or "troops" => TargetMask.Troop,
                "building" or "buildings" => TargetMask.Building,
                _ => throw new DataSchemaException($"Unknown target '{t}'."),
            };
        }
        return mask;
    }

    private static UnitDomain ParseDomain(string text)
    {
        string t = text.Trim().ToLowerInvariant();
        if (t.StartsWith("ground", StringComparison.Ordinal))
            return UnitDomain.Ground;
        return t switch
        {
            "air" => UnitDomain.Air,
            "sea" => UnitDomain.Sea,
            "troop" or "troops" => UnitDomain.Troop,
            _ => throw new DataSchemaException($"Unknown domain '{text}'."),
        };
    }

    private static YamlNode? Child(YamlMappingNode map, string key)
    {
        return map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;
    }

    private static bool Has(YamlMappingNode map, string key) => Child(map, key) != null;

    private static string Scalar(YamlNode node) => ((YamlScalarNode)node).Value ?? "";

    private static string Str(YamlMappingNode map, string key)
    {
        var node = Child(map, key) ?? throw new DataSchemaException($"Missing '{key}'.");
        return Scalar(node);
    }

    private static int Int(YamlMappingNode map, string key, int fallback)
    {
        if (!Has(map, key))
            return fallback;
        return int.Parse(Str(map, key), CultureInfo.InvariantCulture);
    }

    private static Fp Fixed(YamlMappingNode map, string key)
    {
        try
        {
            return Fp.Parse(Str(map, key));
        }
        catch (FormatException ex)
        {
            throw new DataSchemaException(ex.Message);
        }
    }

    private static bool Bool(YamlMappingNode map, string key, bool fallback)
    {
        if (!Has(map, key))
            return fallback;
        string t = Str(map, key).Trim().ToLowerInvariant();
        return t switch
        {
            "true" or "yes" or "1" => true,
            "false" or "no" or "0" => false,
            _ => throw new DataSchemaException($"Bad boolean '{t}'."),
        };
    }
}
