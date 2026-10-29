using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Duende.IdentityModel.OidcClient.Browser;

namespace AppTemplate.PresentationApp.Common.Sessions;

internal sealed class LoopbackBrowser : IBrowser, IDisposable
{
    public const string ReturnToTheApp = "The sign-in is complete: return to the app. This tab can be closed.";

    public const string NoBrowser = "No browser could be opened for the sign-in.";

    public const string TimedOut = "The sign-in was not completed in the browser in time.";

    private static readonly TimeSpan _silentConnectionTimeout = TimeSpan.FromSeconds(10);

    private readonly ISystemBrowser _systemBrowser;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public LoopbackBrowser(ISystemBrowser systemBrowser)
    {
        ArgumentNullException.ThrowIfNull(systemBrowser);

        _systemBrowser = systemBrowser;
        _listener.Start();
        RedirectUri = new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"http://{IPAddress.Loopback}:{((IPEndPoint)_listener.LocalEndpoint).Port}/"));
    }

    public Uri RedirectUri { get; }

    public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        try
        {
            _systemBrowser.Open(new Uri(options.StartUrl));
        }
        catch (Win32Exception)
        {
            return new BrowserResult { ResultType = BrowserResultType.UnknownError, Error = NoBrowser };
        }

        try
        {
            return new BrowserResult
            {
                ResultType = BrowserResultType.Success,
                Response = await CallbackQueryAsync(timeout.Token).ConfigureAwait(false),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new BrowserResult { ResultType = BrowserResultType.Timeout, Error = TimedOut };
        }
    }

    public void Dispose() => _listener.Dispose();

    private async Task<string> CallbackQueryAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using var connection = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);

            if (await AnswerAsync(connection.GetStream(), cancellationToken).ConfigureAwait(false) is { } query)
            {
                return query;
            }
        }
    }

    private async Task<string?> AnswerAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        string? target;

        using (var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            request.CancelAfter(_silentConnectionTimeout);

            try
            {
                target = await RequestTargetAsync(stream, request.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                return null;
            }
        }

        string? query = target is not null
            && Uri.TryCreate(RedirectUri, target, out var callback)
            && callback.AbsolutePath == RedirectUri.AbsolutePath
            && callback.Query.Length > 1
                ? callback.Query
                : null;

        await RespondAsync(stream, query is not null, cancellationToken).ConfigureAwait(false);

        return query;
    }

    private static async Task<string?> RequestTargetAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string[] requestLine = (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty).Split(' ');
        await ReadHeadersSoClosingDoesNotResetTheAnswerAsync(reader, cancellationToken).ConfigureAwait(false);

        return requestLine is ["GET", var target, _] ? target : null;
    }

    private static async Task ReadHeadersSoClosingDoesNotResetTheAnswerAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 })
        {
        }
    }

    private static async Task RespondAsync(Stream stream, bool isCallback, CancellationToken cancellationToken)
    {
        byte[] body = isCallback
            ? Encoding.UTF8.GetBytes(
                $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Sign-in</title></head><body><p>{ReturnToTheApp}</p></body></html>")
            : [];
        byte[] head = Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"HTTP/1.1 {(isCallback ? "200 OK" : "404 Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"));

        await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }
}
