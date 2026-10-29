using Godot;

namespace FirstClnkrWar;

public partial class RtsCamera : Node3D
{
    public float YawDegrees { get; set; } = 30f;
    public float PitchDegrees { get; set; } = 55f;
    public float Distance { get; set; } = 72f;

    private Node3D? _pitch;
    private Camera3D? _camera;

    public Camera3D Camera
    {
        get
        {
            EnsureRig();
            return _camera!;
        }
    }

    public override void _Ready()
    {
        EnsureRig();
    }

    /// <summary>
    /// Builds the yaw/pitch/camera nodes as soon as the rig exists.
    /// Child _Ready can run after the parent finishes, so GameRoot must not
    /// wait for that callback before it reads the camera.
    /// </summary>
    public void EnsureRig()
    {
        if (_pitch != null && _camera != null)
        {
            ApplyRig();
            return;
        }

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
        if (_pitch == null || _camera == null)
            return;
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
