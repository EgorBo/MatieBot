using GoldChatBot;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static Constants;

AppDomain.CurrentDomain.UnhandledException += (_, _) => {};
var cts = new CancellationTokenSource();
await new BotApp().StartListeningAsync(cts);
await Task.Delay(-1);

public class BotApp
{
    public OpenAiService OpenAi { get; private set; }
    public TelegramBotClient TgClient { get; private set; }
    public User BotUser { get; private set; }
    public BotState State { get; private set; }
    public CancellationTokenSource Cts { get; private set; }
    public DateTime StartDate { get; } = DateTime.UtcNow;

    public async Task StartListeningAsync(CancellationTokenSource cts)
    {
        Cts = cts;
        State = new BotState();
        OpenAi = new OpenAiService();
        await OpenAi.InitAsync(ChatGptSystemMessage);
        TgClient = new TelegramBotClient(TelegramToken);
        BotUser = await TgClient.GetMeAsync(cancellationToken: cts.Token);
        Console.WriteLine($"Started as {BotUser.Username}");
        TgClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
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

            bool requestOpenAi = false;
            bool handled = false;
            foreach (var command in BotCommands.AllComands)
            {
                bool triggered = false;
                switch (command.Trigger)
                {
                    case CommandTrigger.StartsWith:
                    {
                        if (msgText.StartsWith(command.Name, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(command.AltName) && 
                             msgText.StartsWith(command.AltName, StringComparison.OrdinalIgnoreCase)))
                        {
                            msgText = msgText.Substring(command.Name.Length).Trim(' ', ',');
                            triggered = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(command.AltName) &&
                             msgText.StartsWith(command.AltName, StringComparison.OrdinalIgnoreCase))
                        {
                            msgText = msgText.Substring(command.AltName.Length).Trim(' ', ',');
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
                }
                else if (command.NeedsOpenAi && !State.CheckGPTCap(GptCaptPerDay))
                {
                    await botClient.SendTextMessageAsync(chatId: message.Chat,
                        replyToMessageId: update.Message.MessageId,
                        text: $"Харэ, не больше {GptCaptPerDay} запросов в ChatGPT за 24 часа.");
                }
                else
                {
                    await command.Action(message, msgText, this);
                }
                requestOpenAi = command.NeedsOpenAi;
                handled = true;
                break;
            }
            State.RecordMessage(message, requestOpenAi);

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
