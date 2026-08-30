using System.Text;

namespace WinNetSwitch.Core.PowerShell;

internal static class NetAdapterScripts
{
    private const string Prelude =
        "$ErrorActionPreference='Stop';" +
        "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;" +
        "$OutputEncoding=[System.Text.Encoding]::UTF8;";

    internal static string ListPhysicalAdapters => Prelude +
        "$items=@(Get-NetAdapter -Name * -Physical -ErrorAction Stop |" +
        "Sort-Object -Property Name |" +
        "Select-Object @{Name='Id';Expression={$_.InterfaceGuid.ToString()}}," +
        "@{Name='DeviceInstanceId';Expression={$_.PnPDeviceID}}," +
        "@{Name='InterfaceIndex';Expression={[int]$_.ifIndex}}," +
        "Name,@{Name='Description';Expression={$_.InterfaceDescription}}," +
        "@{Name='Status';Expression={$_.Status.ToString()}}," +
        "@{Name='MediaConnectionState';Expression={$_.MediaConnectionState.ToString()}}," +
        "@{Name='LinkSpeed';Expression={$_.LinkSpeed.ToString()}}," +
        "@{Name='IsEnabled';Expression={$_.Status.ToString() -notin @('Disabled','Not Present')}});" +
        "$known=@($items | ForEach-Object {$_.DeviceInstanceId});" +
        "$disabled=@(Get-PnpDevice -Class Net -ErrorAction Stop | Where-Object {" +
        "$_.Present -and $_.Problem -eq 22 -and " +
        "($_.InstanceId -like 'PCI\\*' -or $_.InstanceId -like 'USB\\*') -and " +
        "$known -notcontains $_.InstanceId} | ForEach-Object {" +
        "[PSCustomObject]@{Id='';DeviceInstanceId=$_.InstanceId;InterfaceIndex=0;" +
        "Name=$_.FriendlyName;Description=$_.FriendlyName;Status='Disabled';" +
        "MediaConnectionState='Unknown';LinkSpeed='';IsEnabled=$false}});" +
        "$items=@($items)+@($disabled);" +
        "ConvertTo-Json -InputObject $items -Depth 3 -Compress;";

    internal static string Enable(Guid adapterId) => Mutation(adapterId, "Enable-NetAdapter");

    internal static string Disable(Guid adapterId) => Mutation(adapterId, "Disable-NetAdapter");

    internal static string EnablePnpDevice(string deviceInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        var encodedId = Convert.ToBase64String(Encoding.UTF8.GetBytes(deviceInstanceId));
        return Prelude +
            $"$id=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedId}'));" +
            "$items=@(Get-PnpDevice -InstanceId $id -ErrorAction Stop);" +
            "if($items.Count -ne 1){throw \"PnP network device $id not found.\"};" +
            "$items[0] | Enable-PnpDevice -Confirm:$false -ErrorAction Stop;";
    }

    private static string Mutation(Guid adapterId, string command)
    {
        var id = adapterId.ToString("D");
        return Prelude +
            $"$id=[Guid]'{id}';" +
            "$items=@(Get-NetAdapter -Name * -Physical -ErrorAction Stop |" +
            "Where-Object {[Guid]$_.InterfaceGuid -eq $id});" +
            "if($items.Count -ne 1){throw \"Physical network adapter $id was not found.\"};" +
            $"$items[0] | {command} -Confirm:$false -ErrorAction Stop;";
    }
}
