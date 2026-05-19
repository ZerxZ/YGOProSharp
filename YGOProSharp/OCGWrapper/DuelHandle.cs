using System.Runtime.InteropServices;

namespace YGOProSharp.OCGWrapper;

internal sealed class DuelHandle : SafeHandle
{
    private DuelHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    internal DuelHandle(IntPtr handle)
        : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        OcgCoreNative.EndDuel(handle);
        return true;
    }
}
