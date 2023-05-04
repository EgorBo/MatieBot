using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Models;
using OpenAI_API.Moderation;

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
        _conversation = _openAi.Chat.CreateConversation();
        _conversation.RequestParameters.Temperature = 0.9;
        _conversation.Model = new Model("gpt-4.0");
        _conversation.AppendSystemMessage(context.Trim());
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

    public async Task<string> SendUserInputAsync(string prompt)
    {
        try
        {
            _conversation.AppendUserInput(prompt);
            return await _conversation.GetResponseFromChatbot();
        }
        catch (HttpRequestException e)
        {
            // I'm too lazy to extract error codes
            if (e.Message.Contains("This model's maximum context length"))
            {
                // Spawn a new chat context and try again
                _conversation = _openAi.Chat.CreateConversation();
                _conversation.Model = Model.ChatGPTTurbo;
                _conversation.AppendSystemMessage(_systemMsg);
                return "Лимит по токенам, пересоздаю контекст";
            }
            throw;
        }
    }
}