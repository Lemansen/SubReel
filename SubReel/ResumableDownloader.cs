using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
public static class ResumableDownloader
{
    public static async Task DownloadFileAsync(
        HttpClient client,
        string url,
        string filePath,
        IProgress<double>? progress,
        CancellationToken token)
    {
        long existingBytes = 0;

        if (File.Exists(filePath))
            existingBytes = new FileInfo(filePath).Length;

       using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (existingBytes > 0)
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            token);

        response.EnsureSuccessStatusCode();

        long totalBytes =
            (response.Content.Headers.ContentLength ?? 0) + existingBytes;

        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var file = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None,
            8192,
            true);

        var buffer = new byte[8192];
        long totalRead = existingBytes;
        int read;

        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), token);
            totalRead += read;

            if (totalBytes > 0)
            {
                double percent = (double)totalRead / totalBytes * 100;
                progress?.Report(percent);
            }
        }
    }
}