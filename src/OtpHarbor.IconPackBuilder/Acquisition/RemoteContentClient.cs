using System.Net.Http.Headers;

namespace OtpHarbor.IconPackBuilder.Acquisition;

public interface IRemoteContentClient
{
    Task<string> GetStringAsync(string url, long maximumBytes, CancellationToken cancellationToken);
    Task<byte[]> GetBytesAsync(string url, long maximumBytes, CancellationToken cancellationToken);
    Task DownloadFileAsync(string url, string destinationPath, long maximumBytes, CancellationToken cancellationToken);
}

internal sealed class DownloadSizeLimitException(string message) : IOException(message);
internal sealed class DownloadNotFoundException(string message) : IOException(message);

public sealed class RemoteContentClient : IRemoteContentClient, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
    private const int MaximumRedirects = 5;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public RemoteContentClient(HttpClient? client = null)
    {
        if (client is null)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            _client = new HttpClient(handler, disposeHandler: true);
            _ownsClient = true;
        }
        else
        {
            _client = client;
        }

        _client.DefaultRequestHeaders.UserAgent.Clear();
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OtpHarbor-IconPackBuilder", "1.0"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<string> GetStringAsync(string url, long maximumBytes, CancellationToken cancellationToken)
        => System.Text.Encoding.UTF8.GetString(await GetBytesAsync(url, maximumBytes, cancellationToken));

    public async Task<byte[]> GetBytesAsync(string url, long maximumBytes, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await DownloadAsync(url, stream, maximumBytes, cancellationToken);
        return stream.ToArray();
    }

    public async Task DownloadFileAsync(string url, string destinationPath, long maximumBytes, CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await DownloadAsync(url, destination, maximumBytes, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private async Task DownloadAsync(string url, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InputValidationException($"Refusing non-HTTPS upstream URL '{url}'.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= MaximumRedirects)
                    throw new HttpRequestException($"Download from '{uri}' exceeded the redirect limit.");
                var location = response.Headers.Location
                    ?? throw new HttpRequestException($"Download from '{uri}' returned a redirect without a Location header.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (uri.Scheme != Uri.UriSchemeHttps)
                    throw new InputValidationException($"Refusing redirect to non-HTTPS upstream URL '{uri}'.");
                continue;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new DownloadNotFoundException($"Download from '{uri}' returned 404 (Not Found).");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length > maximumBytes)
                throw new DownloadSizeLimitException($"Download from '{uri}' is {length} bytes; the limit is {maximumBytes} bytes.");

            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                total = checked(total + read);
                if (total > maximumBytes)
                    throw new DownloadSizeLimitException($"Download from '{uri}' exceeded the {maximumBytes}-byte limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            return;
        }
    }

    private static bool IsRedirect(System.Net.HttpStatusCode statusCode) => statusCode is
        System.Net.HttpStatusCode.MovedPermanently
        or System.Net.HttpStatusCode.Redirect
        or System.Net.HttpStatusCode.RedirectMethod
        or System.Net.HttpStatusCode.TemporaryRedirect
        or System.Net.HttpStatusCode.PermanentRedirect;

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
