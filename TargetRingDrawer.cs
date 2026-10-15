using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using PromeRotation.Managers;
using PromeRotation;
using PromeRotation.Spatial.Drawing;
using PromeRotation.Spatial.Geometry;

namespace TargetRangeRing;

internal sealed class TargetRingDrawer : IDisposable
{
    private const string AutoAttackRingId = "TargetRangeRing_Auto";
    private const string MaxAttackRingId = "TargetRangeRing_Max";
    private const string CustomRingId = "TargetRangeRing_Custom";
    private const float AutoAttackDistance = 3.1f;
    private const float MaxAttackDistance = 6f;
    private const float DefaultPlayerHitRadius = 0.5f;
    private const string PlayerRingId = "TargetRangeRing_Player";
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

    private readonly Config _config;
    private readonly CachedRing _autoRing = new(AutoAttackRingId);
    private readonly CachedRing _maxRing = new(MaxAttackRingId);
    private readonly CachedRing _customRing = new(CustomRingId);
    private readonly CachedRing _playerRing = new(PlayerRingId, filled: true);
    private long _nextVfxInitAttempt;

    public TargetRingDrawer(Config config)
    {
        _config = config;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        EventManager.OnBattleEnded += InvalidateDrawings;
    }

    // DrawManager clears its elements on these same events.
    private void OnTerritoryChanged(uint _) => InvalidateDrawings();
    private void InvalidateDrawings()
    {
        _autoRing.Invalidate();
        _maxRing.Invalidate();
        _customRing.Invalidate();
        _playerRing.Invalidate();
    }

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

        var config = _config;
        if (!config.Enabled || !ShouldDisplay(config))
        {
            RemoveTargetRings();
            _playerRing.Remove();
            return;
        }

        // Independent of the selected target, job and target rendering backend.
        try
        {
            UpdatePlayerRing(config);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TargetRangeRing] 玩家判定点绘制失败");
        }

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
            RemoveTargetRings();
            _lastTargetId = 0;
            return;
        }

        var target = Svc.Targets.Target;
        if (target == null || !target.IsValid() || target is not IBattleChara bc || target is IPlayerCharacter)
        {
            RemoveTargetRings();
            _lastTargetId = 0;
            return;
        }

        var hitboxRadius = bc.HitboxRadius;
        if (!float.IsFinite(hitboxRadius) || hitboxRadius <= 0f || hitboxRadius > 200f)
        {
            RemoveTargetRings();
            _lastTargetId = 0;
            return;
        }

        var targetId = bc.GameObjectId;
        if (targetId != _lastTargetId || config.Backend != _lastBackend)
        {
            _lastTargetId = targetId;
            _lastBackend = config.Backend;
            RemoveTargetRings();
        }

        try
        {
            float playerHitRadius = Svc.Objects.LocalPlayer?.HitboxRadius ?? DefaultPlayerHitRadius;
            if (!float.IsFinite(playerHitRadius) || playerHitRadius <= 0f || playerHitRadius > 200f)
                playerHitRadius = DefaultPlayerHitRadius;
            float autoRadius = hitboxRadius + playerHitRadius + AutoAttackDistance;
            float maxRadius = hitboxRadius + playerHitRadius + MaxAttackDistance;
            float? customRadius = config.CustomRingEnabled
                ? hitboxRadius + playerHitRadius + (config.CustomRingDistance * CustomDistanceCalibration)
                : null;
            var center = target.Position;

            if (config.Backend == RenderBackend.VFX)
            {
                UpdateVfxRings(center, autoRadius, maxRadius, customRadius, config);
            }
            else
            {
                var renderer = config.Backend == RenderBackend.DirectX ? RendererType.DirectX : RendererType.ImGui;
                _autoRing.Update(center, autoRadius, config.AutoAttackColor, config.Thickness, renderer);
                _maxRing.Update(center, maxRadius, config.MaxAttackColor, config.Thickness, renderer);
                if (customRadius is { } cr)
                    _customRing.Update(center, cr, config.CustomRingColor, config.Thickness, renderer);
                else
                    _customRing.Remove();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TargetRangeRing] 绘制失败");
        }
    }

    private static bool ShouldDisplay(Config config) =>
        config.ForceDisplay ||
        ((!config.ShowInCombat || Svc.Condition[ConditionFlag.InCombat]) &&
         (!config.ShowInInstance || Svc.Condition[ConditionFlag.BoundByDuty]));

    private void UpdatePlayerRing(Config config)
    {
        var player = Svc.Objects.LocalPlayer;
        if (!config.PlayerRingEnabled || player == null || !player.IsValid() ||
            !float.IsFinite(config.PlayerRingSize) || config.PlayerRingSize <= 0f)
        {
            _playerRing.Remove();
            return;
        }

        _playerRing.Update(player.Position, config.PlayerRingSize, config.PlayerRingColor,
            config.Thickness, RendererType.ImGui);
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
        if (path == newPath && current is { Disposed: false })
        {
            _vfx.SetColor(current, color);
            return current;
        }
        current?.Dispose();
        path = newPath;
        return _vfx.CreateCircle(newPath, center, outer, color);
    }

    private void EnsureVfxReady()
    {
        if (_vfx != null || Environment.TickCount64 < _nextVfxInitAttempt) return;
        _nextVfxInitAttempt = Environment.TickCount64 + 5000;
        _vfx = new VfxManager();
        if (!_vfx.Initialize())
        {
            Svc.Log.Error("[TargetRangeRing] VFX 引擎初始化失败");
            _vfx.Dispose();
            _vfx = null;
            return;
        }
    }

    private void RemoveTargetRings()
    {
        try
        {
            _autoRing.Remove();
            _maxRing.Remove();
            _customRing.Remove();
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
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        EventManager.OnBattleEnded -= InvalidateDrawings;
        RemoveTargetRings();
        _playerRing.Remove();
        _vfx?.Dispose();
        _vfx = null;
    }
    private sealed class CachedRing(string id, bool filled = false)
    {
        private CircleGeometry? _geometry;
        private DrawStyle? _style;
        private bool _registered;
        private RendererType _renderer;

        public void Invalidate() => _registered = false;

        public void Update(Vector3 center, float radius, Vector4 color, float thickness, RendererType renderer)
        {
            bool geometryChanged = _geometry == null || _geometry.Position != center || _geometry.Radius != radius;
            bool styleChanged = _style == null || (filled ? _style.FillColor : _style.StrokeColor) != color || _style.StrokeThickness != thickness;
            if (geometryChanged) _geometry = new CircleGeometry(center, radius);
            if (styleChanged) _style = new DrawStyle(filled ? color : null, filled ? null : color, thickness);
            if (_registered && !geometryChanged && !styleChanged && _renderer == renderer) return;

            // A zero duration means persistent; explicit cleanup owns the lifetime.
            var handle = Plugin.Instance.DrawManager.Add(id, _geometry!, _style!, rendererType: renderer);
            _registered = handle.IsValid;
            _renderer = renderer;
        }

        public void Remove()
        {
            if (!_registered) return;
            Plugin.Instance.DrawManager.Remove(id);
            _registered = false;
        }
    }

}