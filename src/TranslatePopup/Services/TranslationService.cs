using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TranslatePopup.Models;

namespace TranslatePopup.Services;

public sealed class TranslationException : Exception
{
    public TranslationException(string message) : base(message) { }
}

public sealed class TranslationService
{
    private const string Endpoint = "https://api.cognitive.microsofttranslator.com";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    // Fallback list used when the languages endpoint cannot be reached.
    private static readonly IReadOnlyList<TranslationLanguage> FallbackLanguages = new List<TranslationLanguage>
    {
        new("ja", "日本語"),
        new("en", "英語"),
        new("zh-Hans", "中国語（簡体字）"),
        new("zh-Hant", "中国語（繁体字）"),
        new("ko", "韓国語"),
        new("fr", "フランス語"),
        new("de", "ドイツ語"),
        new("es", "スペイン語"),
        new("it", "イタリア語"),
        new("pt", "ポルトガル語"),
        new("ru", "ロシア語"),
        new("vi", "ベトナム語"),
        new("th", "タイ語"),
        new("id", "インドネシア語"),
        new("ar", "アラビア語"),
    };

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguageCode,
        string apiKey,
        string region,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new TranslationException("APIキーが設定されていません。設定画面から入力してください。");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{Endpoint}/translate?api-version=3.0&to={Uri.EscapeDataString(targetLanguageCode)}");

        request.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
        if (!string.IsNullOrWhiteSpace(region))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Region", region);
        }

        var body = new[] { new { Text = text } };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only a real HttpClient.Timeout lands here; a deliberate cancellation (e.g. the
            // caller switching languages again) should propagate as-is, not be reported as an error.
            throw new TranslationException("翻訳リクエストがタイムアウトしました。ネットワーク接続を確認してください。");
        }
        catch (HttpRequestException)
        {
            throw new TranslationException("翻訳サーバーに接続できませんでした。ネットワーク接続を確認してください。");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new TranslationException(BuildErrorMessage(response));
        }

        var payload = await response.Content
            .ReadFromJsonAsync<List<TranslateResponseItem>>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var item = payload?.FirstOrDefault();
        var translation = item?.Translations?.FirstOrDefault();
        if (translation is null)
        {
            throw new TranslationException("翻訳結果を取得できませんでした。");
        }

        return new TranslationResult(translation.Text, item?.DetectedLanguage?.Language);
    }

    public async Task<IReadOnlyList<TranslationLanguage>> GetSupportedLanguagesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http
                .GetAsync($"{Endpoint}/languages?api-version=3.0&scope=translation", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return FallbackLanguages;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<LanguagesResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (payload?.Translation is null || payload.Translation.Count == 0)
            {
                return FallbackLanguages;
            }

            return payload.Translation
                .Select(kv => new TranslationLanguage(kv.Key, kv.Value.Name))
                .OrderBy(l => l.Name, StringComparer.CurrentCulture)
                .ToList();
        }
        catch
        {
            return FallbackLanguages;
        }
    }

    private static string BuildErrorMessage(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        return statusCode switch
        {
            401 or 403 => "APIキーが正しくありません。設定を確認してください。",
            429 => "リクエストが多すぎます。しばらく待ってから再試行してください。",
            _ => $"翻訳に失敗しました（エラーコード: {statusCode}）。",
        };
    }

    private sealed class TranslateResponseItem
    {
        [JsonPropertyName("translations")]
        public List<TranslateItem>? Translations { get; set; }

        [JsonPropertyName("detectedLanguage")]
        public DetectedLanguageItem? DetectedLanguage { get; set; }
    }

    private sealed class TranslateItem
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class DetectedLanguageItem
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;
    }

    private sealed class LanguagesResponse
    {
        [JsonPropertyName("translation")]
        public Dictionary<string, LanguageNameItem>? Translation { get; set; }
    }

    private sealed class LanguageNameItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
