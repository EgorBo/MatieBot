using Telegram.Bot.Types;

public static class Constants
{
    public const string OpenAiToken = "";
    public const string TelegramToken = "";
    public const string BotName = "Матье";
    public const string ChatGptSystemMessage = $"Тебя зовут {BotName}, ты отвечаешь на запросы в групповом чате";
    public const string UnathorizedAccessMessage = "Бот работает только в коричневом чате";
    public const string LoserOfTheDay = "неудачник";
    public static ChatId GoldChatId = new(-1001534302177);
    public static ChatId[] BotAdmins =
    {
        new (912083) // EgorBo
    };
}