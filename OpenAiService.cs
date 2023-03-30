using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Models;

public class OpenAiService
{
    private OpenAIAPI _openAi;
    private Conversation _conversation;
    private string _systemMsg;

    public void Init(string systemMessage)
    {
        _systemMsg = systemMessage;
        _openAi = new OpenAIAPI(Constants.OpenAiToken);
        _conversation = _openAi.Chat.CreateConversation();
        _conversation.Model = Model.ChatGPTTurbo;
        _conversation.AppendSystemMessage(_systemMsg);
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
                _conversation.AppendUserInput(prompt);
                return await _conversation.GetResponseFromChatbot();
            }
            throw;
        }
    }
}