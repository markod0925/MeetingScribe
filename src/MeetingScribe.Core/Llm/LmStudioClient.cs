using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeetingScribe.Core.Models;

namespace MeetingScribe.Core.Llm;

public enum LmFailureKind
{
    Connection,
    ServiceUnavailable,
    Timeout,
    Unknown
}

public sealed class LmStudioException(string message, LmFailureKind kind, Exception? inner = null) : Exception(message, inner)
{
    public LmFailureKind Kind { get; } = kind;
}

public sealed class LmStudioClient(HttpClient httpClient)
{
    public async Task<(MeetingSummary? Summary, string Raw)> SummarizeAsync(
        string baseUrl,
        string model,
        string systemPrompt,
        string userPrompt,
        string repairPrompt,
        string rawOutPath,
        int retries,
        int retryDelaySec,
        CancellationToken ct)
    {
        Exception? lastError = null;
        var rawBuffer = new List<string>();

        for (var attempt = 1; attempt <= retries; attempt++)
        {
            try
            {
                var response = await PostCompletionAsync(baseUrl, model, systemPrompt, userPrompt, 0.2, ct);
                rawBuffer.Add($"=== ATTEMPT {attempt} ==={Environment.NewLine}{response.RawEnvelope}");

                await PersistRawAsync(rawOutPath, rawBuffer, ct);

                if (!response.IsSuccess)
                {
                    lastError = response.Error;
                }
                else
                {
                    if (TryParseSummary(response.Content, out var summary))
                    {
                        return (summary, string.Join(Environment.NewLine, rawBuffer));
                    }

                    var repairResponse = await PostCompletionAsync(baseUrl, model, systemPrompt, response.Content + Environment.NewLine + repairPrompt, 0.0, ct);
                    rawBuffer.Add($"=== REPAIR {attempt} ==={Environment.NewLine}{repairResponse.RawEnvelope}");
                    await PersistRawAsync(rawOutPath, rawBuffer, ct);

                    if (repairResponse.IsSuccess && TryParseSummary(repairResponse.Content, out summary))
                    {
                        return (summary, string.Join(Environment.NewLine, rawBuffer));
                    }
                }
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                lastError = new LmStudioException("LM Studio timeout.", LmFailureKind.Timeout, ex);
            }
            catch (HttpRequestException ex)
            {
                lastError = ClassifyHttpException(ex);
            }
            catch (Exception ex)
            {
                lastError = new LmStudioException("Unexpected LM Studio error.", LmFailureKind.Unknown, ex);
            }

            if (attempt < retries)
            {
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySec), ct);
            }
        }

        await PersistRawAsync(rawOutPath, rawBuffer, ct);
        throw new InvalidOperationException("LM Studio request failed after retries.", lastError);
    }

    private async Task<(bool IsSuccess, string Content, string RawEnvelope, Exception? Error)> PostCompletionAsync(
        string baseUrl,
        string model,
        string systemPrompt,
        string userPrompt,
        double temperature,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature
            })
        };

        using var res = await httpClient.SendAsync(req, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            var kind = res.StatusCode == HttpStatusCode.ServiceUnavailable ? LmFailureKind.ServiceUnavailable : LmFailureKind.Unknown;
            return (false, string.Empty, raw, new LmStudioException($"LM Studio returned {(int)res.StatusCode}.", kind));
        }

        return (true, ExtractContent(raw), raw, null);
    }

    private static async Task PersistRawAsync(string rawOutPath, IReadOnlyList<string> chunks, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(rawOutPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(rawOutPath, string.Join(Environment.NewLine + Environment.NewLine, chunks), ct);
    }

    private static LmStudioException ClassifyHttpException(HttpRequestException ex)
    {
        var kind = ex.StatusCode == HttpStatusCode.ServiceUnavailable ? LmFailureKind.ServiceUnavailable : LmFailureKind.Connection;
        return new LmStudioException("LM Studio connection failure.", kind, ex);
    }

    private static string ExtractContent(string completionJson)
    {
        using var doc = JsonDocument.Parse(completionJson);
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }

        return completionJson;
    }

    private static bool TryParseSummary(string json, out MeetingSummary? summary)
    {
        try
        {
            summary = JsonSerializer.Deserialize<MeetingSummary>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return summary is not null;
        }
        catch
        {
            summary = null;
            return false;
        }
    }
}
