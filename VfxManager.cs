using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.System.Resource;

namespace TargetRangeRing;

/// <summary>基于游戏原生 VFX(omen) 引擎的圆环渲染器，移植自 mDraw。</summary>
public unsafe sealed class VfxManager : IDisposable
{
    private const string VfxBase = "vfx/omen/eff/mdraw";
    public const string CirclePath = VfxBase + "/customCircle.avfx";

    private const int ParamBlockSize = 416;
    private const int ResourceLimit = 512;

    private delegate nint CreateVfxFn(nint path, nint param, int type, int unk, float x, float y, float z, float dx, float dy, float dz, float radius, float f1, int i1);
    private delegate nint InitVfxParamFn(nint paramBlock);
    private delegate nint SetVfxP1Fn(nint handle, [MarshalAs(UnmanagedType.LPStr)] string name);
    private delegate nint SetVfxP2Fn(nint handle, [MarshalAs(UnmanagedType.LPStr)] string name);
    private delegate nint SetOmenColorFn(nint handle, float r, float g, float b, float a);
    private delegate nint SetOmenMatrixFn(nint handle, nint matrix);
    private delegate nint RemoveOmenFn(nint handle, int count);
    private delegate nint GetResourceSyncFn(nint rm, nint a, nint b, nint c, nint path, nint e);
    private delegate nint VfxResourcesLoadFn(nint data, nint buffer, uint length, nint res);
    private delegate nint VfxResourcesSetupCompleteFn(nint rm);

