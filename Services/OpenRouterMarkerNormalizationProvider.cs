#nullable enable

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using healthspan_api.Options;
using Microsoft.Extensions.Options;

public class OpenRouterMarkerNormalizationProvider : IMarkerNormalizationProvider
{
    private const string ProviderName = "OpenRouter";
    private readonly HttpClient _httpClient;
    private readonly LlmMarkerNormalizationSettings _settings;
    private readonly ILogger<OpenRouterMarkerNormalizationProvider> _logger;

    public OpenRouterMarkerNormalizationProvider(HttpClient httpClient, IOptions<LlmMarkerNormalizationSettings> settings, ILogger<OpenRouterMarkerNormalizationProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<MarkerNormalizationResult> NormalizeAsync(MarkerNormalizationRequest request, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return DisabledResult("LLM marker normalization is disabled.");
        }

        if (!string.Equals(_settings.Provider, ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return DisabledResult($"Configured LLM provider '{_settings.Provider}' does not match {ProviderName}.");
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.Model))
        {
            return DisabledResult("OpenRouter marker normalization requires both an API key and a model.");
        }

        var compactOcrText = BuildCompactOcrText(request.RawExtractionPayload, _settings.MaxOcrChars);
        try
        {
            if (ShouldUseChunkFirst(request))
            {
                _logger.LogInformation(
                    "OpenRouter chunk-first mode enabled. CandidateMarkers: {CandidateMarkers}. ChunkSize: {ChunkSize}",
                    request.CandidateMarkers.Count,
                    _settings.ChunkSize);
                return await NormalizeInChunksAsync(request, compactOcrText, cancellationToken);
            }

            return await ExecuteNormalizationAsync(request, compactOcrText, cancellationToken, allowChunkFallback: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenRouter marker normalization threw unexpectedly.");
            return FailedResult($"OpenRouter marker normalization failed: {ex.Message}", null, 0, null, null);
        }
    }

    private async Task<MarkerNormalizationResult> ExecuteNormalizationAsync(MarkerNormalizationRequest request, string compactOcrText, CancellationToken cancellationToken, bool allowChunkFallback)
    {
        var requestBody = CreateRequestBody(request, compactOcrText, useJsonMode: true);
        var rawRequest = JsonSerializer.Serialize(requestBody);
        var stopwatch = Stopwatch.StartNew();

        LogRequestSummary(request, compactOcrText, rawRequest, useJsonMode: true);
        var attempt = await SendRequestAsync(rawRequest, useJsonMode: true, cancellationToken);

        if (!attempt.Response.IsSuccessStatusCode && ShouldRetryWithoutJsonMode(attempt.ResponseBody))
        {
            _logger.LogInformation("OpenRouter provider rejected JSON mode. Retrying without response_format.");
            var retryRequestBody = CreateRequestBody(request, compactOcrText, useJsonMode: false);
            rawRequest = JsonSerializer.Serialize(retryRequestBody);
            LogRequestSummary(request, compactOcrText, rawRequest, useJsonMode: false);
            attempt = await SendRequestAsync(rawRequest, useJsonMode: false, cancellationToken);
        }

        stopwatch.Stop();

        if (!attempt.Response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenRouter normalization failed. Status: {Status}. Body: {Body}", attempt.Response.StatusCode, attempt.ResponseBody);
            return FailedResult($"OpenRouter marker normalization failed with status {(int)attempt.Response.StatusCode}.", attempt.ResponseBody, stopwatch.ElapsedMilliseconds, rawRequest.Length / 4, null);
        }

        LogResponseSummary(attempt.ResponseBody);

        try
        {
            var result = ParseResponse(attempt.ResponseBody);
            result.ProviderName = ProviderName;
            result.Model ??= _settings.Model;
            result.PromptVersion = _settings.PromptVersion;
            result.RawResponse = attempt.ResponseBody;
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            result.InputTokenEstimate ??= rawRequest.Length / 4;
            result.OutputTokenEstimate ??= attempt.ResponseBody.Length / 4;

            if (!result.Success)
            {
                _logger.LogWarning("OpenRouter normalization returned no usable markers. Body: {Body}", attempt.ResponseBody);
            }

            return result;
        }
        catch (JsonException ex) when (allowChunkFallback && ShouldRetryInChunks(request))
        {
            _logger.LogWarning(ex, "OpenRouter returned truncated or invalid JSON. Retrying in chunks. CandidateMarkers: {CandidateMarkers}, ChunkSize: {ChunkSize}", request.CandidateMarkers.Count, _settings.ChunkSize);
            return await NormalizeInChunksAsync(request, compactOcrText, cancellationToken);
        }
    }

    private async Task<MarkerNormalizationResult> NormalizeInChunksAsync(MarkerNormalizationRequest request, string compactOcrText, CancellationToken cancellationToken)
    {
        return await NormalizeInChunksAsync(request, compactOcrText, Math.Max(2, _settings.ChunkSize), cancellationToken, depth: 0);
    }

    private async Task<MarkerNormalizationResult> NormalizeInChunksAsync(MarkerNormalizationRequest request, string compactOcrText, int chunkSize, CancellationToken cancellationToken, int depth)
    {
        var aggregatedMarkers = new List<OcrMarkerResult>();
        var rawResponses = new List<string>();
        var errors = new List<string>();
        long totalLatencyMs = 0;

        foreach (var chunk in request.CandidateMarkers.Chunk(chunkSize).Select((markers, index) => new { Markers = markers.ToList(), Index = index + 1 }))
        {
            var chunkRequest = new MarkerNormalizationRequest
            {
                FileName = request.FileName,
                ContentType = request.ContentType,
                RawExtractionPayload = request.RawExtractionPayload,
                CandidateMarkers = chunk.Markers
            };

            _logger.LogInformation("OpenRouter chunk retry {ChunkIndex}. ChunkMarkers: {ChunkMarkers}. ChunkSize: {ChunkSize}. Depth: {Depth}", chunk.Index, chunk.Markers.Count, chunkSize, depth);

            MarkerNormalizationResult chunkResult;
            try
            {
                chunkResult = await ExecuteNormalizationAsync(chunkRequest, compactOcrText, cancellationToken, allowChunkFallback: false);
            }
            catch (JsonException ex) when (chunk.Markers.Count > 1)
            {
                var nextChunkSize = Math.Max(1, chunk.Markers.Count / 2);
                _logger.LogWarning(ex, "OpenRouter chunk {ChunkIndex} still returned truncated JSON. Splitting again. ChunkMarkers: {ChunkMarkers}. NextChunkSize: {NextChunkSize}. Depth: {Depth}", chunk.Index, chunk.Markers.Count, nextChunkSize, depth + 1);
                chunkResult = await NormalizeInChunksAsync(chunkRequest, compactOcrText, nextChunkSize, cancellationToken, depth + 1);
            }

            totalLatencyMs += chunkResult.LatencyMs ?? 0;

            if (!string.IsNullOrWhiteSpace(chunkResult.RawResponse))
            {
                rawResponses.Add($"chunk-{chunk.Index}:{chunkResult.RawResponse}");
            }

            if (chunkResult.Markers.Count > 0)
            {
                AppendDistinctMarkers(aggregatedMarkers, chunkResult.Markers);
            }
            else if (!string.IsNullOrWhiteSpace(chunkResult.ErrorMessage))
            {
                errors.Add($"chunk-{chunk.Index}: {chunkResult.ErrorMessage}");
            }
        }

        return new MarkerNormalizationResult
        {
            Success = aggregatedMarkers.Count > 0,
            ProviderName = ProviderName,
            Model = _settings.Model,
            PromptVersion = _settings.PromptVersion,
            Markers = aggregatedMarkers,
            RawResponse = rawResponses.Count == 0 ? null : string.Join("\n", rawResponses),
            LatencyMs = totalLatencyMs,
            ErrorMessage = errors.Count == 0 ? null : string.Join(" | ", errors)
        };
    }

    private static void AppendDistinctMarkers(List<OcrMarkerResult> target, List<OcrMarkerResult> source)
    {
        var seen = target
            .Select(BuildMarkerFingerprint)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var marker in source)
        {
            var fingerprint = BuildMarkerFingerprint(marker);
            if (seen.Add(fingerprint))
            {
                target.Add(marker);
            }
        }
    }

