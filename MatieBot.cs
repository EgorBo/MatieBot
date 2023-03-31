using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static Constants;

public class MatieBot
{
    private OpenAiService _openAi;
    private TelegramBotClient _botClient;
    private User _botUser;
    private BotState _botState; // serializeable state
    private Timer _timerDayStats;
    private Timer _timerLofd;
    private GUser _loser;

    public async Task StartListeningAsync(CancellationTokenSource ctx)
    {
        _timerDayStats = new Timer(OnDayStatsTick, null, TimeSpan.FromHours(24), TimeSpan.FromHours(24));
        _timerLofd = new Timer(OnLoserOfTheDay, null, TimeSpan.FromHours(1), TimeSpan.FromHours(24));
        _botState = new BotState();
        _openAi = new OpenAiService();
        _openAi.Init(ChatGptSystemMessage);
        _botClient = new TelegramBotClient(TelegramToken);
        _botUser = await _botClient.GetMeAsync(cancellationToken: ctx.Token);
        Console.WriteLine($"Started as {_botUser.Username}");
        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: new ReceiverOptions { AllowedUpdates = new[] { UpdateType.Message } },
            cancellationToken: ctx.Token
        );
    }

    private async void OnLoserOfTheDay(object _)
    {
        _loser = _botState.GetRandomUser();
        if (_loser != null)
        {
            await _botClient.SendTextMessageAsync(chatId: GoldChatId,
                text: $"Новый {LoserOfTheDay} дня: {_loser.FirstName} {_loser.LastName} 🤩🤩");
        }
    }

    private async void OnDayStatsTick(object _)
    {
        await _botClient.SendTextMessageAsync(chatId: GoldChatId, text: _botState.DayStats());
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is not { Text: { } messageText } message)
                return;

            // BotAdmin - just redirect msg to the golden chat (for fun)
            if (BotAdmins.Contains(message.Chat))
            {
                await botClient.SendTextMessageAsync(chatId: GoldChatId,
                    text: messageText, cancellationToken: ct);
                return;
            }

            // Unauthorized use
            if (message.Chat != GoldChatId)
            {
                await botClient.SendTextMessageAsync(chatId: message.Chat,
                    text: UnathorizedAccessMessage, cancellationToken: ct);
                return;
            }

            _botState.RecordMessage(message);

            if (messageText.StartsWith("!stats", StringComparison.InvariantCultureIgnoreCase))
            {
                await botClient.SendTextMessageAsync(chatId: message.Chat,
                    text: _botState.DayStats(), cancellationToken: ct);
            }
            else if (messageText.StartsWith("!globalstats", StringComparison.InvariantCultureIgnoreCase))
            {
                await botClient.SendTextMessageAsync(chatId: message.Chat,
                    text: _botState.GlobalStats(), cancellationToken: ct);
            }
            if (messageText.StartsWith("!users", StringComparison.InvariantCultureIgnoreCase))
            {
                await botClient.SendTextMessageAsync(chatId: message.Chat,
                    text: _botState.UserStats(), cancellationToken: ct);
            }
            if (messageText.StartsWith("!context", StringComparison.InvariantCultureIgnoreCase))
            {
                _openAi.NewContext(messageText.Remove(0, "!context".Length));
                var gptResponse = await _openAi.SendUserInputAsync("понял?");
                await botClient.SendTextMessageAsync(chatId: message.Chat,
                    replyToMessageId: update?.Message?.MessageId,
                    text: gptResponse, cancellationToken: ct);
            }
            else if (messageText.StartsWith("!ping", StringComparison.InvariantCultureIgnoreCase))
            {
                await botClient.SendTextMessageAsync(chatId: message.Chat,
                    replyToMessageId: update?.Message?.MessageId,
                    text: "pong", cancellationToken: ct);
            }
            else if (messageText.Contains($"{LoserOfTheDay} дня", StringComparison.InvariantCultureIgnoreCase))
            {
                _loser ??= _botState.GetRandomUser();
                await botClient.SendTextMessageAsync(chatId: message.Chat,
                    replyToMessageId: update?.Message?.MessageId,
                    text: $"Главный {LoserOfTheDay} дня - {_loser.FirstName} {_loser.LastName} 🤩🤩", cancellationToken: ct);
            }
            else if (messageText.StartsWith(BotName, StringComparison.InvariantCultureIgnoreCase))
            {
                var gptResponse = await _openAi.SendUserInputAsync(messageText);
                await botClient.SendTextMessageAsync(chatId: message.Chat,
                    replyToMessageId: update?.Message?.MessageId,
                    text: gptResponse, cancellationToken: ct);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            try
            {
                //I hope it's not the Telegram that crashed
                await botClient.SendTextMessageAsync(
                    chatId: BotAdmins[0],
                    replyToMessageId: update?.Message?.MessageId,
                    text: "Error:\n\n" + e.Message,
                    cancellationToken: ct);
            }
            catch
            {
                // Ignore 2nd chance exception
            }
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMsg = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };
        Console.WriteLine(errorMsg);
        return Task.CompletedTask;
    }
}