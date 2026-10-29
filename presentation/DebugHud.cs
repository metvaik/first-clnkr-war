using Godot;

namespace FirstClnkrWar;

public partial class DebugHud : CanvasLayer
{
    public event Action<int>? PlayerChanged;

    private OptionButton _player = null!;
    private Label _body = null!;
    private bool _suppress;

    public override void _Ready()
    {
        var panel = new PanelContainer
        {
            Position = new Vector2(12, 12),
            CustomMinimumSize = new Vector2(470, 0),
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.07f, 0.84f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 10,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);

        var title = MakeLabel(18);
        title.Text = "The First C.L.N.K.R. War  —  Phase 1";
        box.AddChild(title);

        _player = new OptionButton();
        _player.AddItem("Slot 1   TDO", 0);
        _player.AddItem("Slot 2   C.L.N.K.R.", 1);
        _player.FocusMode = Control.FocusModeEnum.None;
        _player.ItemSelected += OnPlayer;
        box.AddChild(_player);

        _body = MakeLabel(15);
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _body.CustomMinimumSize = new Vector2(446, 0);
        box.AddChild(_body);

        AddChild(panel);
    }

    public void SetPlayerSilent(int player)
    {
        if (_player == null)
            return;
        _suppress = true;
        _player.Selected = player;
        _suppress = false;
    }

    public void SetText(string text)
    {
        if (_body != null)
            _body.Text = text;
    }

    private void OnPlayer(long index)
    {
        if (_suppress)
            return;
        PlayerChanged?.Invoke((int)index);
    }

    private static Label MakeLabel(int size)
    {
        var label = new Label();
        label.AddThemeColorOverride("font_color", new Color(0.93f, 0.94f, 0.9f));
        label.AddThemeFontSizeOverride("font_size", size);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }
}
