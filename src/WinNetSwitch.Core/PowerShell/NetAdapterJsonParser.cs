using System.Text.Json;

namespace WinNetSwitch.Core.PowerShell;

internal static class NetAdapterJsonParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static IReadOnlyList<PhysicalNetworkAdapter> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new NetworkSwitchException(
                "Windows PowerShell вернул пустой список сетевых адаптеров вместо JSON.");
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<AdapterDto>>(json, SerializerOptions)
                ?? throw new NetworkSwitchException("Windows PowerShell вернул null вместо списка адаптеров.");

            return items.Select(ToAdapter).ToArray();
        }
        catch (JsonException exception)
        {
            throw new NetworkSwitchException(
                "Не удалось разобрать список сетевых адаптеров, возвращённый Windows PowerShell.",
                exception);
        }
    }

    private static PhysicalNetworkAdapter ToAdapter(AdapterDto item)
    {
        if (!Guid.TryParse(item.Id, out var id) || id == Guid.Empty)
        {
            throw new NetworkSwitchException("Windows PowerShell вернул адаптер без корректного InterfaceGuid.");
        }

        if (string.IsNullOrWhiteSpace(item.Name))
        {
            throw new NetworkSwitchException($"Адаптер {id:D} не имеет имени.");
        }

        return new PhysicalNetworkAdapter(
            id,
            item.InterfaceIndex,
            item.Name,
            item.Description ?? string.Empty,
            item.Status ?? "Unknown",
            item.MediaConnectionState ?? "Unknown",
            item.LinkSpeed ?? string.Empty,
            item.IsEnabled);
    }

    private sealed class AdapterDto
    {
        public string? Id { get; init; }

        public int InterfaceIndex { get; init; }

        public string? Name { get; init; }

        public string? Description { get; init; }

        public string? Status { get; init; }

        public string? MediaConnectionState { get; init; }

        public string? LinkSpeed { get; init; }

        public bool IsEnabled { get; init; }
    }
}
