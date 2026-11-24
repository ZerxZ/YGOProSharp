using System.Net.Http.Headers;
using System.Text;

namespace YGOProSharp.Mcp;

/// <summary>
/// 本地 <c>stdio</c> MCP 轻量转发器：从标准输入读取换行分隔的 JSON-RPC 2.0 帧，
/// 附加 Bearer Token 转发至运行中的 YGOProSharp <c>/mcp</c> 端点，并将响应纯净写回标准输出流。
/// 全程零 <c>Console.Write*</c> 调用，确保 <c>stdout</c> 协议帧零污染。
/// </summary>
public static class McpStdioRelay
{
    public static async Task<int> RunAsync(
        Uri endpointUri,
        string rawToken,
        Stream inputStream,
        Stream outputStream,
        HttpMessageHandler? customHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpointUri);
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);

        using HttpClient http = customHandler is null ? new HttpClient() : new HttpClient(customHandler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(45);
        if (!string.IsNullOrWhiteSpace(rawToken))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken.Trim());
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        UTF8Encoding utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
        using StreamReader reader = new(inputStream, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        using StreamWriter writer = new(outputStream, utf8NoBom, bufferSize: 8192, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true
        };

        string? sessionId = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
            {
                Content = new StringContent(trimmed, utf8NoBom, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(sessionId))
                req.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);

            using HttpResponseMessage res = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? sidVals))
            {
                string? sid = sidVals.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(sid))
                    sessionId = sid;
            }

            if ((int)res.StatusCode == 202)
                continue;

            string body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                // 压平为单行 NDJSON 帧写回 stdout
                string singleLine = body.Replace("\r", string.Empty).Replace("\n", " ");
                await writer.WriteLineAsync(singleLine.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        return 0;
    }
}
