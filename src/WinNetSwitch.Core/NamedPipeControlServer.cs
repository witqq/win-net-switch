using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace WinNetSwitch.Core;

public sealed class NamedPipeControlServer : IDisposable
{
    public const string PipeName = "WinNetSwitch.Control.v1";
    private const int ProtocolVersion = 1;
    private const int MaximumRequestCharacters = 4096;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly INetworkAdapterService _adapterService;
    private readonly Action<string> _logInfo;
    private readonly Action<string, Exception> _logError;
    private readonly CancellationTokenSource _stopSource = new();
    private Task? _serverTask;
    private bool _disposed;

    public NamedPipeControlServer(
        INetworkAdapterService adapterService,
        Action<string>? logInfo = null,
        Action<string, Exception>? logError = null)
    {
        _adapterService = adapterService ?? throw new ArgumentNullException(nameof(adapterService));
        _logInfo = logInfo ?? (_ => { });
        _logError = logError ?? ((_, _) => { });
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_serverTask is not null)
        {
            throw new InvalidOperationException("The local control server is already running.");
        }

        _serverTask = Task.Run(() => RunAsync(_stopSource.Token));
        _logInfo($"Local control server started. Pipe: {PipeName}.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopSource.Cancel();
        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(item => item is OperationCanceledException))
        {
        }
        finally
        {
            _stopSource.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logError("Local control server connection failed.", exception);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        using var requestTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeoutSource.CancelAfter(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        await using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            AutoFlush = true,
        };

        NetworkControlResponse response;
        try
        {
            var requestJson = await ReadBoundedLineAsync(reader, requestTimeoutSource.Token)
                    .ConfigureAwait(false)
                ?? throw new InvalidDataException("The request was empty.");

            var request = JsonSerializer.Deserialize<NetworkControlRequest>(
                    requestJson,
                    SerializerOptions)
                ?? throw new InvalidDataException("The request JSON was empty.");
            response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logError("Local control request failed.", exception);
            response = NetworkControlResponse.Failure(exception.Message);
        }

        await writer.WriteLineAsync(
                JsonSerializer.Serialize(response, SerializerOptions).AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string?> ReadBoundedLineAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var request = new StringBuilder(capacity: 256);
        var character = new char[1];
        while (true)
        {
            var read = await reader.ReadAsync(character.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return request.Length == 0 ? null : request.ToString();
            }

            if (character[0] == '\n')
            {
                return request.ToString();
            }

            if (character[0] == '\r')
            {
                continue;
            }

            if (request.Length >= MaximumRequestCharacters)
            {
                throw new InvalidDataException(
                    $"The request exceeds {MaximumRequestCharacters} characters.");
            }

            request.Append(character[0]);
        }
    }

    private async Task<NetworkControlResponse> ExecuteAsync(
        NetworkControlRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Version != ProtocolVersion)
        {
            throw new InvalidDataException(
                $"Unsupported protocol version {request.Version}; expected {ProtocolVersion}.");
        }

        _logInfo($"Local control request received: {request.Command}.");
        var adapters = request.Command switch
        {
            NetworkControlCommand.List =>
                await _adapterService.GetPhysicalAdaptersAsync(cancellationToken).ConfigureAwait(false),
            NetworkControlCommand.Toggle =>
                await _adapterService.ToggleAdapterAsync(
                        RequireAdapterId(request),
                        cancellationToken)
                    .ConfigureAwait(false),
            NetworkControlCommand.Cycle =>
                await _adapterService.CycleToNextAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidDataException($"Unsupported command: {request.Command}."),
        };

        _logInfo($"Local control request completed: {request.Command}.");
        return NetworkControlResponse.Success(adapters);
    }

    private static Guid RequireAdapterId(NetworkControlRequest request) =>
        request.AdapterId is { } adapterId && adapterId != Guid.Empty
            ? adapterId
            : throw new InvalidDataException("The toggle command requires a non-empty adapterId.");

    private enum NetworkControlCommand
    {
        List,
        Toggle,
        Cycle,
    }

    private sealed record NetworkControlRequest(
        int Version,
        NetworkControlCommand Command,
        Guid? AdapterId);

    private sealed record NetworkControlAdapter(
        Guid Id,
        string Name,
        string Description,
        string Status,
        bool IsEnabled,
        bool IsActive,
        bool IsWireless);

    private sealed record NetworkControlResponse(
        int Version,
        bool Ok,
        IReadOnlyList<NetworkControlAdapter> Adapters,
        string? Error)
    {
        internal static NetworkControlResponse Success(
            IReadOnlyList<PhysicalNetworkAdapter> adapters) =>
            new(
                ProtocolVersion,
                Ok: true,
                adapters.Select(adapter => new NetworkControlAdapter(
                        adapter.Id,
                        adapter.Name,
                        adapter.Description,
                        adapter.Status,
                        adapter.IsEnabled,
                        adapter.IsActive,
                        adapter.IsWireless))
                    .ToArray(),
                Error: null);

        internal static NetworkControlResponse Failure(string error) =>
            new(ProtocolVersion, Ok: false, Adapters: [], Error: error);
    }
}
