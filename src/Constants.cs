using Telegram.Bot.Types;

public static class Constants
{
    public static readonly string OpenAiToken = Environment.GetEnvironmentVariable("MATIE_OAI_TOKEN");
    public static readonly string TelegramToken = Environment.GetEnvironmentVariable("MATIE_TG_TOKEN");
    public static readonly string AzureBlobCS = Environment.GetEnvironmentVariable("MATIE_AZURE_BLOB_CS");
    public const string BotName = "Матье";
    public const string AltBotName = "Matie";
    public const string ChatGptSystemMessage = $"Тебя зовут {BotName}, ты отвечаешь на запросы в групповом чате";
    public static readonly string Database = Environment.GetEnvironmentVariable("MATIE_DB_PATH") ?? @"C:\prj\matie.db";
    public const int GptCapPerDay = 400;
    public const int Dalle3CapPerUser = 20;
    public static ChatId GoldChatId = new(-1001534302177);
    public static ChatId[] BotAdmins =
        {
            new (912083) // EgorBo
        };
}