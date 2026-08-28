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
        "@{Name='InterfaceIndex';Expression={[int]$_.ifIndex}}," +
        "Name,@{Name='Description';Expression={$_.InterfaceDescription}}," +
        "@{Name='Status';Expression={$_.Status.ToString()}}," +
        "@{Name='MediaConnectionState';Expression={$_.MediaConnectionState.ToString()}}," +
        "@{Name='LinkSpeed';Expression={$_.LinkSpeed.ToString()}}," +
        "@{Name='IsEnabled';Expression={$_.Status.ToString() -notin @('Disabled','Not Present')}});" +
        "ConvertTo-Json -InputObject $items -Depth 3 -Compress;";

    internal static string Enable(Guid adapterId) => Mutation(adapterId, "Enable-NetAdapter");

    internal static string Disable(Guid adapterId) => Mutation(adapterId, "Disable-NetAdapter");

    private static string Mutation(Guid adapterId, string command)
    {
        var id = adapterId.ToString("D");
        return Prelude +
            $"$id=[Guid]'{id}';" +
            "$items=@(Get-NetAdapter -Name * -Physical -ErrorAction Stop |" +
            "Where-Object {$_.InterfaceGuid -eq $id});" +
            "if($items.Count -ne 1){throw \"Физический сетевой адаптер $id не найден.\"};" +
            $"$items[0] | {command} -Confirm:$false -ErrorAction Stop;";
    }
}
