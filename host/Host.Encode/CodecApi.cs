using System.Runtime.InteropServices;

namespace ScreenPortal.Host.Encode;

/// <summary>
/// Acesso mínimo ao ICodecAPI (não exposto pelo Vortice) via chamada direta de vtable.
/// </summary>
internal sealed unsafe class CodecApi : IDisposable
{
    public static readonly Guid AVLowLatencyMode = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    public static readonly Guid AVEncCommonRateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    public static readonly Guid AVEncCommonMeanBitRate = new("f7222374-2144-4815-b550-a37f8e12ee52");
    public static readonly Guid AVEncMPVGOPSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");
    public static readonly Guid AVEncMPVDefaultBPictureCount = new("8d390aac-dc5c-4200-b57f-814d04babab2");
    public static readonly Guid AVEncVideoForceKeyFrame = new("398c1b98-8353-475a-9ef2-8f265d260345");

    public const uint RateControlCbr = 0;
    public const uint RateControlLowDelayVbr = 4;

    private static readonly Guid IID_ICodecAPI = new("901db4c7-31ce-41a2-85dc-8fa0bf41b8da");
    private const int SetValueSlot = 9; // IUnknown(3) + IsSupported, IsModifiable, GetParameterRange, GetParameterValues, GetDefaultValue, GetValue

    private IntPtr _ptr;

    private CodecApi(IntPtr ptr) => _ptr = ptr;

    public static CodecApi? TryGet(IntPtr transform)
    {
        var iid = IID_ICodecAPI;
        return Marshal.QueryInterface(transform, in iid, out var ptr) >= 0 ? new CodecApi(ptr) : null;
    }

    /// <returns>HRESULT (encoders ignoram propriedades que não suportam; o chamador só loga).</returns>
    public int SetUInt32(Guid api, uint value) => SetValue(api, new Variant { Vt = VT_UI4, UInt32 = value });

    public int SetBool(Guid api, bool value) => SetValue(api, new Variant { Vt = VT_BOOL, Bool = value ? (short)-1 : (short)0 });

    private int SetValue(Guid api, Variant value)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, Variant*, int>)(*(*(void***)_ptr + SetValueSlot));
        return fn(_ptr, &api, &value);
    }

    public void Dispose()
    {
        if (_ptr != IntPtr.Zero) { Marshal.Release(_ptr); _ptr = IntPtr.Zero; }
    }

    private const ushort VT_BOOL = 11;
    private const ushort VT_UI4 = 19;

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public uint UInt32;
        [FieldOffset(8)] public short Bool;
    }
}
