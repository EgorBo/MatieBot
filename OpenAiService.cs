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
        _openAi = new OpenAIAPI(Constants.OpenAiToken);
        _systemMsg = systemMessage;
        NewContext(_systemMsg);
    }

    public void NewContext(string context)
    {
        _conversation = _openAi.Chat.CreateConversation();
        _conversation.Model = Model.ChatGPTTurbo;
        _conversation.AppendSystemMessage(context.Trim());
    }

    public async Task<(string, bool)> SendUserInputAsync(string prompt)
    {
        try
        {
            _conversation.AppendUserInput(prompt);
            return (await _conversation.GetResponseFromChatbot(), false);
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
                _conversation.AppendUserInput(prompt.Trim());
                return (await _conversation.GetResponseFromChatbot(), true);
            }
            throw;
        }
    }
}