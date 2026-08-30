using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinNetSwitch.Core;

/// <summary>
/// Reads and changes the software Wi-Fi radio through the Windows Native Wi-Fi API.
/// </summary>
public sealed class NativeWirelessRadioController : IWirelessRadioController
{
    private const uint ClientVersion = 2;
    private const uint ErrorServiceNotActive = 1062;
    private const int InterfaceListHeaderBytes = sizeof(uint) * 2;
    private const int RadioStateHeaderBytes = sizeof(uint);

    public Task<WirelessRadioState?> GetStateAsync(
        Guid interfaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();

        using var client = OpenClient(allowInactiveService: true);
        if (client.IsInvalid || !ContainsInterface(client, interfaceId))
        {
            return Task.FromResult<WirelessRadioState?>(null);
        }

        var states = QueryPhysicalRadioStates(client, interfaceId);
        var state = new WirelessRadioState(
            SoftwareOn: states.All(item => item.SoftwareState == Dot11RadioState.On),
            HardwareOn: states.All(item => item.HardwareState != Dot11RadioState.Off),
            PhysicalLayerCount: states.Count);
        return Task.FromResult<WirelessRadioState?>(state);
    }

    public Task<bool> SetSoftwareStateAsync(
        Guid interfaceId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();

        using var client = OpenClient(allowInactiveService: false);
        if (!ContainsInterface(client, interfaceId))
        {
            return Task.FromResult(false);
        }

        var states = QueryPhysicalRadioStates(client, interfaceId);
        if (states.Count == 0)
        {
            throw new NetworkSwitchException(
                $"The Windows WLAN API returned no PHY entries for interface {interfaceId:D}.");
        }

        foreach (var currentState in states)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestedState = new WlanPhyRadioState
            {
                PhyIndex = currentState.PhyIndex,
                SoftwareState = enabled ? Dot11RadioState.On : Dot11RadioState.Off,
                HardwareState = Dot11RadioState.Unknown,
            };
            var statePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WlanPhyRadioState>());
            try
            {
                Marshal.StructureToPtr(requestedState, statePointer, fDeleteOld: false);
                ThrowIfError(
                    WlanSetInterface(
                        client,
                        ref interfaceId,
                        WlanInterfaceOpcode.RadioState,
                        (uint)Marshal.SizeOf<WlanPhyRadioState>(),
                        statePointer,
                        IntPtr.Zero),
                    "change the software radio state");
            }
            finally
            {
                Marshal.FreeHGlobal(statePointer);
            }
        }

        return Task.FromResult(true);
    }

    private static SafeWlanHandle OpenClient(bool allowInactiveService)
    {
        var result = WlanOpenHandle(
            ClientVersion,
            IntPtr.Zero,
            out _,
            out var rawHandle);
        if (allowInactiveService && result == ErrorServiceNotActive)
        {
            return new SafeWlanHandle();
        }

        ThrowIfError(result, "open a WLAN client handle");
        return new SafeWlanHandle(rawHandle);
    }

    private static bool ContainsInterface(SafeWlanHandle client, Guid interfaceId)
    {
        ThrowIfError(
            WlanEnumInterfaces(client, IntPtr.Zero, out var listPointer),
            "enumerate WLAN interfaces");
        try
        {
            var count = Marshal.ReadInt32(listPointer);
            var itemSize = Marshal.SizeOf<WlanInterfaceInfo>();
            for (var index = 0; index < count; index++)
            {
                var itemPointer = IntPtr.Add(
                    listPointer,
                    InterfaceListHeaderBytes + (index * itemSize));
                var item = Marshal.PtrToStructure<WlanInterfaceInfo>(itemPointer);
                if (item.InterfaceGuid == interfaceId)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private static IReadOnlyList<WlanPhyRadioState> QueryPhysicalRadioStates(
        SafeWlanHandle client,
        Guid interfaceId)
    {
        ThrowIfError(
            WlanQueryInterface(
                client,
                ref interfaceId,
                WlanInterfaceOpcode.RadioState,
                IntPtr.Zero,
                out var dataSize,
                out var dataPointer,
                IntPtr.Zero),
            "read the WLAN radio state");
        try
        {
            var count = Marshal.ReadInt32(dataPointer);
            var itemSize = Marshal.SizeOf<WlanPhyRadioState>();
            var maximumCount = Math.Max(0, ((int)dataSize - RadioStateHeaderBytes) / itemSize);
            if (count < 0 || count > maximumCount)
            {
                throw new NetworkSwitchException("The Windows WLAN API returned an invalid radio state size.");
            }

            var states = new WlanPhyRadioState[count];
            for (var index = 0; index < count; index++)
            {
                var itemPointer = IntPtr.Add(
                    dataPointer,
                    RadioStateHeaderBytes + (index * itemSize));
                states[index] = Marshal.PtrToStructure<WlanPhyRadioState>(itemPointer);
            }

            return states;
        }
        finally
        {
            WlanFreeMemory(dataPointer);
        }
    }

    private static void ThrowIfError(uint errorCode, string operation)
    {
        if (errorCode == 0)
        {
            return;
        }

        throw new NetworkSwitchException(
            $"The Windows WLAN API could not {operation}: {new Win32Exception((int)errorCode).Message} " +
            $"(error code {errorCode}).");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Native Wi-Fi API is available only on Windows.");
        }
    }

    private enum WlanInterfaceOpcode
    {
        RadioState = 4,
    }

    private enum Dot11RadioState
    {
        Unknown = 0,
        On = 1,
        Off = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanPhyRadioState
    {
        internal uint PhyIndex;
        internal Dot11RadioState SoftwareState;
        internal Dot11RadioState HardwareState;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        internal Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string InterfaceDescription;

        internal int InterfaceState;
    }

    private sealed class SafeWlanHandle : SafeHandle
    {
        internal SafeWlanHandle()
            : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        internal SafeWlanHandle(IntPtr handle)
            : this()
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        protected override bool ReleaseHandle() => WlanCloseHandle(handle, IntPtr.Zero) == 0;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(
        uint clientVersion,
        IntPtr reserved,
        out uint negotiatedVersion,
        out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(
        SafeWlanHandle clientHandle,
        IntPtr reserved,
        out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(
        SafeWlanHandle clientHandle,
        ref Guid interfaceGuid,
        WlanInterfaceOpcode opcode,
        IntPtr reserved,
        out uint dataSize,
        out IntPtr data,
        IntPtr opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanSetInterface(
        SafeWlanHandle clientHandle,
        ref Guid interfaceGuid,
        WlanInterfaceOpcode opcode,
        uint dataSize,
        IntPtr data,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
