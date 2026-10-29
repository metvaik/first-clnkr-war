using Godot;

namespace FirstClnkrWar;

public partial class RtsCamera : Node3D
{
    public float YawDegrees { get; set; } = 30f;
    public float PitchDegrees { get; set; } = 55f;
    public float Distance { get; set; } = 72f;

    private Node3D _pitch = null!;
    private Camera3D _camera = null!;

    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _pitch = new Node3D { Name = "Pitch" };
        AddChild(_pitch);
        _camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Near = 0.15f,
            Far = 3000f,
        };
        _pitch.AddChild(_camera);
        ApplyRig();
    }

    public void ApplyRig()
    {
        RotationDegrees = new Vector3(0f, YawDegrees, 0f);
        _pitch.RotationDegrees = new Vector3(-PitchDegrees, 0f, 0f);
        _camera.Position = new Vector3(0f, 0f, Distance);
    }

    public void Pan(float right, float back, float distance)
    {
        var basis = GlobalTransform.Basis;
        var side = basis.X;
        side.Y = 0f;
        var away = basis.Z;
        away.Y = 0f;
        if (side.LengthSquared() > 0.0001f)
            side = side.Normalized();
        if (away.LengthSquared() > 0.0001f)
            away = away.Normalized();
        var next = Position + side * right * distance + away * back * distance;
        next.Y = 0f;
        Position = next;
    }

    public void ClampToMap(float mapSize)
    {
        var p = Position;
        p.X = Mathf.Clamp(p.X, 0f, mapSize);
        p.Z = Mathf.Clamp(p.Z, 0f, mapSize);
        p.Y = 0f;
        Position = p;
    }
}