    private static string BuildMarkerFingerprint(OcrMarkerResult marker)
    {
        var originalName = string.IsNullOrWhiteSpace(marker.OriginalName) ? marker.Name : marker.OriginalName;
        var unit = string.IsNullOrWhiteSpace(marker.Unit) ? string.Empty : marker.Unit.Trim();
        return $"{originalName?.Trim()}|{marker.Name.Trim()}|{marker.Value}|{unit}";
    }

    private object CreateRequestBody(MarkerNormalizationRequest request, string compactOcrText, bool useJsonMode)
    {
        const string normalizationInstructions = "Normalize OCR-extracted blood test markers. Return JSON only. Never invent a marker that does not appear in the OCR text or candidate list. Prefer dropping uncertain rows over guessing. Output schema: {\"markers\":[{\"originalName\":string,\"canonicalName\":string,\"unit\":string|null,\"valueText\":string|null,\"value\":number|null,\"confidence\":number|null}],\"notes\":string|null}. Canonical names should be concise marker labels. Keep originalName as the OCR/raw label when possible.";

        var payload = new
        {
            instructions = normalizationInstructions,
            fileName = request.FileName,
            contentType = request.ContentType,
            candidateMarkers = request.CandidateMarkers
                .Take(Math.Max(1, _settings.MaxCandidateMarkers))
                .Select(marker => new
            {
                originalName = marker.OriginalName ?? marker.Name,
                name = marker.Name,
                unit = marker.Unit,
                valueText = marker.ValueText,
                value = marker.Value,
                confidence = marker.Confidence
            }).ToList(),
            ocrText = compactOcrText
        };

