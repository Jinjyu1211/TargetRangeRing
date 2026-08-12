using System;
using System.Collections.Generic;
using System.Numerics;
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

    private static readonly HashSet<uint> AllowedJobs = new()
    {
        19,  // PLD
        21,  // WAR
        32,  // DRK
        37,  // GNB
        20,  // MNK
        22,  // DRG
        30,  // NIN
        34,  // SAM
        35,  // RPR
        41,  // VPR
    };

    private bool _disposed;
    private ulong _lastTargetId;
    private uint _lastJobId;
    private bool _loggedJobCheck;
    private bool _loggedDrawCheck;

    public void Update()
    {
        if (_disposed || Plugin.Instance == null) return;

        var config = Svc.PluginInterface.GetPluginConfig() as Config;
        if (config == null) return;

        var classJobRowId = Svc.PlayerState.ClassJob.RowId;
        if (classJobRowId != _lastJobId)
        {
            _lastJobId = classJobRowId;
            _loggedJobCheck = false;
        }

        if (!_loggedJobCheck)
        {
            _loggedJobCheck = true;
            Svc.Log.Info($"[TargetRangeRing] JobId={classJobRowId}, Allowed={AllowedJobs.Contains(classJobRowId)}, IsLoaded={Svc.PlayerState.IsLoaded}");
        }

        if (classJobRowId == 0 || !AllowedJobs.Contains(classJobRowId))
        {
            if (_lastTargetId != 0) RemoveAllRings();
            _lastTargetId = 0;
            return;
        }

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
            var baseRadius = GetBaseRadius(bc, hitboxRadius);
            float autoAttackRadius = hitboxRadius + AutoAttackDistance;
            float maxAttackRadius = hitboxRadius + MaxAttackDistance;

            var targetPos = target.Position;
            var groundPos = new Vector3(targetPos.X, targetPos.Y, targetPos.Z);

            if (!_loggedDrawCheck)
            {
                _loggedDrawCheck = true;
                Svc.Log.Info($"[TargetRangeRing] targetId={targetId}, pos=({targetPos.X:F1},{targetPos.Y:F1},{targetPos.Z:F1}), ground=({groundPos.X:F1},{groundPos.Y:F1},{groundPos.Z:F1}), hitbox={hitboxRadius:F1}, baseR={baseRadius:F1}, autoR={autoAttackRadius:F1}, maxR={maxAttackRadius:F1}");
            }

            RemoveAllRings();

            var autoGeometry = new CircleGeometry(groundPos, autoAttackRadius);
            var autoStyle = new DrawStyle(null, config.AutoAttackColor, config.Thickness);
            Plugin.Instance.DrawManager.Add(AutoAttackRingId, autoGeometry, autoStyle, durationMs: DurationMs, rendererType: RendererType.ImGui);

            var maxGeometry = new CircleGeometry(groundPos, maxAttackRadius);
            var maxStyle = new DrawStyle(null, config.MaxAttackColor, config.Thickness);
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
                float radiusFalse = bc->GetRadius(false);
                Svc.Log.Info($"[TargetRangeRing] GetRadius(true)={radiusTrue:F1}, GetRadius(false)={radiusFalse:F1}, HitboxRadius={hitboxRadius:F1}");
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
