using System.Numerics;
using Dalamud.Configuration;

namespace TargetRangeRing;

public sealed class Config : IPluginConfiguration
{
    public int Version { get; set; } = 9;

    public bool Enabled { get; set; } = true;
    public float Thickness { get; set; } = 1f;

    public Vector4 AutoAttackColor { get; set; } = new(1f, 0.8f, 0f, 0.8f);
    public Vector4 MaxAttackColor { get; set; } = new(0.3f, 0.7f, 0.3f, 0.8f);
}
