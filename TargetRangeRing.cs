using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using PromeRotation.Plugins;
using PromeRotation.Spatial.Drawing;

namespace TargetRangeRing;

[PromePlugin(
    id: "target.range.ring",
    name: "TargetRangeRing",
    author: "Jinyu",
    description: "在当前目标脚下绘制自动攻击圈、最大攻击圈与自定义距离圈",
    version: "0.4.0")]
public sealed class TargetRangeRingPlugin : IPromePlugin, IDisposable
{
    private TargetRingDrawer? _drawer;
    private Config? _config;
    private bool _disposed;

    public void Initialize()
    {
        _config = Svc.PluginInterface.GetPluginConfig() as Config ?? new Config();
        _drawer = new TargetRingDrawer();
        Svc.Framework.Update += OnUpdate;
        Svc.Log.Information("[TargetRangeRing] 初始化成功");
    }

    public void DrawConfigUI()
    {
        if (_config is not { } config) return;

        ImGui.TextUnformatted("目标攻击范围圈");
        ImGui.TextDisabled("自动攻击 3 米，最大攻击 6 米，自定义判定圈可调");
        ImGui.Separator();

        var enabled = config.Enabled;
        if (ImGui.Checkbox("启用", ref enabled))
        {
            config.Enabled = enabled;
            SaveConfig();
        }

        var thickness = config.Thickness;
        if (ImGui.SliderFloat("圈线粗细", ref thickness, 0.1f, 10f))
        {
            config.Thickness = thickness;
            SaveConfig();
        }

        var vfxThickness = config.VfxThickness;
        if (ImGui.SliderFloat("VFX 圆环粗细(米)", ref vfxThickness, 0.05f, 3f))
        {
            config.VfxThickness = vfxThickness;
            SaveConfig();
        }

        var backendLabels = new[] { "ImGui", "DirectX", "VFX" };
        var backendValues = new[] { RenderBackend.ImGui, RenderBackend.DirectX, RenderBackend.VFX };
        var backendIndex = Array.IndexOf(backendValues, config.Backend);
        if (backendIndex < 0) backendIndex = 0;
        if (ImGui.Combo("渲染方式", ref backendIndex, backendLabels, backendLabels.Length))
        {
            config.Backend = backendValues[backendIndex];
            SaveConfig();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("自动攻击圈");

        var autoColor = config.AutoAttackColor;
        if (ImGui.ColorEdit4("颜色##auto", ref autoColor))
        {
            config.AutoAttackColor = autoColor;
            SaveConfig();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("最大攻击圈");

        var maxColor = config.MaxAttackColor;
        if (ImGui.ColorEdit4("颜色##max", ref maxColor))
        {
            config.MaxAttackColor = maxColor;
            SaveConfig();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("自定义攻击判定圈");

        var customEnabled = config.CustomRingEnabled;
        if (ImGui.Checkbox("启用##custom", ref customEnabled))
        {
            config.CustomRingEnabled = customEnabled;
            SaveConfig();
        }

        var customDistance = config.CustomRingDistance;
        if (ImGui.SliderFloat("距离(米)", ref customDistance, 0f, 30f))
        {
            config.CustomRingDistance = customDistance;
            SaveConfig();
        }

        var customColor = config.CustomRingColor;
        if (ImGui.ColorEdit4("颜色##custom", ref customColor))
        {
            config.CustomRingColor = customColor;
            SaveConfig();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Svc.Framework.Update -= OnUpdate;
        _drawer?.Dispose();
        _drawer = null;

        Svc.Log.Information("[TargetRangeRing] 已卸载");
    }

    private void OnUpdate(IFramework framework)
    {
        _drawer?.Update();
    }

    private void SaveConfig()
    {
        try
        {
            Svc.PluginInterface.SavePluginConfig(_config);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TargetRangeRing] 保存配置失败");
        }
    }
}