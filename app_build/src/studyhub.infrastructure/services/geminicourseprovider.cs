using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.Integrations;
using studyhub.application.Contracts.Settings;
using studyhub.application.Interfaces;
using studyhub.application.Interfaces.Providers;
using studyhub.domain.AIContracts;

namespace studyhub.infrastructure.services;

public class GeminiCourseProvider(
    IIntegrationSettingsService integrationSettingsService,
    ILogger<GeminiCourseProvider> logger) : IGeminiCourseProvider
{
    private const string DefaultModel = "gemini-2.5-flash";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private readonly IIntegrationSettingsService _integrationSettingsService = integrationSettingsService;
    private readonly ILogger<GeminiCourseProvider> _logger = logger;

    public Task<OnlineCoursePlanningResponse> PlanOnlineCourseAsync(OnlineCoursePlanningRequest request, CancellationToken cancellationToken = default)
    {
        var systemPrompt = """
            You are an expert curriculum architect for a personal study app.
            Return only valid JSON in pt-BR.
            Build a cohesive online course plan that feels like a real course, not a loose link list.
            Prefer logical progression, practical sequencing, and strong YouTube discovery queries.
            """;

        var userPrompt = $$"""
            Gere um plano pedagogico para um curso online curado.

            Regras:
            - idioma: pt-BR
            - retornar somente JSON
            - o curso deve ter progressao logica
            - os modulos precisam ajudar a quebrar a futura curadoria do YouTube
            - inclua queries de busca de alta qualidade para YouTube

            Intencao:
            {{IntegrationJsonHelper.Serialize(request.Intent)}}

            Estrutura esperada:
            {
              "friendlyTitle": "string",
              "courseDescription": "string",
              "pedagogicalDirection": "string",
              "roadmapMacro": "string",
              "discoveryQueries": ["string"],
              "modules": [
                {
                  "order": 1,
                  "title": "string",
                  "objective": "string",
                  "description": "string",
                  "searchQueries": ["string"],
                  "keywords": ["string"]
                }
              ]
            }
            """;

        return GenerateJsonAsync<OnlineCoursePlanningResponse>(systemPrompt, userPrompt, null, cancellationToken);
    }

    public Task<CoursePresentationResponseContract> GenerateCoursePresentationAsync(CoursePresentationRequestContract request, CancellationToken cancellationToken = default)
    {
        var systemPrompt = """
            You are a senior course copywriter.
            Return only valid JSON in pt-BR.
            Keep titles concise, practical, and suitable for a study app catalog.
            """;

        var userPrompt = $$"""
            Gere apresentacao amigavel para um curso do StudyHub.
            Responda somente JSON.

            Contexto:
            {{IntegrationJsonHelper.Serialize(request)}}

            Estrutura:
            {
              "courseTitle": "string",
              "courseDescription": "string",
              "displayModules": [
                {
                  "rawTitle": "string",
                  "displayTitle": "string"
                }
              ]
            }
            """;

        return GenerateJsonAsync<CoursePresentationResponseContract>(systemPrompt, userPrompt, null, cancellationToken);
    }

    public Task<CourseRoadmapResponseContract> GenerateRoadmapAsync(CourseRoadmapRequestContract request, CancellationToken cancellationToken = default)
    {
        var systemPrompt = """
            You are an expert study planner.
            Return only valid JSON in pt-BR.
            Build a roadmap with levels, stages, checklist blocks, validation questions and common mistakes.
            The roadmap must be realistic for self-study and coherent with the course structure.
            """;

        var userPrompt = $$"""
            Gere um roadmap completo para o curso abaixo.
            Responda somente JSON.

            Contexto:
            {{IntegrationJsonHelper.Serialize(request)}}

            Estrutura:
            {
              "levels": [
                {
                  "order": 1,
                  "kicker": "string",
                  "title": "string",
                  "objective": "string",
                  "detailedGoal": "string",
                  "focusTags": ["string"],
                  "stages": [
                    {
                      "order": 1,
                      "kicker": "string",
                      "title": "string",
                      "subtitle": "string",
                      "blocks": [
                        {
                          "title": "string",
                          "description": "string",
                          "items": [
                            {
                              "description": "string"
                            }
                          ]
                        }
                      ],
                      "masteryExpectation": "string",
                      "commonMistakes": ["string"],
                      "validationQuestions": ["string"]
                    }
                  ]
                }
              ]
            }
            """;

        return GenerateJsonAsync<CourseRoadmapResponseContract>(systemPrompt, userPrompt, null, cancellationToken);
    }

    public Task<CourseTextRefinementResponse> RefineCourseTextAsync(CourseTextRefinementRequest request, CancellationToken cancellationToken = default)
    {
        var systemPrompt = """
            You are a pragmatic instructional editor for a study app.
            Return only valid JSON in pt-BR.
            Refine module and lesson titles lightly, keep them concise, and generate short descriptions.
            Do not invent content unrelated to the course theme.
            """;

        var userPrompt = $$"""
            Refine os textos do curso abaixo.
            Responda somente JSON.

            Contexto:
            {{IntegrationJsonHelper.Serialize(request)}}

            Estrutura:
            {
              "refinedCourseTitle": "string",
              "refinedCourseDescription": "string",
              "modules": [
                {
                  "moduleId": "guid",
                  "title": "string",
                  "description": "string",
                  "lessons": [
                    {
                      "lessonId": "guid",
                      "title": "string",
                      "description": "string"
                    }
                  ]
                }
              ]
            }
            """;

        return GenerateJsonAsync<CourseTextRefinementResponse>(systemPrompt, userPrompt, null, cancellationToken);
    }

    public async Task<AuxiliaryTextTaskResponse> ExecuteAuxiliaryTaskAsync(AuxiliaryTextTaskRequest request, CancellationToken cancellationToken = default)
    {
        var systemPrompt = """
            You are a concise text normalization assistant for a study app.
            Return only the transformed text with no markdown.
            """;

        var userPrompt = $$"""
            Operacao: {{request.Operation}}
            Entrada:
            {{request.Input}}
            """;

        var output = await ExecuteTextAsync(systemPrompt, userPrompt, null, cancellationToken);
        return new AuxiliaryTextTaskResponse
        {
            Output = output.Trim()
        };
    }

    public Task<CourseSupplementaryMaterialsResponseContract> GenerateSupplementaryQueriesAsync(CourseSupplementaryMaterialsRequestContract request, CancellationToken cancellationToken = default)
    {
        var systemPrompt = """
            You are a curator of free supplementary study resources.
            Return only valid JSON in pt-BR.
            Suggest highly relevant YouTube search queries and short curation notes.
            """;

        var userPrompt = $$"""
            Gere queries de curadoria complementar para o curso abaixo.
            Responda somente JSON.

            Contexto:
            {{IntegrationJsonHelper.Serialize(request)}}

            Estrutura:
            {
              "recommendedSearchQueries": ["string"],
              "curationNotes": ["string"]
            }
            """;

        return GenerateJsonAsync<CourseSupplementaryMaterialsResponseContract>(systemPrompt, userPrompt, null, cancellationToken);
    }

    public async Task<ProviderValidationResponse> ValidateApiKeyAsync(ProviderValidationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await GenerateJsonAsync<GeminiHealthCheckResponse>(
                "Return only valid JSON.",
                "Responda exatamente com {\"status\":\"ok\"}.",
                request.ApiKey,
                cancellationToken);

            return new ProviderValidationResponse
            {
                IsValid = true,
                Message = "Conexao com Gemini validada com sucesso."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini API key validation failed.");
            return new ProviderValidationResponse
            {
                IsValid = false,
                Message = $"Falha ao validar Gemini: {ex.Message}"
            };
        }
    }

    private async Task<TResponse> GenerateJsonAsync<TResponse>(
        string systemPrompt,
        string userPrompt,
        string? apiKeyOverride,
        CancellationToken cancellationToken)
    {
        var settings = await _integrationSettingsService.GetSettingsAsync(cancellationToken);
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride)
            ? settings.GeminiApiKey
            : apiKeyOverride;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("A chave do Gemini nao esta configurada.");
        }

        var model = string.IsNullOrWhiteSpace(settings.GeminiModel)
            ? DefaultModel
            : settings.GeminiModel.Trim();

        var payload = new
        {
            system_instruction = new
            {
                parts = new[]
                {
                    new { text = systemPrompt }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = userPrompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                responseMimeType = "application/json",
                thinkingConfig = new
                {
                    thinkingBudget = 0
                }
            }
        };

        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        requestMessage.Headers.Add("x-goog-api-key", apiKey);
        requestMessage.Content = JsonContent.Create(payload, options: IntegrationJsonHelper.JsonOptions);

        using var response = await HttpClient.SendAsync(requestMessage, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildGeminiErrorMessage(rawBody, response.StatusCode));
        }

        var text = ExtractGeminiText(rawBody);
        return IntegrationJsonHelper.Deserialize<TResponse>(text);
    }

    private async Task<string> ExecuteTextAsync(
        string systemPrompt,
        string userPrompt,
        string? apiKeyOverride,
        CancellationToken cancellationToken)
    {
        var settings = await _integrationSettingsService.GetSettingsAsync(cancellationToken);
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride)
            ? settings.GeminiApiKey
            : apiKeyOverride;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("A chave do Gemini nao esta configurada.");
        }

        var model = string.IsNullOrWhiteSpace(settings.GeminiModel)
            ? DefaultModel
            : settings.GeminiModel.Trim();

        var payload = new
        {
            system_instruction = new
            {
                parts = new[]
                {
                    new { text = systemPrompt }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = userPrompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                thinkingConfig = new
                {
                    thinkingBudget = 0
                }
            }
        };

        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        requestMessage.Headers.Add("x-goog-api-key", apiKey);
        requestMessage.Content = JsonContent.Create(payload, options: IntegrationJsonHelper.JsonOptions);

        using var response = await HttpClient.SendAsync(requestMessage, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildGeminiErrorMessage(rawBody, response.StatusCode));
        }

        return ExtractGeminiText(rawBody);
    }

    private static string ExtractGeminiText(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);

        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Gemini nao retornou candidatos de resposta.");
        }

        var parts = candidates[0]
            .GetProperty("content")
            .GetProperty("parts");

        var chunks = new List<string>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textNode))
            {
                chunks.Add(textNode.GetString() ?? string.Empty);
            }
        }

        var merged = string.Join(string.Empty, chunks).Trim();
        if (string.IsNullOrWhiteSpace(merged))
        {
            throw new InvalidOperationException("Gemini retornou uma resposta vazia.");
        }

        return merged;
    }

    private static string BuildGeminiErrorMessage(string rawBody, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var message = document.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString();

            return string.IsNullOrWhiteSpace(message)
                ? $"Gemini retornou erro HTTP {(int)statusCode}."
                : message;
        }
        catch
        {
            return $"Gemini retornou erro HTTP {(int)statusCode}.";
        }
    }

    private sealed class GeminiHealthCheckResponse
    {
        public string Status { get; set; } = string.Empty;
    }
}
