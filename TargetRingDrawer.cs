using System;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using PromeRotation;
using PromeRotation.Spatial.Drawing;
using PromeRotation.Spatial.Geometry;

namespace TargetRangeRing;

internal sealed class TargetRingDrawer : IDisposable
{
    private const float DurationMs = 3600000f;
    private const string AutoAttackRingId = "TargetRangeRing_Auto";
    private const string MaxAttackRingId = "TargetRangeRing_Max";
    private const float AutoAttackDistance = 3f;
    private const float MaxAttackDistance = 5f;

    private bool _disposed;
    private ulong _lastTargetId;

    private static readonly FieldInfo FillColorField = typeof(DrawStyle)
        .GetField("<FillColor>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo StrokeColorField = typeof(DrawStyle)
        .GetField("<StrokeColor>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo StrokeThicknessField = typeof(DrawStyle)
        .GetField("<StrokeThickness>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public void Update()
    {
        if (_disposed || Plugin.Instance == null) return;

        var config = Svc.PluginInterface.GetPluginConfig() as Config;
        if (config == null) return;

        var target = Svc.Targets.Target;
        if (target == null || !target.IsValid())
        {
            if (_lastTargetId != 0) RemoveAllRings();
            _lastTargetId = 0;
            return;
        }

        if (target is not IBattleChara bc)
        {
            if (_lastTargetId != 0) RemoveAllRings();
            _lastTargetId = 0;
            return;
        }

        if (target is IPlayerCharacter)
        {
            if (_lastTargetId != 0) RemoveAllRings();
            _lastTargetId = 0;
            return;
        }

        var hitboxRadius = bc.HitboxRadius;
        if (hitboxRadius <= 0f || hitboxRadius > 200f)
        {
            if (_lastTargetId != 0) RemoveAllRings();
            _lastTargetId = 0;
            return;
        }

        var targetId = bc.GameObjectId;
        if (targetId != _lastTargetId)
        {
            RemoveAllRings();
            _lastTargetId = targetId;
        }

        if (!config.Enabled)
        {
            RemoveAllRings();
            return;
        }

        try
        {
            RemoveAllRings();

            float baseRadius = GetBaseRadius(bc, hitboxRadius);
            float autoAttackCenter = baseRadius + AutoAttackDistance;
            float maxAttackCenter = baseRadius + MaxAttackDistance;
            float halfThickness = config.Thickness * 0.05f;

            var targetPos = target.Position;
            var groundPos = new Vector3(targetPos.X, targetPos.Y - baseRadius, targetPos.Z);

            var autoGeometry = new DonutGeometry(groundPos, autoAttackCenter - halfThickness, autoAttackCenter + halfThickness);
            var autoStyle = CreateFillStyle(config.AutoAttackColor);
            Plugin.Instance.DrawManager.Add(AutoAttackRingId, autoGeometry, autoStyle, durationMs: DurationMs, rendererType: RendererType.ImGui);

            var maxGeometry = new DonutGeometry(groundPos, maxAttackCenter - halfThickness, maxAttackCenter + halfThickness);
            var maxStyle = CreateFillStyle(config.MaxAttackColor);
            Plugin.Instance.DrawManager.Add(MaxAttackRingId, maxGeometry, maxStyle, durationMs: DurationMs, rendererType: RendererType.ImGui);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TargetRangeRing] 绘制失败");
        }
    }

    private static unsafe float GetBaseRadius(IBattleChara target, float hitboxRadius)
    {
        try
        {
            var addr = target.Address;
            if (addr != IntPtr.Zero)
            {
                var bc = (BattleChara*)addr;
                float radiusTrue = bc->GetRadius(true);
                if (radiusTrue > 0f && radiusTrue < 500f)
                {
                    return radiusTrue;
                }
            }
        }
        catch
        {
        }

        return hitboxRadius;
    }

    private static DrawStyle CreateFillStyle(Vector4 color)
    {
        object boxed = DrawStyle.Safe;

        FillColorField.SetValue(boxed, color);
        StrokeColorField.SetValue(boxed, null);
        StrokeThicknessField.SetValue(boxed, 0f);

        return (DrawStyle)boxed;
    }

    private static void RemoveAllRings()
    {
        try
        {
            Plugin.Instance.DrawManager.Remove(AutoAttackRingId);
            Plugin.Instance.DrawManager.Remove(MaxAttackRingId);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveAllRings();
    }
}
