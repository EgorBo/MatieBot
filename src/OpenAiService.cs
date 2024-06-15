using System.Net.Http.Headers;
using System.Text;
using Google.Apis.CustomSearchAPI.v1.Data;
using Google.Apis.CustomSearchAPI.v1;
using Google.Apis.Services;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Models;

public class OpenAiService
{
    private readonly OpenAIAPI _openAi = new(Constants.OpenAiToken) { HttpClientFactory = new HttpClientFactory() };
    private Conversation _conversation;

    public void NewContext(string context) => _conversation = CreateContext(_openAi, context);

    public static string DefaultGptModel { get; set; } = "gpt-4o";
    public static string DefaultVoice { get; set; } = "alloy";
    public static string DefaultStyle { get; set; } = "vivid";

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

    public async Task<string> SearchInGoogleAsync(string query)
    {
        try
        {
            // Clean the input query (remove botname, etc)
            if (query.StartsWith(Constants.BotName, StringComparison.OrdinalIgnoreCase))
                query = query.Substring(Constants.BotName.Length);
            if (query.StartsWith(Constants.AltBotName, StringComparison.OrdinalIgnoreCase))
                query = query.Substring(Constants.BotName.Length);
            query = query.Trim(' ', ',', '\r', '\n', '\t');

            // Google Search (2 results)
            var service = new CustomSearchAPIService(new BaseClientService.Initializer { ApiKey = Constants.GoogleApiKey });
            CseResource.ListRequest listRequest = service.Cse.List();
            listRequest.Cx = Constants.GoogleSearchId;
            listRequest.Q = query;
            listRequest.Num = 1;
            Search search = await listRequest.ExecuteAsync();

            // Walk through the results and get the raw html
            string rawHtml = "";
            foreach (Result result in search.Items)
            {
                try
                {
                    var html = await new HttpClient().GetStringAsync(result.Link);

                    // Now the hard part: clean the html from scripts, tags, keep only meaningful text
                    // my implementation sucks for sure.

                    html = Regex.Replace(html, "(\\<script(.+?)\\</script\\>)|(\\<style(.+?)\\</style\\>)", "");
                    html = Regex.Replace(html, "<[^>]*>", "");
                    html = html.Replace("\t", " ").Replace("\n", " ").Replace("\r", " ").Replace("  ", " ").Replace("  ", " ");
                    if (html.Length > 4000)
                        html = html.Substring(0, 4000);
                    rawHtml += "\n\n======================\n\n" + html;
                }
                catch
                {
                    // ignore
                }
            }

            var conversation = _openAi.Chat.CreateConversation();
            conversation.Model = new Model(DefaultGptModel);
            conversation.AppendSystemMessage("Ты поисковой бот");
            conversation.AppendUserInput($"Пользователь ищет в интернете запрос: \"{query}\". Проанализируй следующие необработанные поисковые результаты и попытайся предоставить какие-нибудь результаты на его запрос:\n\n" + rawHtml); ;
            return await conversation.GetResponseFromChatbotAsync();
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public class ImageGenerationData
    {
        public string revised_prompt { get; set; }
        public string url { get; set; }
    }

    public class ImageGenerationResponse
    {
        public List<ImageGenerationData> data { get; set; }
        public Error error { get; set; }
    }

    public class Error
    {
        public string message { get; set; }
    }

    public async Task<string> TextToSpeachAsync(string text)
    {
        if (text.Length > 1000)
            text = text.Substring(0, 1000);

        try
        {
            using var client = new HttpClient();
            var requestData = new
            {
                model = "tts-1-hd",
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

    public async Task<ImageGenerationResponse> GenerateImageAsync(bool isHd, string prompt, int count = 1, Orientation orientation = Orientation.Square)
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
            var dalle3 = JsonConvert.DeserializeObject<ImageGenerationResponse>(str);
            return dalle3;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    public async Task<string> SendUserInputAsync(string prompt)
    {
        try
        {
            _conversation ??= CreateContext(_openAi, Constants.ChatGptSystemMessage);
            _conversation.AppendUserInput(prompt);
            return await _conversation.GetResponseFromChatbotAsync();
        }
        catch (HttpRequestException e)
        {
            // I'm too lazy to extract error codes
            if (e.Message.Contains("maximum context length"))
            {
                // Spawn a new chat context and try again
                _conversation = _openAi.Chat.CreateConversation();
                _conversation.Model = new Model(DefaultGptModel);
                _conversation.AppendSystemMessage(Constants.ChatGptSystemMessage);
                return "Лимит по токенам, пересоздаю контекст";
            }
            throw;
        }
    }

    public async Task<string> SpeechToTextAsync(StreamContent content)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Constants.OpenAiToken);
            using var formData = new MultipartFormDataContent
            {
                { content, "file", "file.mp3" },
                { new StringContent("whisper-1"), "model" },
                { new StringContent("text"), "response_format" },
            };
            var response = await httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", formData);
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<(string text, bool success)> AnalyzeImageAsync(string prompt, string[] urls, string detail = "low")
    {
        ImageData[] images = urls.Select(url =>
            new ImageData { 
                type = "image_url", 
                image_url = new ImageDataUrl { detail = detail, url = url } }).ToArray();

        var requestData = new 
        {
            model = "gpt-4-vision-preview",
            messages = new object[]
            {
                new 
                {
                    role = "user",
                    content = prompt
                },
                new
                {
                    role = "user",
                    content = images
                },
            },
            max_tokens = 600
        };
        string requestJson = JsonConvert.SerializeObject(requestData);

        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Constants.OpenAiToken);
            HttpResponseMessage response = await client.SendAsync(request);
            var str = await response.Content.ReadAsStringAsync();
            var gptResponse = JsonConvert.DeserializeObject<ChatCompletionResponse>(str);

            return gptResponse.error != null ? (gptResponse.error.message, false) : (gptResponse.choices.First().message.content, true);
        }
        catch (Exception e)
        {
            return (e.Message, false);
        }
    }

    public async Task<string[]> GenerateImageVariationAsync(StreamContent content, string model, int num)
    {
        try
        {
            // OpenAI_API doesn't support this API
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Constants.OpenAiToken);
            using var formData = new MultipartFormDataContent
            {
                { content, "image", "file.png" },
                { new StringContent(num.ToString()), "n" },
                { new StringContent("1024x1024"), "size" }
            };
            var response = await httpClient.PostAsync("https://api.openai.com/v1/images/variations", formData);
            if (response.IsSuccessStatusCode)
            {
                var files = JsonConvert.DeserializeObject<ImageVariationResponse>(await response.Content.ReadAsStringAsync());
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

    public async Task<string[]> GetAllModels()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Constants.OpenAiToken);
            var models = JsonConvert.DeserializeObject<ModelsResponse>(await httpClient.GetStringAsync("https://api.openai.com/v1/models"));
            return models.data.Select(item => item.id).Distinct().OrderBy(i => i).ToArray();
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
                { new StringContent("1024x1024"), "size" },
                { new StringContent("dall-e-3"), "model" },
            };
            var response = await httpClient.PostAsync("https://api.openai.com/v1/images/edits", formData);
            if (response.IsSuccessStatusCode)
            {
                var files = JsonConvert.DeserializeObject<ImageVariationResponse>(await response.Content.ReadAsStringAsync());
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

    public class Choice
    {
        public Message message { get; set; }
    }

    public class Message
    {
        public string content { get; set; }
    }

    public class ChatCompletionResponse
    {
        public List<Choice> choices { get; set; }

        public Error error { get; set; }
    }

    public class ImageVariationData
    {
        public string url { get; set; }
        public string id { get; set; }
    }

    public class ImageVariationResponse
    {
        public List<ImageVariationData> data { get; set; }
    }

    public enum Orientation
    {
        Landscape,
        Portrait,
        Square
    }

    public class ModelsResponse
    {
        public List<ImageVariationData> data { get; set; }
    }
}

public class ImageDataUrl
{
    public string detail { get; set; }
    public string url { get; set; }
}

public class ImageData
{
    public string type { get; set; }
    public ImageDataUrl image_url { get; set; }
}

public class HttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        return client;
    }
}