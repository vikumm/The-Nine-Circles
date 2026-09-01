using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Divinity.Launcher.Oidc;

public sealed class LoopbackCallbackListener : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly LoopbackCallbackState _callbackState;

    private LoopbackCallbackListener(string host, int port, string callbackPath, string expectedState)
    {
        RedirectUri = new UriBuilder("http", host, port, callbackPath).Uri;
        _callbackState = new LoopbackCallbackState(expectedState, callbackPath);
        _listener.Prefixes.Add(new UriBuilder("http", host, port, "/").Uri.ToString());
        _listener.Start();
    }

    public Uri RedirectUri { get; }

    public static LoopbackCallbackListener Start(string host, string callbackPath, string expectedState) =>
        new(host, GetFreeLoopbackPort(), callbackPath, expectedState);

    public async Task<string> WaitForCodeAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        while (!timeoutCts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Login timed out while waiting for the loopback callback. Retry the login from the current launcher window.");
            }

            if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint?.Address ?? IPAddress.None))
            {
                await WriteResponseAsync(context.Response, HttpStatusCode.Forbidden, "Only loopback callbacks are accepted.", timeoutCts.Token);
                continue;
            }

            var result = _callbackState.Accept(context.Request.Url!);
            if (result.Status == CallbackValidationStatus.UnexpectedPath)
            {
                await WriteResponseAsync(context.Response, HttpStatusCode.NotFound, "Unexpected callback path.", timeoutCts.Token);
                continue;
            }

            var statusCode = result.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            await WriteResponseAsync(context.Response, statusCode, result.Success ? "Login received. You may return to the launcher." : result.Message, timeoutCts.Token);

            if (!result.Success)
            {
                throw new LauncherAuthException(result.Message);
            }

            return result.Code!;
        }

        throw new TimeoutException("Login timed out while waiting for the loopback callback. Retry the login from the current launcher window.");
    }

    public ValueTask DisposeAsync()
    {
        _listener.Close();
        return ValueTask.CompletedTask;
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, HttpStatusCode statusCode, string body, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
