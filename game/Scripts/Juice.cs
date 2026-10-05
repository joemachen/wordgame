using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Scoring "ring-up" effects and every number that tunes them. Intensity grows with the play's share of the round's
/// deadline: <see cref="BigPlay"/> and <see cref="HugePlay"/> unlock stronger punches, screen shake and confetti; a
/// play that clears the whole deadline on its own gets the <see cref="StampText"/> stamp.
/// </summary>
public static class Juice
{
    // Tempo: the first step is slow, later steps speed up (Balatro-style), never below the floor.
    public const double FirstStepSeconds = 0.34;
    public const double MinStepSeconds = 0.13;
    public const double StepAcceleration = 0.88;
    public const double FinalCountSeconds = 0.55;

    // Intensity thresholds: the play's total as a fraction of the round's deadline.
    public const double BigPlay = 0.25;
    public const double HugePlay = 0.6;

    // Scale punches (1 = none). Each step adds a little, up to the cap, so long logs build up.
    public const float StepPunch = 1.12f;
    public const float PunchPerStep = 0.025f;
    public const float MaxStepPunch = 1.3f;
    public const float SourcePunch = 1.08f;
    public const float SourcePunchPerStep = 0.3f; // share of the step punch a Desk Item card adds on top
    public const float FinalPunch = 1.25f;
    public const float BigFinalPunch = 1.45f;
    public const float HugeFinalPunch = 1.7f;

    public const float ShakePixels = 12f;
    public const double ShakeSeconds = 0.45;
    public const int ConfettiAmount = 90;
    public const string StampText = "STOP THE PRESSES!";

    // NEW tags on freshly drawn tiles: shown this long (unless the hand is touched first), then faded out.
    public const double NewTagSeconds = 3.0;
    public const double NewTagFadeSeconds = 0.5;

    public enum Level { Normal, Big, Huge }

    public static Level LevelFor(long playTotal, long target) =>
        target <= 0 ? Level.Normal
        : playTotal >= target * HugePlay ? Level.Huge
        : playTotal >= target * BigPlay ? Level.Big
        : Level.Normal;

    /// <summary>Seconds for log step <paramref name="index"/>.</summary>
    public static double StepSeconds(int index) =>
        Math.Max(MinStepSeconds, FirstStepSeconds * Math.Pow(StepAcceleration, index));

    public static float StepScale(int index, Level level) =>
        Math.Min(MaxStepPunch, StepPunch + PunchPerStep * index) + (level == Level.Huge ? 0.08f : level == Level.Big ? 0.04f : 0f);

    /// <summary>Scales a control up and back, around its centre (or its left edge, for left-aligned text).</summary>
    public static void Pop(Control control, float scale, double seconds = 0.2, bool fromLeft = false)
    {
        if (!GodotObject.IsInstanceValid(control))
            return;
        control.PivotOffset = fromLeft ? new Vector2(0, control.Size.Y / 2) : control.Size / 2;
        var tween = control.CreateTween();
        tween.TweenProperty(control, "scale", new Vector2(scale, scale), seconds * 0.4).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(control, "scale", Vector2.One, seconds * 0.6).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
    }

    /// <summary>Tints a control briefly, then fades back to normal.</summary>
    public static void Flash(CanvasItem item, Color color, double seconds = 0.3)
    {
        if (!GodotObject.IsInstanceValid(item))
            return;
        item.Modulate = color;
        item.CreateTween().TweenProperty(item, "modulate", Colors.White, seconds);
    }

    /// <summary>Ticks a label's number from one value to another.</summary>
    public static void CountUp(Label label, decimal from, decimal to, double seconds, string format)
    {
        if (from == to)
        {
            label.Text = to.ToString(format);
            return;
        }
        label.CreateTween().TweenMethod(Callable.From<double>(v => label.Text = ((decimal)v).ToString(format)), (double)from, (double)to, seconds);
    }

    /// <summary>Text that rises and fades from a point on the effects layer.</summary>
    public static void FloatText(Control layer, Vector2 at, string text, Color color, int size = 18)
    {
        var label = UiKit.MakeLabel(text, size, color, HorizontalAlignment.Center);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(label);
        label.ResetSize();
        label.Position = at - new Vector2(label.Size.X / 2, label.Size.Y);
        var tween = label.CreateTween().SetParallel();
        tween.TweenProperty(label, "position:y", label.Position.Y - 46, 0.8).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(label, "modulate:a", 0f, 0.8).SetDelay(0.3);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    /// <summary>Shakes a control around its current position, then puts it back.</summary>
    public static void Shake(Control control, float pixels = ShakePixels, double seconds = ShakeSeconds)
    {
        var home = control.Position;
        var tween = control.CreateTween();
        const int steps = 9;
        for (int i = 0; i < steps; i++)
        {
            float fade = 1f - (float)i / steps;
            var offset = new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0)) * pixels * fade;
            tween.TweenProperty(control, "position", home + offset, seconds / steps);
        }
        tween.TweenProperty(control, "position", home, seconds / steps);
    }

    /// <summary>A one-shot burst of newsprint-coloured confetti on the effects layer.</summary>
    public static void Confetti(Control layer, Vector2 at)
    {
        var particles = new CpuParticles2D
        {
            Position = at,
            Amount = ConfettiAmount,
            OneShot = true,
            Explosiveness = 1f,
            Lifetime = 1.4,
            Direction = Vector2.Up,
            Spread = 75f,
            InitialVelocityMin = 260f,
            InitialVelocityMax = 520f,
            Gravity = new Vector2(0, 700),
            AngularVelocityMin = -360f,
            AngularVelocityMax = 360f,
            ScaleAmountMin = 4f,
            ScaleAmountMax = 8f,
        };
        var ramp = new Gradient();
        ramp.SetColor(0, UiKit.Money);
        ramp.SetColor(1, UiKit.Mult);
        ramp.AddPoint(0.5f, UiKit.Chips);
        particles.ColorInitialRamp = ramp;
        layer.AddChild(particles);
        particles.Emitting = true;
        particles.GetTree().CreateTimer(2.0).Timeout += particles.QueueFree;
    }

    /// <summary>Slams a rotated rubber-stamp banner onto the centre of <paramref name="over"/>.</summary>
    public static void Stamp(Control layer, Control over, string text)
    {
        var panel = UiKit.MakePanel(new Color(0, 0, 0, 0.35f), padding: 14, radius: 6, border: UiKit.Mult, borderWidth: 5);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        var label = UiKit.MakeLabel(text, 54, UiKit.Mult, HorizontalAlignment.Center);
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
        panel.AddChild(label);
        layer.AddChild(panel);
        panel.ResetSize();
        var centre = over.GetGlobalRect().GetCenter();
        panel.Position = centre - panel.Size / 2;
        panel.PivotOffset = panel.Size / 2;
        panel.Rotation = Mathf.DegToRad(-9);
        panel.Scale = new Vector2(2.6f, 2.6f);
        panel.Modulate = new Color(1, 1, 1, 0);
        var tween = panel.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(panel, "scale", Vector2.One, 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenProperty(panel, "modulate:a", 1f, 0.12);
        tween.Chain().TweenInterval(1.0);
        tween.Chain().TweenProperty(panel, "modulate:a", 0f, 0.4);
        tween.Chain().TweenCallback(Callable.From(panel.QueueFree));
    }
}
