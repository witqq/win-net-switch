using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace WinNetSwitch.Core;

internal static class LocalControlPipeFactory
{
    internal const string MediumIntegrityLabelSddl = "S:(ML;;NW;;;ME)";

    internal static NamedPipeServerStream Create(string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }

        return CreateWindowsPipe(pipeName);
    }

    internal static NamedPipeServerStream CreateForCurrentUserSmoke(string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Create(pipeName);
        }

        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        return CreateWindowsPipe(pipeName, userSid);
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindowsPipe(string pipeName)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var logonSid = identity.Groups?
            .OfType<SecurityIdentifier>()
            .FirstOrDefault(group => group.IsWellKnown(WellKnownSidType.LogonIdsSid))
            ?? throw new InvalidOperationException("The current Windows logon SID is unavailable.");
        return CreateWindowsPipe(pipeName, logonSid);
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindowsPipe(
        string pipeName,
        SecurityIdentifier accessSid)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var ownerSid = identity.User
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = CreateWindowsSecurity(ownerSid, accessSid);

        var pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security,
            HandleInheritability.None,
            PipeAccessRights.TakeOwnership);
        try
        {
            ApplyMediumIntegrityLabel(pipe.SafePipeHandle);
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    internal static PipeSecurity CreateWindowsSecurity(
        SecurityIdentifier ownerSid,
        SecurityIdentifier accessSid)
    {
        var security = new PipeSecurity();
        security.SetOwner(ownerSid);
        // A logon SID is unique to this interactive sign-in. Unlike a user SID, it does not
        // grant access to another local session or a remote logon by the same account.
        // Do not replace this with PipeOptions.CurrentUserOnly: on Windows that option derives
        // its ACL from Token.Owner, which can be Administrators for an elevated process.
        security.AddAccessRule(new PipeAccessRule(
            accessSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    [SupportedOSPlatform("windows")]
    internal static RawSecurityDescriptor CreateMediumIntegrityLabelDescriptor() =>
        new(MediumIntegrityLabelSddl);

    [SupportedOSPlatform("windows")]
    private static void ApplyMediumIntegrityLabel(SafePipeHandle pipeHandle)
    {
        const int labelSecurityInformation = 0x00000010;
        // Mandatory Integrity Control is evaluated before the DACL. The medium label permits
        // ordinary Stream Deck to write while the logon-SID DACL remains the identity boundary.
        var descriptor = CreateMediumIntegrityLabelDescriptor();
        var binaryDescriptor = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binaryDescriptor, 0);
        if (!SetKernelObjectSecurity(
                pipeHandle,
                labelSecurityInformation,
                binaryDescriptor))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not allow the current medium-integrity logon session to use the control pipe.");
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(
        SafePipeHandle handle,
        int securityInformation,
        byte[] securityDescriptor);
}
