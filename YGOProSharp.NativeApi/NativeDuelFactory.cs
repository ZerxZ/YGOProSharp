using YGOProSharp.Abstractions.Ocg;

namespace YGOProSharp.NativeApi;

public sealed unsafe class NativeDuelFactory : IDuelFactory
{
    private readonly NativeOcgRuntime _runtime;

    internal NativeDuelFactory(NativeOcgRuntime runtime)
    {
        _runtime = runtime;
    }

    public IDuelSession Create(uint seed)
    {
        uint[] seedSequence = CreateSeedSequence(seed);
        return Create(seedSequence);
    }

    public IDuelSession Create(ReadOnlySpan<uint> seedSequence)
    {
        if (seedSequence.Length != OcgCoreConstants.SeedCount)
            throw new ArgumentException($"Seed sequence must contain {OcgCoreConstants.SeedCount} values.", nameof(seedSequence));

        _runtime.EnsureInitialized();

        uint[] copy = seedSequence.ToArray();
        fixed (uint* seedPtr = copy)
        {
            IntPtr nativeHandle;
            try
            {
                nativeHandle = OcgCoreImports.CreateDuelV2(seedPtr);
            }
            catch (Exception exception) when (NativeOcgErrors.IsNativeRuntimeFailure(exception))
            {
                throw NativeOcgErrors.CreateRuntimeFailure(exception);
            }

            return CreateSession(nativeHandle);
        }
    }

    public IDuelSession CreateLegacy(uint seed)
    {
        _runtime.EnsureInitialized();

        MtRandom random = new();
        random.Reset(seed);
        try
        {
            return CreateSession(OcgCoreImports.CreateDuel(random.Rand()));
        }
        catch (Exception exception) when (NativeOcgErrors.IsNativeRuntimeFailure(exception))
        {
            throw NativeOcgErrors.CreateRuntimeFailure(exception);
        }
    }

    private NativeDuelSession CreateSession(IntPtr nativeHandle)
    {
        if (nativeHandle == IntPtr.Zero)
            throw new InvalidOperationException("Could not create native duel. Ensure YGOProSharp.Native runtime assets are available.");

        NativeDuelSession session = new(_runtime, new DuelHandle(nativeHandle));
        _runtime.Register(session);
        return session;
    }

    private static uint[] CreateSeedSequence(uint seed)
    {
        MtRandom random = new();
        random.Reset(seed);

        uint[] seedSequence = new uint[OcgCoreConstants.SeedCount];
        for (int i = 0; i < seedSequence.Length; i++)
            seedSequence[i] = random.Rand();

        return seedSequence;
    }
}
