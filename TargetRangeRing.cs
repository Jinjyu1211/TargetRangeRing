using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using PromeRotation.Plugins;

namespace TargetRangeRing;

[PromePlugin(
    id: "target.range.ring",
    name: "TargetRangeRing",
    author: "Jinyu",
    description: "在当前目标脚下绘制自动攻击圈与最大攻击圈",
    version: "0.1.0")]
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
        ImGui.TextDisabled("自动攻击 3 米，最大攻击 5 米");
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
