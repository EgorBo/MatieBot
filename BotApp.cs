using System.Net.Http.Headers;
using GoldChatBot;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static Constants;


using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Constants.OpenAiToken);
var models = await httpClient.GetStringAsync("https://api.openai.com/v1/models");

AppDomain.CurrentDomain.UnhandledException += (_, _) => { };
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
    public List<string> CurrentCharView { get; set; } = new();

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
            updateHandler: (c, u, ct) =>
            {
                // Don't block updateHandler
                ThreadPool.QueueUserWorkItem(_ => HandleUpdateAsync(c, u, ct));
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
                else if (command.NeedsOpenAi && !State.CheckGPTCap(GptCapPerDay))
                {
                    await botClient.SendTextMessageAsync(chatId: message.Chat,
                        replyToMessageId: update.Message.MessageId,
                        text: $"Харэ, не больше {GptCapPerDay} запросов в ChatGPT за 24 часа.");
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

            // Just for fun - keep chat history in the global chat in a trimmable variable (we're not saving it to the DB)
            // We're going to keep it less than 8K tokens to be able to feed it to the GPT
            if (!handled && message.Type == MessageType.Text && message.Chat == GoldChatId && 
                message.From != null && !string.IsNullOrEmpty(message.From.Username))
            {
                string text = message.Text;
                if (message.ReplyToMessage != null && message.ReplyToMessage.From != null &&
                    !string.IsNullOrEmpty(message.ReplyToMessage.From.Username))
                {
                    // Who are you talking to?
                    text = $"{message.ReplyToMessage.From.Username}, " + text;
                }

                CurrentCharView.Add($"[{message.From.Username}]: {text}");
                if (CurrentCharView.Count > 10000)
                {
                    // To avoid memory leaks
                    CurrentCharView.RemoveRange(0, 1000);
                }
            }

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
