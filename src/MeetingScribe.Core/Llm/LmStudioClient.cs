using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MeetingScribe.Core.Models;

namespace MeetingScribe.Core.Llm;

public sealed class LmStudioClient(HttpClient httpClient)
{
    public async Task<(MeetingSummary? Summary, string Raw)> SummarizeAsync(string baseUrl, string model, string systemPrompt, string userPrompt, string rawOutPath, int retries, int retryDelaySec, CancellationToken ct)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= retries; attempt++)
        {
            try
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
                        temperature = 0.2
                    })
                };

                using var res = await httpClient.SendAsync(req, ct);
                var text = await res.Content.ReadAsStringAsync(ct);
                await File.WriteAllTextAsync(rawOutPath, text, ct);

                if (!res.IsSuccessStatusCode)
                {
                    lastError = new HttpRequestException($"LM Studio returned {(int)res.StatusCode}");
                }
                else
                {
                    var json = ExtractContent(text);
                    if (TryParseSummary(json, out var summary)) return (summary, text);
                    var repaired = await RepairAsync(baseUrl, model, systemPrompt, json, ct);
                    await File.WriteAllTextAsync(rawOutPath, repaired, ct);
                    if (TryParseSummary(repaired, out summary)) return (summary, repaired);
                    return (null, repaired);
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(retryDelaySec), ct);
        }

        throw new InvalidOperationException("LM Studio request failed after retries.", lastError);
    }

    private async Task<string> RepairAsync(string baseUrl, string model, string priorSystem, string invalidJson, CancellationToken ct)
    {
        const string repairPrompt = "Your previous output was invalid JSON. Return ONLY valid JSON matching the schema. No commentary.";
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = priorSystem },
                    new { role = "system", content = repairPrompt },
                    new { role = "user", content = invalidJson }
                },
                temperature = 0.0
            })
        };
        using var res = await httpClient.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        return ExtractContent(text);
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
