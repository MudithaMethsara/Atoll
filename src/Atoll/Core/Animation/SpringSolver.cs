namespace Atoll.Core.Animation;

public sealed class SpringSolver
{
    private const double MaxStepDt = 1.0 / 240.0;
    private const double MaxFrameDt = 0.05;

    private double _stiffness;
    private double _dampingRatio;
    private double _mass;
    private double _dampingCoeff;

    public double Position { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; private set; }

    public double Stiffness
    {
        get => _stiffness;
        set { _stiffness = value; UpdateDampingCoeff(); }
    }

    public double DampingRatio
    {
        get => _dampingRatio;
        set { _dampingRatio = Math.Clamp(value, 0.2, 2.0); UpdateDampingCoeff(); }
    }

    public double Mass
    {
        get => _mass;
        set { _mass = Math.Max(0.1, value); UpdateDampingCoeff(); }
    }

    public double PositionEpsilon { get; set; }
    public double VelocityEpsilon { get; set; }

    public bool IsSettled =>
        Math.Abs(Position - Target) <= PositionEpsilon &&
        Math.Abs(Velocity) <= VelocityEpsilon;

    public SpringSolver(
        double initialValue = 0.0,
        double stiffness = 340.0,
        double dampingRatio = 0.76,
        double mass = 1.0,
        double positionEpsilon = 0.05,
        double velocityEpsilon = 0.25)
    {
        Position = initialValue;
        Target = initialValue;
        Velocity = 0.0;
        _stiffness = stiffness;
        _dampingRatio = Math.Clamp(dampingRatio, 0.2, 2.0);
        _mass = Math.Max(0.1, mass);
        PositionEpsilon = positionEpsilon;
        VelocityEpsilon = velocityEpsilon;
        UpdateDampingCoeff();
    }

    private void UpdateDampingCoeff()
    {
        _dampingCoeff = 2.0 * _dampingRatio * Math.Sqrt(_stiffness * _mass);
    }

    public void SetTarget(double target)
    {
        Target = target;
    }

    public void Impulse(double velocityDelta)
    {
        Velocity += velocityDelta;
    }

    public void Snap(double value)
    {
        Position = value;
        Target = value;
        Velocity = 0.0;
    }

    public bool Step(double dt)
    {
        if (IsSettled)
        {
            Position = Target;
            Velocity = 0.0;
            return false;
        }

        double remaining = Math.Clamp(dt, 0.0, MaxFrameDt);
        if (remaining <= 0.0) return true;

        double c = _dampingCoeff;

        while (remaining > 0.0)
        {
            double step = Math.Min(remaining, MaxStepDt);
            double displacement = Position - Target;
            double springForce = -Stiffness * displacement;
            double dampingForce = -c * Velocity;
            double acceleration = (springForce + dampingForce) / Mass;

            Velocity += acceleration * step;
            Position += Velocity * step;
            remaining -= step;
        }

        if (IsSettled)
        {
            Position = Target;
            Velocity = 0.0;
            return false;
        }

        return true;
    }
}

