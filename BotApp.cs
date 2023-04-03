using GoldChatBot;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static Constants;

var cts = new CancellationTokenSource();
await new BotApp().StartListeningAsync(cts);
Console.WriteLine("Listening... Press any key to stop the bot.");
Console.ReadKey();
cts.Cancel();

public class BotApp
{
    private OpenAiService _openAi;
    private TelegramBotClient _botClient;
    private User _botUser;
    private BotState _botState;

    public async Task StartListeningAsync(CancellationTokenSource ctx)
    {
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

                if (command.AllowedChats != null && !command.AllowedChats.Contains(message.Chat))
                {
                    await botClient.SendTextMessageAsync(chatId: message.Chat,
                        replyToMessageId: update.Message.MessageId,
                        text: "вы кто такие? я вас не знаю. Access denied.");
                }
                else if (command.NeedsOpenAi && !_botState.CheckGPTCap(50))
                {
                    await botClient.SendTextMessageAsync(chatId: message.Chat,
                        replyToMessageId: update.Message.MessageId,
                        text: "Харэ, не больше 50 запросов в ChatGPT за 24 часа.");
                }
                else
                {
                    await command.Action(message, msgText, _botClient, _botState, command.NeedsOpenAi ? _openAi : null);
                }
                requestOpenAi = command.NeedsOpenAi;
                handled = true;
                break;
            }
            _botState.RecordMessage(message, requestOpenAi);

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
