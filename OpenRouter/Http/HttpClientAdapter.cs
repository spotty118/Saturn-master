using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Saturn.OpenRouter.Errors;
using Saturn.OpenRouter.Headers;
using Saturn.OpenRouter.Models.Api;
using Saturn.OpenRouter.Serialization;

namespace Saturn.OpenRouter.Http
{
    /// <summary>
    /// Lightweight wrapper around HttpClient providing consistent headers, JSON (de)serialization,
    /// error handling, and SSE start helper for the OpenRouter API.
    /// </summary>
    public sealed class HttpClientAdapter : IDisposable
    {
        private readonly HttpClient _http;
        private readonly OpenRouterOptions _options;

        /// <summary>
        /// Create a new adapter for the given <see cref="OpenRouterOptions"/>.
        /// </summary>
        public HttpClientAdapter(OpenRouterOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            var handler = options.HttpMessageHandler ?? new HttpClientHandler();
            _http = new HttpClient(handler)
            {
                Timeout = options.Timeout
            };

            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? "https://openrouter.ai/api/v1"
                : options.BaseUrl;

            if (!baseUrl.EndsWith("/"))
            {
                baseUrl += "/";
            }

            _http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        }

        /// <summary>
        /// Create an HttpRequestMessage configured with default and per-call headers.
        /// </summary>
        public HttpRequestMessage CreateRequest(HttpMethod method, string path, IDictionary<string, string>? headers = null, bool acceptSse = false)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

            var request = new HttpRequestMessage(method, path);

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            foreach (var kvp in _options.DefaultHeaders)
            {
                if (!request.Headers.Contains(kvp.Key))
                {
                    request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                }
            }

            AppAttributionHeaders.Append(request, _options, headers);

            if (headers != null)
            {
                foreach (var kvp in headers)
                {
                    if (string.Equals(kvp.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (request.Headers.Contains(kvp.Key))
                        request.Headers.Remove(kvp.Key);

                    request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                }
            }

            var acceptValue = acceptSse ? "text/event-stream" : "application/json";
            if (!request.Headers.Accept.Any())
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptValue));
            }

            return request;
        }

        /// <summary>
        /// Send a request with an optional JSON body and deserialize the JSON response to <typeparamref name="TResponse"/>.
        /// Throws <see cref="OpenRouterException"/> on non-success responses after attempting to parse the API error.
        /// </summary>
        public async Task<TResponse?> SendJsonAsync<TResponse>(
            HttpMethod method,
            string path,
            object? body = null,
            IDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            var request = CreateRequest(method, path, headers, acceptSse: false);

            if (body != null)
            {
                var json = Json.Serialize(body, _options.CreateJsonOptions());
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await ThrowForErrorAsync(response, cancellationToken).ConfigureAwait(false);
                return default;
            }

            if (response.Content is null)
                return default;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            if (stream == Stream.Null)
                return default;
            var resp = await Json.DeserializeAsync<TResponse>(stream, _options.CreateJsonOptions(), cancellationToken).ConfigureAwait(false);
            return resp;
        }

        /// <summary>
        /// Send a request and return the raw <see cref="HttpResponseMessage"/> (disposed by caller).
        /// Throws <see cref="OpenRouterException"/> on non-success status codes.
        /// </summary>
        public async Task<HttpResponseMessage> SendRawAsync(
            HttpMethod method,
            string path,
            HttpContent? content = null,
            IDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            var request = CreateRequest(method, path, headers, acceptSse: false);
            request.Content = content;

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    await ThrowForErrorAsync(response, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    response.Dispose();
                }
            }

            return response;
        }

        /// <summary>
        /// Start a Server-Sent Events request and return an async stream of <see cref="SseEvent"/>.
        /// The underlying HTTP response is disposed automatically when iteration ends.
        /// </summary>
        public async IAsyncEnumerable<SseEvent> StartSseAsync(
            HttpMethod method,
            string path,
            HttpContent? content = null,
            IDictionary<string, string>? headers = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = CreateRequest(method, path, headers, acceptSse: true);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            if (content != null)
            {
                if (content.Headers.ContentType is null)
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
                    {
                        CharSet = "utf-8"
                    };
                }
                request.Content = content;
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await ThrowForErrorAsync(response, cancellationToken).ConfigureAwait(false);
                yield break;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var evt in SseStream.ReadEventsAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }
        }

        private async Task ThrowForErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var status = response.StatusCode;
            string? payload = null;
            try
            {
                payload = response.Content is null
                    ? null
                    : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HttpClientAdapter] Failed to read error response content from OpenRouter API (Status: {(int)status} {response.ReasonPhrase}): {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    var parsed = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(payload!, _options.CreateJsonOptions());
                    var code = parsed?.Error?.Code;
                    var message = parsed?.Error?.Message ?? $"Request failed with status {(int)status} {response.ReasonPhrase}.";
                    var metadata = parsed?.Error?.Metadata;

                    if (metadata != null && metadata.Count > 0)
                    {
                        try
                        {
                            if (metadata.TryGetValue("provider_name", out var providerNameEl) && providerNameEl.ValueKind == JsonValueKind.String)
                            {
                                var providerName = providerNameEl.GetString();
                                if (!string.IsNullOrWhiteSpace(providerName))
                                {
                                    message += $" | provider={providerName}";
                                }
                            }
                            if (metadata.TryGetValue("raw", out var rawEl))
                            {
                                var rawStr = rawEl.ToString();
                                if (!string.IsNullOrWhiteSpace(rawStr))
                                {
                                    var snippet = Truncate(rawStr, 512).Replace("\r", " ").Replace("\n", " ");
                                    message += $" | raw={snippet}";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[HttpClientAdapter] Failed to parse error metadata from OpenRouter response: {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    throw new OpenRouterException(status, message, apiErrorCode: code, metadata: metadata);
                }
            }
            catch (OpenRouterException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HttpClientAdapter] Failed to deserialize OpenRouter error response (Status: {(int)status} {response.ReasonPhrase}): {ex.GetType().Name}: {ex.Message}");
            }

            var generic = $"Request failed with status {(int)status} {response.ReasonPhrase}.";
            if (!string.IsNullOrWhiteSpace(payload))
            {
                generic += $" Body: {Truncate(payload!, 2048)}";
            }

            throw new OpenRouterException(status, generic);
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value.Substring(0, max);

        /// <summary>Dispose the underlying HttpClient.</summary>
        public void Dispose()
        {
            _http.Dispose();
        }
    }
}