    private readonly object _lock = new();
    private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);

    private CreateVfxFn? _createVfx;
    private InitVfxParamFn? _initVfxParam;
    private SetVfxP1Fn? _setVfxP1;
    private SetVfxP2Fn? _setVfxP2;
    private SetOmenColorFn? _setOmenColor;
    private SetOmenMatrixFn? _setOmenMatrix;
    private RemoveOmenFn? _removeOmen;
    private GetResourceSyncFn? _getResourceSync;
    private VfxResourcesLoadFn? _vfxResourcesLoad;
    private VfxResourcesSetupCompleteFn? _vfxResourcesSetupComplete;

    private nint _resourceManager;
    private bool _disposed;

    public bool IsReady { get; private set; }

    public bool Initialize()
    {
        try
        {
            if (!Resolve(out _createVfx, CreateVfxPattern, true)
                || !Resolve(out _initVfxParam, InitVfxParamPattern, true)
                || !Resolve(out _setVfxP1, SetVfxP1Pattern, true)
                || !Resolve(out _setVfxP2, SetVfxP2Pattern, true)
                || !Resolve(out _setOmenColor, SetOmenColorPattern, false)
                || !Resolve(out _setOmenMatrix, SetOmenMatrixPattern, false)
                || !Resolve(out _removeOmen, RemoveOmenPattern, true)
                || !Resolve(out _getResourceSync, GetResourceSyncPattern, true)
                || !Resolve(out _vfxResourcesLoad, VfxResourcesLoadPattern, false)
                || !Resolve(out _vfxResourcesSetupComplete, VfxResourcesSetupCompletePattern, false))
            {
                return false;
            }

            _resourceManager = (nint)ResourceManager.Instance();
            if (_resourceManager == IntPtr.Zero)
            {
                Svc.Log.Error("[TargetRangeRing] ResourceManager.Instance() returned null");
                return false;
            }

            IsReady = true;
            Svc.Log.Information("[TargetRangeRing] VfxManager initialized");
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TargetRangeRing] VfxManager initialization failed");
            return false;
        }
    }

    public byte[] LoadEmbeddedCircle() =>
        ReadEmbedded("TargetRangeRing.Resources.tmp_circle");

    public byte[] LoadEmbeddedDonut() =>
        ReadEmbedded("TargetRangeRing.Resources.tmp_donut");

    /// <summary>获取(或注册)指定内/外半径的空心圆环模板路径，供 CreateCircle 使用。</summary>
    public string? GetOrRegisterDonut(float inner, float outer)
    {
        if (_disposed || !IsReady || outer <= 0f) return null;
        float num = Math.Clamp(RoundTo(inner / outer, 0.005f), 0.001f, 0.999f);
        string path = VfxBase + "/customDonut" + Format(num) + ".avfx";
        lock (_lock)
        {
            if (_registered.Contains(path)) return path;
            if (_registered.Count >= ResourceLimit) return null;
            try
            {
                byte[] data = BuildDonutTemplate(LoadEmbeddedDonut(), num);
                if (!Register(path, data)) return null;
                _registered.Add(path);
                Svc.Log.Information("[TargetRangeRing] Registered donut template {Path} (inner/outer={Num})", path, num);
                return path;
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, "[TargetRangeRing] Failed to build donut template");
                return null;
            }
        }
    }

    /// <summary>按 mDraw 的字节偏移修改 donut 模板，用内/外半径比率控制圆环粗细(完整圆)。</summary>
    private static byte[] BuildDonutTemplate(byte[] src, float num)
    {
        float mid = 0.5f * (1f - num) / (1f + num);
        byte[] full = BitConverter.GetBytes(1f);          // 完整圆(角度 2π)
        byte[] ratio = BitConverter.GetBytes(mid);        // 内/外半径比率换算值
        byte[] scale = BitConverter.GetBytes(1f / (0.5f + mid));
        byte[] array = src.ToArray();
        BlockCopy(scale, array, 388);
        BlockCopy(scale, array, 412);
        BlockCopy(full, array, 6044);
        BlockCopy(ratio, array, 6088);
        BlockCopy(full, array, 8772);
        BlockCopy(ratio, array, 8816);
        BlockCopy(full, array, 11500);
        BlockCopy(ratio, array, 11544);
        return array;
    }

    private static void BlockCopy(byte[] val, byte[] dst, int offset) =>
        Buffer.BlockCopy(val, 0, dst, offset, val.Length);

    private static float RoundTo(float value, float step) =>
        MathF.Round(value / step) * step;

    private static string Format(float value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture)
              .Replace("-", "m", StringComparison.Ordinal)
              .Replace(".", "p", StringComparison.Ordinal);

    /// <summary>注册 avfx 模板资源，供 CreateCircle 使用。</summary>
    public bool RegisterResource(string path, byte[] data)
    {
        if (_disposed || !IsReady) return false;
        lock (_lock)
        {
            if (_registered.Contains(path)) return true;
            if (_registered.Count >= ResourceLimit) return false;

            if (!Register(path, data))
            {
                Svc.Log.Warning("[TargetRangeRing] RegisterResource failed for {Path}", path);
                return false;
            }
            _registered.Add(path);
            Svc.Log.Information("[TargetRangeRing] RegisterResource OK for {Path} ({Len} bytes)", path, data.Length);
            return true;
        }
    }

    /// <summary>创建圆环 VFX，返回句柄。位置/半径由后续 SetMatrix 控制。</summary>
    public VfxHandle? CreateCircle(string path, Vector3 center, float radius, Vector4 color)
    {
        if (_disposed || !IsReady
            || _createVfx == null || _initVfxParam == null
            || _setVfxP1 == null || _setVfxP2 == null || _setOmenColor == null)
        {
            return null;
        }

        nint pathPtr = IntPtr.Zero;
        nint paramBlock = IntPtr.Zero;
        nint matrixPtr = IntPtr.Zero;
        nint handle = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToHGlobalAnsi(path);
            paramBlock = Marshal.AllocHGlobal(ParamBlockSize);
            matrixPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Matrix4x4>());

            nint initParams = _initVfxParam(paramBlock);
            handle = _createVfx(pathPtr, initParams, 2, 0,
                center.X, center.Y, center.Z,
                radius, 10f, radius,
                radius, 1f, -1);

            if (handle == IntPtr.Zero)
            {
                Free(pathPtr, paramBlock, matrixPtr);
                Svc.Log.Warning("[TargetRangeRing] CreateVfx returned null handle for {Path}", path);
                return null;
            }

            _setVfxP1(handle, "1");
            _setVfxP2(handle, "1");

            var vfx = new VfxHandle(this, handle, pathPtr, paramBlock, matrixPtr);
            if (!SetColor(vfx, color))
            {
                vfx.Dispose();
                return null;
            }
            Svc.Log.Information("[TargetRangeRing] CreateCircle OK handle={Handle:X} path={Path} r={Radius:F1} center=({X:F1},{Y:F1},{Z:F1})",
                handle, path, radius, center.X, center.Y, center.Z);
            return vfx;
        }
        catch (Exception ex)
        {
            if (handle != IntPtr.Zero && _removeOmen != null)
            {
                try { _removeOmen(handle, 1); } catch { }
            }
            Free(pathPtr, paramBlock, matrixPtr);
            Svc.Log.Debug(ex, "[TargetRangeRing] Failed to create circle VFX");
            return null;
        }
    }

    public bool SetColor(VfxHandle vfx, Vector4 color)
    {
        if (vfx.Disposed || vfx.Handle == IntPtr.Zero || _setOmenColor == null) return false;
        try
        {
            // omen 渲染对透明度的处理不同于 ImGui，强制不透明以保证清晰可见
            _setOmenColor(vfx.Handle, color.X, color.Y, color.Z, 1f);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[TargetRangeRing] Failed to set VFX color");
            return false;
        }
    }

    public bool SetMatrix(VfxHandle vfx, Vector3 center, float radius, float rotation = 0f)
    {
        if (vfx.Disposed || vfx.Handle == IntPtr.Zero || vfx.MatrixPtr == IntPtr.Zero || _setOmenMatrix == null) return false;
        try
        {
            var matrix = Matrix4x4.CreateScale(new Vector3(radius, 10f, radius))
                       * Matrix4x4.CreateRotationY(rotation)
                       * Matrix4x4.CreateTranslation(center);
            Marshal.StructureToPtr(matrix, vfx.MatrixPtr, false);
            nint ret = _setOmenMatrix(vfx.Handle, vfx.MatrixPtr);
            Svc.Log.Debug("[TargetRangeRing] SetMatrix handle={Handle:X} center=({X:F1},{Y:F1},{Z:F1}) r={Radius:F1} ret={Ret:X}",
                vfx.Handle, center.X, center.Y, center.Z, radius, ret);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[TargetRangeRing] Failed to set VFX matrix");
            return false;
        }
    }

    internal void Remove(VfxHandle vfx)
    {
        if (vfx.Handle != IntPtr.Zero && _removeOmen != null)
        {
            try { _removeOmen(vfx.Handle, 1); } catch { }
        }
        Free(vfx.PathPtr, vfx.ParamBlockPtr, vfx.MatrixPtr);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsReady = false;
        _registered.Clear();
    }

    private bool Register(string path, byte[] data)
    {
        try
        {
            uint count = 8u;
            uint magic = 1635149432u;
            uint hash = Crc32(path);
            nint pathPtr = Marshal.StringToHGlobalAnsi(path);
            try
            {
                nint res = _getResourceSync!(_resourceManager, (nint)(&count), (nint)(&magic), (nint)(&hash), pathPtr, IntPtr.Zero);
                if (res == IntPtr.Zero)
                {
                    Svc.Log.Warning("[TargetRangeRing] GetResourceSync returned null for {Path} (hash={Hash})", path, hash);
                    return false;
                }

                Marshal.WriteByte(res + 168, 2);
                Marshal.WriteByte(res + 169, 7);

                nint dataPtr = Marshal.ReadIntPtr(res + 192);
                nint buffer = Marshal.AllocHGlobal(data.Length);
                try
                {
                    Marshal.Copy(data, 0, buffer, data.Length);
                    nint loadResult = _vfxResourcesLoad!(dataPtr, buffer, (uint)data.Length, res);
                    nint setupResult = _vfxResourcesSetupComplete!(res);
                    Svc.Log.Information("[TargetRangeRing] VfxResourcesLoad done res={Res:X} dataPtr={Data:X} result={Result:X} setup={Setup:X}",
                        res, dataPtr, loadResult, setupResult);
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pathPtr);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[TargetRangeRing] Failed to register VFX resource {Path}", path);
            return false;
        }
    }

    private static void Free(nint a, nint b, nint c)
    {
        if (a != IntPtr.Zero) Marshal.FreeHGlobal(a);
        if (b != IntPtr.Zero) Marshal.FreeHGlobal(b);
        if (c != IntPtr.Zero) Marshal.FreeHGlobal(c);
    }

    private bool Resolve<T>(out T? value, string pattern, bool relative) where T : Delegate
    {
        value = null;
        nint addr = default;
        if (!Svc.SigScanner.TryScanText(pattern, out addr))
        {
            Svc.Log.Warning("[TargetRangeRing] missing signature for {Name}", typeof(T).Name);
            return false;
        }
        if (relative)
        {
            byte op = Marshal.ReadByte(addr);
            if ((uint)(op - 232) <= 1u) // E8 call / E9 jmp
            {
                int disp = Marshal.ReadInt32(IntPtr.Add(addr, 1));
                addr = Svc.SigScanner.ResolveRelativeAddress(IntPtr.Add(addr, 5), disp);
            }
        }
        value = Marshal.GetDelegateForFunctionPointer<T>(addr);
        return true;
    }

    private static byte[] ReadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded VFX resource " + name);
        var data = new byte[stream.Length];
        _ = stream.Read(data, 0, data.Length);
        return data;
    }

    private static uint Crc32(string s)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in System.Text.Encoding.ASCII.GetBytes(s))
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1)));
        }
        return ~crc;
    }

    private const string CreateVfxPattern = "e8 ?? ?? ?? ?? 48 ?? ?? 48 ?? ?? ?? ?? ?? ?? 33 ?? 48 ?? ?? 48 ?? ?? 48 ?? ?? ?? e8 ?? ?? ?? ?? 41 ?? ??";
    private const string InitVfxParamPattern = "e8 ?? ?? ?? ?? f3 ?? ?? ?? ?? ?? ?? ?? 48 ?? ?? ?? ?? ?? ?? 48 ?? ?? ?? 48 ?? ?? ?? ?? c7 44 24";
    private const string SetVfxP1Pattern = "e8 ?? ?? ?? ?? b2 ?? 48 ?? ?? e8 ?? ?? ?? ?? 81 a3 ?? ?? ?? ?? ?? ?? ?? ??";
    private const string SetVfxP2Pattern = "E8 ?? ?? ?? ?? 66 41 89 2E";
    private const string SetOmenColorPattern = "48 ?? ?? ?? ?? ?? ?? 48 ?? ?? 74 ?? 48 ?? ?? ?? f3 ?? ?? ?? ?? ?? f3 0f 11 89";
    private const string SetOmenMatrixPattern = "48 8B C4 48 83 ?? ?? 48 8B ?? ?? 01 00 00 48 85 C9 0F 84 D2 00";
    private const string RemoveOmenPattern = "e8 ?? ?? ?? ?? 48 ?? ?? 49 ?? ?? ?? ?? ?? ?? e8 ?? ?? ?? ?? ba ?? ?? ?? ?? 48 ?? ?? e8 ?? ?? ?? ?? eb ??";
    private const string GetResourceSyncPattern = "E8 ?? ?? ?? ?? 48 8B 8E ?? ?? ?? ?? 49 89 04 0E";
    private const string VfxResourcesLoadPattern = "48 89 5c 24 ?? 48 89 6c 24 ?? 48 89 74 24 ?? 48 89 7c 24 ?? 41 ?? 48 ?? ?? ?? 48 ?? ?? ?? 49 ?? ?? 48 ?? ?? ?? 41 ?? ??";
    private const string VfxResourcesSetupCompletePattern = "40 ?? 48 ?? ?? ?? 48 ?? ?? 33 ?? 8b ?? f0 0f c0 83";
}

/// <summary>单个圆环 VFX 的句柄，封装需要释放的原生指针。</summary>
public sealed class VfxHandle : IDisposable
{
    private readonly VfxManager _owner;
    private int _disposedFlag;

    public nint Handle { get; }
    public nint PathPtr { get; }
    public nint ParamBlockPtr { get; }
    public nint MatrixPtr { get; }

    public bool Disposed => _disposedFlag != 0;

    internal VfxHandle(VfxManager owner, nint handle, nint pathPtr, nint paramBlock, nint matrix)
    {
        _owner = owner;
        Handle = handle;
        PathPtr = pathPtr;
        ParamBlockPtr = paramBlock;
        MatrixPtr = matrix;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedFlag, 1) == 0)
            _owner.Remove(this);
    }
}
