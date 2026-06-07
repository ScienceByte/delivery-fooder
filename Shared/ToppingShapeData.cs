using System;

public readonly record struct ToppingShapeProfile(
    string Name,
    float HalfWidth,
    float HalfDepth,
    float Thickness,
    float Mass,
    float Friction,
    float EdgeGrip
)
{
    public float SlideBoundary => MathF.Max(HalfWidth, HalfDepth) * EdgeGrip;
}

public static partial class ToppingShapeData
{
    public static ToppingShapeProfile Default => new(
        "Default",
        0.9f,
        0.9f,
        0.2f,
        1f,
        0.9f,
        0.95f
    );
}
