using Godot;
using YamlDotNet.RepresentationModel;

namespace FirstClnkrWar;

/// <summary>
/// Presentation-only bindings. Camera speeds never enter the simulation.
/// </summary>
public sealed class InputBindings
{
    public Key PanUp { get; set; } = Key.W;
    public Key PanDown { get; set; } = Key.S;
    public Key PanLeft { get; set; } = Key.A;
    public Key PanRight { get; set; } = Key.D;
    public Key RotateLeft { get; set; } = Key.Q;
    public Key RotateRight { get; set; } = Key.E;
    public Key TiltUp { get; set; } = Key.R;
    public Key TiltDown { get; set; } = Key.F;
    public Key Stop { get; set; } = Key.H;
    public Key AttackMove { get; set; } = Key.Z;
    public Key SwitchPlayer { get; set; } = Key.Tab;
    public Key Pause { get; set; } = Key.Space;
    public Key FocusBase { get; set; } = Key.Home;
    public Key PlaceRefinery { get; set; } = Key.N;
    public Key PlaceCollector { get; set; } = Key.M;

    public bool EdgeScroll { get; set; } = true;
    public float PanSpeed { get; set; } = 40f;
    public float RotateSpeed { get; set; } = 80f;
    public float TiltSpeed { get; set; } = 40f;
    public float ZoomStep { get; set; } = 6f;
    public float MinPitch { get; set; } = 25f;
    public float MaxPitch { get; set; } = 80f;
    public float MinZoom { get; set; } = 14f;
    public float MaxZoom { get; set; } = 220f;
    public float StartPitch { get; set; } = 55f;
    public float StartZoom { get; set; } = 72f;

    public static InputBindings Load(string yaml)
    {
        var bindings = new InputBindings();
        if (string.IsNullOrWhiteSpace(yaml))
            return bindings;

        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode map)
            return bindings;

        bindings.PanUp = KeyOf(map, "pan_up", bindings.PanUp);
        bindings.PanDown = KeyOf(map, "pan_down", bindings.PanDown);
        bindings.PanLeft = KeyOf(map, "pan_left", bindings.PanLeft);
        bindings.PanRight = KeyOf(map, "pan_right", bindings.PanRight);
        bindings.RotateLeft = KeyOf(map, "rotate_left", bindings.RotateLeft);
        bindings.RotateRight = KeyOf(map, "rotate_right", bindings.RotateRight);
        bindings.TiltUp = KeyOf(map, "tilt_up", bindings.TiltUp);
        bindings.TiltDown = KeyOf(map, "tilt_down", bindings.TiltDown);
        bindings.Stop = KeyOf(map, "stop", bindings.Stop);
        bindings.AttackMove = KeyOf(map, "attack_move", bindings.AttackMove);
        bindings.SwitchPlayer = KeyOf(map, "switch_player", bindings.SwitchPlayer);
        bindings.Pause = KeyOf(map, "pause", bindings.Pause);
        bindings.FocusBase = KeyOf(map, "focus_base", bindings.FocusBase);
        bindings.PlaceRefinery = KeyOf(map, "place_refinery", bindings.PlaceRefinery);
        bindings.PlaceCollector = KeyOf(map, "place_collector", bindings.PlaceCollector);

        bindings.EdgeScroll = BoolOf(map, "edge_scroll", bindings.EdgeScroll);
        bindings.PanSpeed = FloatOf(map, "pan_speed", bindings.PanSpeed);
        bindings.RotateSpeed = FloatOf(map, "rotate_speed", bindings.RotateSpeed);
        bindings.TiltSpeed = FloatOf(map, "tilt_speed", bindings.TiltSpeed);
        bindings.ZoomStep = FloatOf(map, "zoom_step", bindings.ZoomStep);
        bindings.MinPitch = FloatOf(map, "min_pitch", bindings.MinPitch);
        bindings.MaxPitch = FloatOf(map, "max_pitch", bindings.MaxPitch);
        bindings.MinZoom = FloatOf(map, "min_zoom", bindings.MinZoom);
        bindings.MaxZoom = FloatOf(map, "max_zoom", bindings.MaxZoom);
        bindings.StartPitch = FloatOf(map, "start_pitch", bindings.StartPitch);
        bindings.StartZoom = FloatOf(map, "start_zoom", bindings.StartZoom);
        return bindings;
    }

    public bool IsDown(Key key) => Input.IsPhysicalKeyPressed(key);

    private static Key KeyOf(YamlMappingNode map, string name, Key fallback)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode(name), out var node))
            return fallback;
        string text = ((YamlScalarNode)node).Value ?? "";
        return Enum.TryParse(text, ignoreCase: true, out Key key) ? key : fallback;
    }

    private static bool BoolOf(YamlMappingNode map, string name, bool fallback)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode(name), out var node))
            return fallback;
        string text = (((YamlScalarNode)node).Value ?? "").Trim().ToLowerInvariant();
        return text switch
        {
            "true" or "yes" or "1" => true,
            "false" or "no" or "0" => false,
            _ => fallback,
        };
    }

    private static float FloatOf(YamlMappingNode map, string name, float fallback)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode(name), out var node))
            return fallback;
        string text = ((YamlScalarNode)node).Value ?? "";
        return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
            ? value
            : fallback;
    }
}
