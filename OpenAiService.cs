﻿using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Images;
using OpenAI_API.Models;
using OpenAI_API.Moderation;
using System.Net.Http.Headers;
using System;
using Newtonsoft.Json;
using System.Text;

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

    public static string DefaultGptModel = "gpt-4";//gpt-4-vision-preview

    public static Conversation CreateContext(OpenAIAPI openAi, string context)
    {
        var conversation = openAi.Chat.CreateConversation();
        conversation.RequestParameters.Temperature = 0.9;
        conversation.RequestParameters.MaxTokens = 1024;
        conversation.Model = new Model(DefaultGptModel);
        if (!string.IsNullOrWhiteSpace(context))
        {
            conversation.AppendSystemMessage(context.Trim());
        }
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

    public class Dalle3ImgData
    {
        public string revised_prompt { get; set; }
        public string url { get; set; }
    }

    public class Dalle3
    {
        public int created { get; set; }
        public List<Dalle3ImgData> data { get; set; }
        public Error error { get; set; }
    }

    public class Error
    {
        public string code { get; set; }
        public string message { get; set; }
        public object param { get; set; }
        public string type { get; set; }
    }

    public static string DefaultVoice = "alloy";

    public async Task<string> TextToSpeachAsync(string text)
    {
        if (text.Length > 1000)
            text = text.Substring(0, 1000);

        try
        {
            using var client = new HttpClient();
            var requestData = new
            {
                model = "tts-1",
                input = text,
                voice = DefaultVoice
            };
            string requestJson = JsonConvert.SerializeObject(requestData);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Constants.OpenAiToken);
            HttpResponseMessage response = await client.SendAsync(request);

            response.EnsureSuccessStatusCode();

            var tmpFile = Path.GetTempFileName() + ".mp3";
            await using var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write);
            await (await response.Content.ReadAsStreamAsync()).CopyToAsync(fs);
            return tmpFile;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string DefaultStyle = "vivid";

    public async Task<Dalle3> GenerateImageAsync_Dalle3(bool isHd, string prompt, int count, Orientation orientation)
    {
        try
        {
            using var client = new HttpClient();

            var res = orientation switch
            {
                Orientation.Landscape => "1792x1024",
                Orientation.Portrait => "1024x1792",
                _ => "1024x1024"
            };

            var requestData = new
            {
                model = "dall-e-3",
                prompt = prompt,
                n = count,
                size = res,
                style = DefaultStyle,
                quality = isHd ? "hd" : "standard"
            };
            string requestJson = JsonConvert.SerializeObject(requestData);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Constants.OpenAiToken);
            HttpResponseMessage response = await client.SendAsync(request);

            var str = await response.Content.ReadAsStringAsync();
            var dalle3 = JsonConvert.DeserializeObject<Dalle3>(str);
            return dalle3;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
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
                _conversation.Model = new Model(DefaultGptModel);
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
                { new StringContent("1"), "n" },
                { new StringContent("dall-e-3"), "model" },
                { new StringContent("1024x1024"), "size" }
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

    public enum Orientation
    {
        Landscape,
        Portrait,
        Square
    }
}