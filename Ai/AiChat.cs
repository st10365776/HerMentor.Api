using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace HerMentor.Api.Ai;

public sealed class AiOptions
{
	public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
	public string? ApiKey { get; set; }
	public string? Model { get; set; }
}

public record ChatMessage(string Role, string Content);

/// <summary>An error whose message is safe to show to the end user (HubException messages are sent to the client).</summary>
public sealed class AiException(string message) : HubException(message);

/// <summary>Streams chat completions from any OpenAI-compatible provider (Groq by default).</summary>
public sealed class AiChat(HttpClient http, IOptions<AiOptions> options, ILogger<AiChat> log)
{
	// Tried in order; the first one the provider currently serves is used.
	private static readonly string[] PreferredModels =
	[
		"llama-3.3-70b-versatile",
		"llama-3.1-8b-instant",
		"openai/gpt-oss-20b",
		"openai/gpt-oss-120b"
	];

	private static readonly string[] NotChatModels = ["whisper", "guard", "tts", "embed", "compound", "orpheus", "playai"];

	private string? _model;

	public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

	public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var model = await ResolveModelAsync(ct);

		var isReasoning = model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase);

		using var request = Build(HttpMethod.Post, "chat/completions");
		request.Content = new StringContent(
			JsonSerializer.Serialize(new Dictionary<string, object?>
			{
				["model"] = model,
				["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }),
				["stream"] = true,
				["temperature"] = 0.6,
				["max_tokens"] = isReasoning ? 2000 : 600,
				// Reasoning models spend tokens thinking; keep that small so the answer fits.
				["reasoning_effort"] = isReasoning ? "low" : null
			}.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)),
			Encoding.UTF8,
			"application/json");

		using var response = await SendAsync(request, ct);
		await using var body = await response.Content.ReadAsStreamAsync(ct);
		using var reader = new StreamReader(body);

		while (await reader.ReadLineAsync(ct) is { } line)
		{
			if (!line.StartsWith("data:", StringComparison.Ordinal))
				continue;

			var data = line["data:".Length..].Trim();
			if (data == "[DONE]")
				yield break;

			if (TryReadDelta(data, out var text))
				yield return text;
		}
	}

	/// <summary>Non-streaming convenience for features that need the whole answer (e.g. JSON output).</summary>
	public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
	{
		var sb = new StringBuilder();
		await foreach (var chunk in StreamAsync(messages, ct))
			sb.Append(chunk);
		return sb.ToString();
	}

	private async Task<string> ResolveModelAsync(CancellationToken ct)
	{
		if (!string.IsNullOrWhiteSpace(options.Value.Model))
			return options.Value.Model!;
		if (_model is not null)
			return _model;

		using var request = Build(HttpMethod.Get, "models");
		using var response = await SendAsync(request, ct);
		using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

		var ids = doc.RootElement.GetProperty("data").EnumerateArray()
			.Select(e => e.GetProperty("id").GetString())
			.Where(id => !string.IsNullOrEmpty(id))
			.Select(id => id!)
			.ToList();

		_model = PreferredModels.FirstOrDefault(ids.Contains)
			?? ids.FirstOrDefault(id => !NotChatModels.Any(n => id.Contains(n, StringComparison.OrdinalIgnoreCase)))
			?? throw new AiException("The AI service has no chat models available right now.");

		log.LogInformation("AI model selected: {Model}", _model);
		return _model;
	}

	private HttpRequestMessage Build(HttpMethod method, string path)
	{
		var request = new HttpRequestMessage(method, $"{options.Value.BaseUrl.TrimEnd('/')}/{path}");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
		return request;
	}

	private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		HttpResponseMessage response;
		try
		{
			response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
		{
			log.LogWarning(ex, "AI provider unreachable");
			throw new AiException("The AI service can't be reached right now. Please try again shortly.");
		}

		if (response.IsSuccessStatusCode)
			return response;

		var detail = await response.Content.ReadAsStringAsync(ct);
		log.LogWarning("AI provider returned {Status}: {Detail}", (int)response.StatusCode, detail);
		response.Dispose();

		throw response.StatusCode switch
		{
			HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
				new AiException("The AI key on the server was rejected. Check the Ai:ApiKey setting."),
			HttpStatusCode.TooManyRequests =>
				new AiException("The AI is busy right now. Please try again in a moment."),
			HttpStatusCode.NotFound or HttpStatusCode.BadRequest =>
				new AiException("The AI model isn't available. Check the Ai:Model setting."),
			_ => new AiException("The AI service had a problem. Please try again.")
		};
	}

	private static bool TryReadDelta(string json, out string text)
	{
		text = "";
		try
		{
			using var doc = JsonDocument.Parse(json);
			var choices = doc.RootElement.GetProperty("choices");
			if (choices.GetArrayLength() == 0)
				return false;

			if (choices[0].TryGetProperty("delta", out var delta) &&
				delta.TryGetProperty("content", out var content) &&
				content.ValueKind == JsonValueKind.String)
			{
				text = content.GetString() ?? "";
				return text.Length > 0;
			}
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
		{
			// Ignore keep-alive or malformed chunks.
		}

		return false;
	}
}
