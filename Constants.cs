using Telegram.Bot.Types;

public static class Constants
{
    public const string OpenAiToken = "";
    public const string TelegramToken = "";
    public const string BotName = "Матье";
    public const string AltBotName = "Matie";
    public const string ChatGptSystemMessage = $"Тебя зовут {BotName}, ты отвечаешь на запросы в групповом чате";
    public const string Database = @"C:\prj\matie.db";
    public static ChatId GoldChatId = new(-1001534302177);
    public static ChatId[] BotAdmins =
        {
            new (912083) // EgorBo
        };
}