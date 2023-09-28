using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Images;
using OpenAI_API.Models;
using OpenAI_API.Moderation;
using System.Net.Http.Headers;
using System;
using Newtonsoft.Json;

public class HttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        return client;
    }
}

public class OpenAiService
{
    private OpenAIAPI _openAi;
    private Conversation _conversation;
    private string _systemMsg;

    public async Task InitAsync(string systemMessage)
    {
        _openAi = new OpenAIAPI(Constants.OpenAiToken);
        _openAi.HttpClientFactory = new HttpClientFactory();
        _systemMsg = systemMessage;
        NewContext(_systemMsg);
    }

    public void NewContext(string context)
    {
        _conversation = CreateContext(_openAi, context);
    }

    public static Conversation CreateContext(OpenAIAPI openAi, string context)
    {
        var conversation = openAi.Chat.CreateConversation();
        conversation.RequestParameters.Temperature = 0.9;
        conversation.RequestParameters.MaxTokens = 1024;
        conversation.Model = new Model("gpt-4");
        conversation.AppendSystemMessage(context.Trim());
        return conversation;
    }

    public async Task<string> CallModerationAsync(string prompt)
    {
        var result = await _openAi.Moderation.CallModerationAsync(
            new ModerationRequest(prompt, Model.TextModerationLatest));

        string response = result.Results.FirstOrDefault()!
            .CategoryScores.Where(c => c.Value >= 0.0099)
            .OrderByDescending(c => c.Value)
            .Aggregate("", (current, category) => current + $" {category.Key}: {category.Value:F2}\n");
        return response == "" ? "обычный текст, ничего необычного" : "Анализ:\n" + response;
    }

    public async Task<string> CompletionAsync(string prompt)
    {
        try
        {
            var result = await _openAi.Completions.CreateCompletionAsync(prompt);
            return string.Join(" ", result.Completions.Select(c => c.Text));
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string> GenerateImageAsync(string prompt)
    {
        try
        {
            var result = await _openAi.ImageGenerations.CreateImageAsync(prompt);
            return result.Data[0].Url;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string> SendUserInputAsync(string prompt)
    {
        try
        {
            _conversation.AppendUserInput(prompt);
            return await _conversation.GetResponseFromChatbotAsync();
        }
        catch (HttpRequestException e)
        {
            // I'm too lazy to extract error codes
            if (e.Message.Contains("This model's maximum context length"))
            {
                // Spawn a new chat context and try again
                _conversation = _openAi.Chat.CreateConversation();
                _conversation.Model = new Model("gpt-4");
                _conversation.AppendSystemMessage(_systemMsg);
                return "Лимит по токенам, пересоздаю контекст";
            }
            throw;
        }
    }

    public async Task<string> AnalyzeChatSummaryAsync(string context, List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            context = "Я хочу дать тебе историю сообщений из закрытого группового чата на 30 человек где каждое сообщение вида \"[имя участника группы]: текст сообщения\". " +
                      "Пожалуйста, проведи общий анализ авторов сообщений и напиши краткую характеристику каждого участника группы";
        }

        const int threshold = 100;

        Conversation ctx = CreateContext(_openAi, context);
        ctx.AppendUserInput(string.Join("\n", messages.Count > threshold ? messages.TakeLast(threshold) : messages));
        return await ctx.GetResponseFromChatbotAsync();
    }

    public async Task<string[]> GenerateImageVariationAsync(StreamContent content, int count)
    {
        try
        {
            // OpenAI_API doesn't support this API
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Constants.OpenAiToken);
            using var formData = new MultipartFormDataContent
            {
                { content, "image", "file.png" },
                { new StringContent(count.ToString()), "n" },
                { new StringContent("512x512"), "size" }
            };
            var response = await httpClient.PostAsync("https://api.openai.com/v1/images/variations", formData);
            if (response.IsSuccessStatusCode)
            {
                var files = JsonConvert.DeserializeObject<ImageVariationResult>(await response.Content.ReadAsStringAsync());
                return files.data.Select(item => item.url).ToArray();
            }

            string error = await response.Content.ReadAsStringAsync();
            return new[] { $"Failed: {error}" };
        }
        catch (Exception e)
        {
            return new[] { $"Failed: {e.Message}" };
        }
    }

    public async Task<string[]> GenerateImageWithMaskAsync(StreamContent content, string prompt)
    {
        try
        {
            // OpenAI_API doesn't support this API
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Constants.OpenAiToken);
            using var formData = new MultipartFormDataContent
            {
                { content, "image", "file.png" },
                { new StringContent(prompt), "prompt" },
                { new StringContent("1"), "n" },
                { new StringContent("512x512"), "size" }
            };
            var response = await httpClient.PostAsync("https://api.openai.com/v1/images/edits", formData);
            if (response.IsSuccessStatusCode)
            {
                var files = JsonConvert.DeserializeObject<ImageVariationResult>(await response.Content.ReadAsStringAsync());
                return files.data.Select(item => item.url).ToArray();
            }

            string error = await response.Content.ReadAsStringAsync();
            return new[] { $"Failed: {error}" };
        }
        catch (Exception e)
        {
            return new[] { $"Failed: {e.Message}" };
        }
    }

    public class Datum
    {
        public string url { get; set; }
    }

    public class ImageVariationResult
    {
        public int created { get; set; }
        public List<Datum> data { get; set; }
    }
}