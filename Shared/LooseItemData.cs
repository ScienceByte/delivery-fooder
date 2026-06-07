using System;

public readonly record struct LooseItemDefinition(
    string Name,
    float PositionX,
    float PositionY,
    float PositionZ,
    float RotationX,
    float RotationY,
    float RotationZ,
    float ScaleX,
    float ScaleY,
    float ScaleZ,
    float HalfWidth,
    float HalfDepth,
    float MinY,
    float MaxY
)
{
    public float Thickness => MaxY - MinY;
}

public static partial class LooseItemData
{
    public static readonly LooseItemDefinition[] Empty = Array.Empty<LooseItemDefinition>();
}
