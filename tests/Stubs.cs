using System.Numerics;
namespace Dalamud.Configuration { public interface IPluginConfiguration { int Version { get; set; } } }
namespace Dalamud.Game.ClientState.Conditions { public enum ConditionFlag { InCombat, BoundByDuty } }
namespace Dalamud.Game.ClientState.Objects.Types {
    public interface IGameObject { Vector3 Position { get; } float HitboxRadius { get; } ulong GameObjectId { get; } bool IsValid(); }
    public interface IBattleChara : IGameObject { }
    public class Battle : IBattleChara {
        public Vector3 Position { get; set; } public float HitboxRadius { get; set; } = 1; public ulong GameObjectId { get; set; } = 1;
        public bool IsValid() => true;
    }
}
namespace Dalamud.Game.ClientState.Objects.SubKinds {
    public interface IPlayerCharacter : Dalamud.Game.ClientState.Objects.Types.IBattleChara { }
    public class Player : Dalamud.Game.ClientState.Objects.Types.Battle, IPlayerCharacter { }
}
namespace PromeRotation.Spatial.Geometry {
    public sealed class CircleGeometry(Vector3 position, float radius) { public Vector3 Position { get; } = position; public float Radius { get; } = radius; }
}
namespace PromeRotation.Spatial.Drawing {
    public enum RendererType { ImGui, DirectX }
    public sealed record DrawStyle(Vector4? FillColor, Vector4? StrokeColor, float StrokeThickness);
    public readonly record struct DrawHandle(string Id) { public bool IsValid => !string.IsNullOrEmpty(Id); }
    public sealed record Item(PromeRotation.Spatial.Geometry.CircleGeometry Geometry, DrawStyle Style, RendererType Renderer);
    public class DrawManager {
        public Dictionary<string, Item> Items { get; } = new(); public int Adds;
        public DrawHandle Add(string id, PromeRotation.Spatial.Geometry.CircleGeometry geometry, DrawStyle style, RendererType rendererType) {
            Adds++; Items[id] = new(geometry, style, rendererType); return new(id);
        }
        public void Remove(string id) => Items.Remove(id);
    }
}
namespace PromeRotation { public class Plugin { public static Plugin Instance { get; } = new(); public Spatial.Drawing.DrawManager DrawManager { get; } = new(); } }
namespace PromeRotation.Managers {
    public static class EventManager { public static event Action? OnBattleEnded; public static void End() { PromeRotation.Plugin.Instance.DrawManager.Items.Clear(); OnBattleEnded?.Invoke(); } }
}
namespace ECommons.DalamudServices {
    using Dalamud.Game.ClientState.Objects.Types;
    using Dalamud.Game.ClientState.Objects.SubKinds;
    using Dalamud.Game.ClientState.Conditions;
    public static class Svc {
        public static object PluginInterface { get; } = new();
        public static State PlayerState { get; } = new(); public static TargetState Targets { get; } = new();
        public static ObjectState Objects { get; } = new(); public static Conditions Condition { get; } = new();
        public static Client ClientState { get; } = new(); public static Logger Log { get; } = new();
    }
    public class State { public Job ClassJob { get; } = new(); public bool IsLoaded => true; }
    public class Job { public uint RowId { get; set; } }
    public class TargetState { public IGameObject? Target { get; set; } }
    public class ObjectState { public IPlayerCharacter? LocalPlayer { get; set; } }
    public class Conditions { public bool this[ConditionFlag flag] { get => Values.GetValueOrDefault(flag); set => Values[flag] = value; } private Dictionary<ConditionFlag,bool> Values { get; } = new(); }
    public class Client { public event Action<uint>? TerritoryChanged; public void Change() { PromeRotation.Plugin.Instance.DrawManager.Items.Clear(); TerritoryChanged?.Invoke(1); } }
    public class Logger { public void Info(string message) { } public void Error(string message) { throw new Exception(message); } public void Error(Exception ex, string message) { throw new Exception(message, ex); } }
}
namespace TargetRangeRing {
    // Native VFX is outside these checks; target/player drawing uses the production drawer.
    public class VfxHandle : IDisposable { public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
    public class VfxManager : IDisposable {
        public bool IsReady => true; public bool Initialize() => true;
        public string GetOrRegisterDonut(float inner, float outer) => "donut";
        public VfxHandle CreateCircle(string path, Vector3 center, float radius, Vector4 color) => new();
        public void SetColor(VfxHandle handle, Vector4 color) { } public void SetMatrix(VfxHandle handle, Vector3 center, float radius) { } public void Dispose() { }
    }
}
