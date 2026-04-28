using CloudScout.Core.Inference;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CloudScout.Core.Tests.Inference;

/// <summary>
/// Verifies the OpenAI wire-format <see cref="HttpLlmInference"/> sends — particularly the
/// content-shape difference between text-only requests (string content) and multimodal
/// requests (content-parts array). These tests don't actually run an LLM; they intercept the
/// outgoing HTTP request via a fake handler and inspect the JSON.
/// </summary>
public class HttpLlmInferenceWireFormatTests
{
    [Fact]
    public async Task Text_only_request_uses_string_content()
    {
        var captured = await CaptureRequestAsync(inference =>
            inference.GenerateAsync("Classify this file."));

        var content = captured.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.String);
        content.GetString().Should().Be("Classify this file.");
    }

    [Fact]
    public async Task Image_request_uses_content_parts_array_with_data_uri()
    {
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // arbitrary; we just check it's encoded
        var captured = await CaptureRequestAsync(inference =>
            inference.GenerateAsync("Classify this image.", imageBytes, "image/jpeg"));

        var content = captured.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.Array);
        content.GetArrayLength().Should().Be(2);

        var textPart = content[0];
        textPart.GetProperty("type").GetString().Should().Be("text");
        textPart.GetProperty("text").GetString().Should().Be("Classify this image.");

        var imagePart = content[1];
        imagePart.GetProperty("type").GetString().Should().Be("image_url");
        var url = imagePart.GetProperty("image_url").GetProperty("url").GetString();
        url.Should().StartWith("data:image/jpeg;base64,");
        url.Should().EndWith(Convert.ToBase64String(imageBytes));
    }

    [Fact]
    public async Task Image_request_falls_back_to_image_jpeg_when_mime_is_null()
    {
        var captured = await CaptureRequestAsync(inference =>
            inference.GenerateAsync("Look", new byte[] { 1, 2, 3 }, imageMimeType: null));

        var url = captured.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content")[1]
            .GetProperty("image_url")
            .GetProperty("url")
            .GetString();

        url.Should().StartWith("data:image/jpeg;base64,");
    }

    private static async Task<JsonDocument> CaptureRequestAsync(Func<HttpLlmInference, Task<string>> call)
    {
        // The handler stashes the outgoing request body so we can assert on its shape, then
        // returns a stub OpenAI-compatible response so the call completes successfully.
        string? capturedBody = null;
        var handler = new CapturingHandler(async req =>
        {
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return JsonContent.Create(new
            {
                choices = new[]
                {
                    new { message = new { role = "assistant", content = "{\"categoryId\":\"x\",\"confidence\":1,\"reason\":\"y\"}" } },
                },
            });
        });

        // We need access to HttpLlmInference's internal HttpClient. The current implementation
        // creates its own — for the test we substitute it via reflection rather than restructure
        // production code just for testing.
        var options = Options.Create(new LlmOptions { ServerUrl = "http://localhost:8080" });
        var serverManager = new LlmServerManager(options, NullLogger<LlmServerManager>.Instance);
        var inference = new HttpLlmInference(options, serverManager, NullLogger<HttpLlmInference>.Instance);

        var httpField = typeof(HttpLlmInference).GetField("_http",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var oldClient = (HttpClient)httpField.GetValue(inference)!;
        oldClient.Dispose();
        httpField.SetValue(inference, new HttpClient(handler));

        // The server manager will try to reach /v1/models — make that succeed via the same handler.
        await call(inference);

        capturedBody.Should().NotBeNull();
        return JsonDocument.Parse(capturedBody!);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpContent>> _respond;
        public CapturingHandler(Func<HttpRequestMessage, Task<HttpContent>> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // GET /v1/models is the readiness probe — return any 200 so EnsureRunningAsync proceeds.
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

            var content = await _respond(request);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
