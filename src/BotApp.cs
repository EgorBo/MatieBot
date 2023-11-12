using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static Constants;

Console.WriteLine("Starting bot...");
AppDomain.CurrentDomain.UnhandledException += (_, _) => { };
var cts = new CancellationTokenSource();
await new BotApp().StartListeningAsync(cts);
await Task.Delay(-1);

public class BotApp
{
    public OpenAiService OpenAi { get; private set; }
    public TelegramBotClient TgClient { get; private set; }
    public User BotUser { get; private set; }
    public Database BotDb { get; private set; }
    public CancellationTokenSource Cts { get; private set; }
    public DateTime StartDate { get; } = DateTime.UtcNow;

    public async Task StartListeningAsync(CancellationTokenSource cts)
    {
        Cts = cts;
        BotDb = new Database();
        OpenAi = new OpenAiService();
        TgClient = new TelegramBotClient(TelegramToken);
        BotUser = await TgClient.GetMeAsync(cancellationToken: cts.Token);
        Console.WriteLine($"Started as {BotUser.Username}");
        TgClient.StartReceiving(
            updateHandler: (c, u, ct) =>
            {
                // Don't block updateHandler
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                ThreadPool.QueueUserWorkItem(_ => HandleUpdateAsync(c, u, ct));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                return Task.CompletedTask;
            },
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: new ReceiverOptions { AllowedUpdates = new[] { UpdateType.Message } },
            cancellationToken: cts.Token
        );
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is not { Text: { } msgText } message)
                return;

            Command processedCommand = null;
            bool handled = false;
            foreach (var command in BotCommands.AllCommands)
            {
                bool triggered = false;
                switch (command.Trigger)
                {
                    case CommandTrigger.StartsWith:
                    {
                        if (msgText.StartsWith(command.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            msgText = msgText.Substring(command.Name.Length).Trim(' ', ',', '\r', '\n', '\t');
                            triggered = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(command.AltName) &&
                             msgText.StartsWith(command.AltName, StringComparison.OrdinalIgnoreCase))
                        {
                            msgText = msgText.Substring(command.AltName.Length).Trim(' ', ',', '\r', '\n', '\t');
                                triggered = true;
                        }
                        break;
                    }
                    case CommandTrigger.Contains:
                    {
                        if (msgText.Contains(command.Name, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(command.AltName) && 
                             msgText.Contains(command.AltName, StringComparison.OrdinalIgnoreCase)))
                        {
                            triggered = true;
                        }
                        break;
                    }
                    default:
                        throw new NotImplementedException();
                }

                if (!triggered)
                    continue;

                if (command.AllowedChats.Any() && !command.AllowedChats.Contains(message.Chat) && 
                    (message.From == null || !command.AllowedChats.Contains(message.From.Id)))
                {
                    await botClient.SendTextMessageAsync(chatId: message.Chat,
                        replyToMessageId: update.Message.MessageId,
                        text: "вы кто такие? я вас не знаю. Access denied.");
                    return;
                }
                
                if (command.CommandType == CommandType.GPT_Vision && !BotDb.CheckGptCapPerUser(message?.From?.Id ?? 0))
                {
                    await botClient.SendTextMessageAsync(chatId: message.Chat,
                        replyToMessageId: update.Message.MessageId,
                        text: $"Харэ, не больше {GptCapPerDay} запросов в Dall-3 на рыло за 24 часа.");
                    return;
                }
                
                if (command.CommandType != CommandType.None && !BotDb.CheckGptCap(GptCapPerDay))
                {
                    await botClient.SendTextMessageAsync(chatId: message.Chat,
                        replyToMessageId: update.Message.MessageId,
                        text: $"Харэ, не больше {GptCapPerDay} запросов в ChatGPT за 24 часа.");
                    return;
                }

                await command.Action(message, msgText, this);
                handled = true;
                processedCommand = command;
                break;
            }

            BotDb.RecordMessage(message, processedCommand?.CommandType ?? CommandType.None);

            // BotAdmin - just redirect msg to the golden chat (for fun)
            if (!handled && BotAdmins.Contains(message.Chat))
            {
                await botClient.SendTextMessageAsync(chatId: GoldChatId,
                    text: msgText, cancellationToken: ct);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            try
            {
                foreach (var botAdmin in BotAdmins)
                {
                    await botClient.SendTextMessageAsync(chatId: botAdmin, text: e.ToString(), cancellationToken: ct);
                }
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