        return new
        {
            model = _settings.Model,
            stream = false,
            max_tokens = _settings.MaxOutputTokens,
            temperature = _settings.Temperature,
            response_format = useJsonMode ? new
            {
                type = "json_object"
            } : null,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(payload)
                }
            }
        };
    }

    private async Task<(HttpResponseMessage Response, string ResponseBody)> SendRequestAsync(string rawRequest, bool useJsonMode, CancellationToken cancellationToken)
    {
        LogPayload("OpenRouter request payload", rawRequest, useJsonMode);

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(rawRequest, Encoding.UTF8, "application/json")
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        if (!string.IsNullOrWhiteSpace(_settings.SiteUrl))
        {
            message.Headers.TryAddWithoutValidation("HTTP-Referer", _settings.SiteUrl);
        }

        if (!string.IsNullOrWhiteSpace(_settings.AppName))
        {
            message.Headers.TryAddWithoutValidation("X-Title", _settings.AppName);
        }

        var response = await _httpClient.SendAsync(message, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        LogPayload($"OpenRouter response payload (status {(int)response.StatusCode})", responseBody, useJsonMode);
        return (response, responseBody);
    }

    private void LogPayload(string label, string payload, bool useJsonMode)
    {
        if (!_settings.LogPayloads)
        {
            return;
        }

        _logger.LogInformation(
            "{Label}. JsonMode: {JsonMode}. Payload: {Payload}",
            label,
            useJsonMode,
            TrimForLogging(payload));
    }

    private void LogRequestSummary(MarkerNormalizationRequest request, string compactOcrText, string rawRequest, bool useJsonMode)
    {
        if (!_settings.LogPayloads)
        {
            return;
        }

        _logger.LogInformation(
            "OpenRouter request summary. JsonMode: {JsonMode}. Model: {Model}. CandidateMarkers: {CandidateMarkers}. OCRChars: {OCRChars}. RequestChars: {RequestChars}. FileName: {FileName}",
            useJsonMode,
            _settings.Model,
            request.CandidateMarkers.Count,
            compactOcrText.Length,
            rawRequest.Length,
            request.FileName);
    }

    private void LogResponseSummary(string responseBody)
    {
        if (!_settings.LogPayloads)
        {
            return;
        }

        var assistantContent = TryExtractAssistantContent(responseBody);
        var finishReason = TryExtractFinishReason(responseBody);
        _logger.LogInformation(
            "OpenRouter response summary. FinishReason: {FinishReason}. ResponseChars: {ResponseChars}. AssistantContentChars: {AssistantContentChars}. AssistantContent: {AssistantContent}",
            finishReason,
            responseBody.Length,
            assistantContent?.Length ?? 0,
            TrimForLogging(assistantContent ?? string.Empty));
    }

    private static string? TryExtractAssistantContent(string responseBody)
    {
        try
        {
            using var responseDocument = JsonDocument.Parse(responseBody);
            var root = responseDocument.RootElement;
            return root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractFinishReason(string responseBody)
    {
        try
        {
            using var responseDocument = JsonDocument.Parse(responseBody);
            var root = responseDocument.RootElement;
            return root
                .GetProperty("choices")[0]
                .GetProperty("finish_reason")
                .GetString();
        }
        catch
        {
            return null;
        }
    }

    private string TrimForLogging(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        var maxChars = Math.Max(1000, _settings.LogPayloadMaxChars);
        if (payload.Length <= maxChars)
        {
            return payload;
        }

        return $"{payload[..maxChars]}\n[truncated for log]";
    }

    private static bool ShouldRetryWithoutJsonMode(string responseBody)
    {
        return responseBody.Contains("JSON mode is not enabled", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldRetryInChunks(MarkerNormalizationRequest request)
    {
        return request.CandidateMarkers.Count > Math.Max(5, _settings.ChunkSize);
    }

    private bool ShouldUseChunkFirst(MarkerNormalizationRequest request)
    {
        return request.CandidateMarkers.Count > Math.Max(5, _settings.ChunkSize);
    }

    private static string BuildCompactOcrText(string? rawExtractionPayload, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(rawExtractionPayload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(rawExtractionPayload);
            if (document.RootElement.TryGetProperty("FilteredContent", out var filteredContentElement) && filteredContentElement.ValueKind == JsonValueKind.String)
            {
                return TrimToLength(filteredContentElement.GetString(), maxChars);
            }

            if (document.RootElement.TryGetProperty("Content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
            {
                return TrimToLength(contentElement.GetString(), maxChars);
            }
        }
        catch
        {
        }

        return TrimToLength(rawExtractionPayload, maxChars);
    }

    private static string TrimToLength(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return $"{normalized[..maxChars]}\n[truncated]";
    }

    private static MarkerNormalizationResult ParseResponse(string responseBody)
    {
        using var responseDocument = JsonDocument.Parse(responseBody);
        var root = responseDocument.RootElement;
        var content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        var json = ExtractJson(content);
        using var contentDocument = JsonDocument.Parse(json);
        var contentRoot = contentDocument.RootElement;
        var markers = new List<OcrMarkerResult>();

        if (contentRoot.TryGetProperty("markers", out var markersElement) && markersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var markerElement in markersElement.EnumerateArray())
            {
                var name = ReadString(markerElement, "canonicalName") ?? ReadString(markerElement, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                markers.Add(new OcrMarkerResult
                {
                    OriginalName = ReadString(markerElement, "originalName") ?? name,
                    Name = name,
                    Unit = ReadNullableString(markerElement, "unit"),
                    ValueText = ReadNullableString(markerElement, "valueText"),
                    Value = ReadNullableDecimal(markerElement, "value"),
                    Confidence = ReadNullableDouble(markerElement, "confidence")
                });
            }
        }

        return new MarkerNormalizationResult
        {
            Success = markers.Count > 0,
            Markers = markers,
            OutputTokenEstimate = responseBody.Length / 4,
            InputTokenEstimate = root.TryGetProperty("usage", out var usageElement) && usageElement.TryGetProperty("prompt_tokens", out var promptTokens)
                ? promptTokens.GetInt32()
                : null,
            Model = root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
                ? modelElement.GetString()
                : null,
            ErrorMessage = markers.Count > 0 ? null : "OpenRouter returned no normalized markers."
        };
    }

    private static string ExtractJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenRouter returned an empty response body.");
        }

        var trimmed = content.Trim();
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            throw new InvalidOperationException("OpenRouter response did not contain a JSON object.");
        }

        return trimmed[firstBrace..(lastBrace + 1)];
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return valueElement.GetString()?.Trim();
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement))
        {
            return null;
        }

        return valueElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => valueElement.GetString()?.Trim(),
            _ => valueElement.ToString().Trim()
        };
    }

    private static decimal? ReadNullableDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement))
        {
            return null;
        }

        if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetDecimal(out var numericValue))
        {
            return numericValue;
        }

        if (valueElement.ValueKind == JsonValueKind.String && decimal.TryParse(valueElement.GetString(), out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private static double? ReadNullableDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement))
        {
            return null;
        }

        if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetDouble(out var numericValue))
        {
            return numericValue;
        }

        if (valueElement.ValueKind == JsonValueKind.String && double.TryParse(valueElement.GetString(), out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private MarkerNormalizationResult DisabledResult(string message)
    {
        return new MarkerNormalizationResult
        {
            Success = false,
            ProviderName = ProviderName,
            Model = _settings.Model,
            PromptVersion = _settings.PromptVersion,
            ErrorMessage = message
        };
    }

    private MarkerNormalizationResult FailedResult(string message, string? rawResponse, long latencyMs, int? inputTokens, int? outputTokens)
    {
        return new MarkerNormalizationResult
        {
            Success = false,
            ProviderName = ProviderName,
            Model = _settings.Model,
            PromptVersion = _settings.PromptVersion,
            ErrorMessage = message,
            RawResponse = rawResponse,
            LatencyMs = latencyMs,
            InputTokenEstimate = inputTokens,
            OutputTokenEstimate = outputTokens
        };
    }
}