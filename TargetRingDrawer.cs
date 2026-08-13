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
    private const string CustomRingId = "TargetRangeRing_Custom";
    private const float AutoAttackDistance = 3.1f;
    private const float MaxAttackDistance = 6f;
    private const float PlayerHitRadius = 0.5f;
    // 校准系数：由自动圈(3.1→3y)、最大圈(6→6y)、自定义(22→23y)三个条件拟合，
    // 使自定义圈设置值直接对应游戏内实际距离(yalm)。
    private const float CustomDistanceCalibration = 23f / 22f;

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
        39,  // RPR
        41,  // VPR
    };

    private bool _disposed;
    private ulong _lastTargetId;
    private uint _lastJobId;
    private bool _loggedJobCheck;
    private RenderBackend _lastBackend = RenderBackend.ImGui;

    private VfxManager? _vfx;
    private VfxHandle? _autoVfx;
    private VfxHandle? _maxVfx;
    private VfxHandle? _customVfx;
    private string? _autoPath;
    private string? _maxPath;
    private string? _customPath;

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
            RemoveAllRings();
            _lastTargetId = 0;
            return;
        }

        var target = Svc.Targets.Target;
        if (target == null || !target.IsValid() || target is not IBattleChara bc || target is IPlayerCharacter)
        {
            RemoveAllRings();
            _lastTargetId = 0;
            return;
        }

        var hitboxRadius = bc.HitboxRadius;
        if (hitboxRadius <= 0f || hitboxRadius > 200f)
        {
            RemoveAllRings();
            _lastTargetId = 0;
            return;
        }

        var targetId = bc.GameObjectId;
        if (targetId != _lastTargetId || config.Backend != _lastBackend)
        {
            _lastTargetId = targetId;
            _lastBackend = config.Backend;
            RemoveAllRings();
        }

        if (!config.Enabled)
        {
            RemoveAllRings();
            return;
        }

        try
        {
            float autoRadius = hitboxRadius + PlayerHitRadius + AutoAttackDistance;
            float maxRadius = hitboxRadius + PlayerHitRadius + MaxAttackDistance;
            float? customRadius = config.CustomRingEnabled
                ? hitboxRadius + PlayerHitRadius + (config.CustomRingDistance * CustomDistanceCalibration)
                : null;
            var center = target.Position;

            if (config.Backend == RenderBackend.VFX)
            {
                UpdateVfxRings(center, autoRadius, maxRadius, customRadius, config);
            }
            else
            {
                var autoGeometry = new CircleGeometry(center, autoRadius);
                var autoStyle = new DrawStyle(null, config.AutoAttackColor, config.Thickness);
                Plugin.Instance.DrawManager.Add(AutoAttackRingId, autoGeometry, autoStyle, durationMs: DurationMs,
                    rendererType: config.Backend == RenderBackend.DirectX ? RendererType.DirectX : RendererType.ImGui);

                var maxGeometry = new CircleGeometry(center, maxRadius);
                var maxStyle = new DrawStyle(null, config.MaxAttackColor, config.Thickness);
                Plugin.Instance.DrawManager.Add(MaxAttackRingId, maxGeometry, maxStyle, durationMs: DurationMs,
                    rendererType: config.Backend == RenderBackend.DirectX ? RendererType.DirectX : RendererType.ImGui);

                if (customRadius is { } cr)
                {
                    var customGeometry = new CircleGeometry(center, cr);
                    var customStyle = new DrawStyle(null, config.CustomRingColor, config.Thickness);
                    Plugin.Instance.DrawManager.Add(CustomRingId, customGeometry, customStyle, durationMs: DurationMs,
                        rendererType: config.Backend == RenderBackend.DirectX ? RendererType.DirectX : RendererType.ImGui);
                }
                else
                {
                    Plugin.Instance.DrawManager.Remove(CustomRingId);
                }
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TargetRangeRing] 绘制失败");
        }
    }

    private void UpdateVfxRings(Vector3 center, float autoRadius, float maxRadius, float? customRadius, Config config)
    {
        EnsureVfxReady();
        if (_vfx == null || !_vfx.IsReady) return;

        _autoVfx = EnsureRing(_autoVfx, ref _autoPath, autoRadius, config.VfxThickness, config.AutoAttackColor, center);
        _maxVfx = EnsureRing(_maxVfx, ref _maxPath, maxRadius, config.VfxThickness, config.MaxAttackColor, center);

        if (customRadius is { } cr)
        {
            _customVfx = EnsureRing(_customVfx, ref _customPath, cr, config.VfxThickness, config.CustomRingColor, center);
            if (_customVfx != null) _vfx.SetMatrix(_customVfx, center, cr);
        }
        else
        {
            _customVfx?.Dispose();
            _customVfx = null;
            _customPath = null;
        }

        if (_autoVfx != null) _vfx.SetMatrix(_autoVfx, center, autoRadius);
        if (_maxVfx != null) _vfx.SetMatrix(_maxVfx, center, maxRadius);
    }

    private VfxHandle? EnsureRing(VfxHandle? current, ref string? path, float outer, float thickness, Vector4 color, Vector3 center)
    {
        string? newPath = _vfx!.GetOrRegisterDonut(Math.Max(0f, outer - thickness), outer);
        if (newPath == null)
        {
            current?.Dispose();
            path = null;
            return null;
        }
        if (path == newPath) return current;
        current?.Dispose();
        path = newPath;
        return _vfx.CreateCircle(newPath, center, outer, color);
    }

    private void EnsureVfxReady()
    {
        if (_vfx != null) return;
        _vfx = new VfxManager();
        if (!_vfx.Initialize())
        {
            Svc.Log.Error("[TargetRangeRing] VFX 引擎初始化失败");
            _vfx.Dispose();
            _vfx = null;
            return;
        }
    }

    private void RemoveAllRings()
    {
        try
        {
            Plugin.Instance.DrawManager.Remove(AutoAttackRingId);
            Plugin.Instance.DrawManager.Remove(MaxAttackRingId);
            Plugin.Instance.DrawManager.Remove(CustomRingId);
        }
        catch
        {
        }

        _autoVfx?.Dispose();
        _autoVfx = null;
        _maxVfx?.Dispose();
        _maxVfx = null;
        _customVfx?.Dispose();
        _customVfx = null;
        _autoPath = null;
        _maxPath = null;
        _customPath = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveAllRings();
        _vfx?.Dispose();
        _vfx = null;
    }
}