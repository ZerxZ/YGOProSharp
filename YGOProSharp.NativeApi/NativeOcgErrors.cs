namespace YGOProSharp.NativeApi;

internal static class NativeOcgErrors
{
    internal static bool IsNativeRuntimeFailure(Exception exception)
    {
        return exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException;
    }

    internal static InvalidOperationException CreateRuntimeFailure(Exception innerException)
    {
        return new InvalidOperationException(
            "Could not load the ocgcore native runtime. Ensure the YGOProSharp.Native runtime dependency is referenced and the current RID has a matching native library.",
            innerException);
    }
}
