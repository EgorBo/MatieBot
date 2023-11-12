using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using File = System.IO.File;

public static class TelegramExtensions
{
    public static Task ReplyAsync(this ITelegramBotClient client, Message msg, string text, bool parse = true)
    {
        return client.SendTextMessageAsync(chatId: msg.Chat, 
            replyToMessageId: msg.MessageId, parseMode: parse ? ParseMode.Markdown : null, text: text);
    }

    public static async Task ReplyWithImageAsync(this ITelegramBotClient client, Message msg, string url, string caption = "")
    {
        var tmp = Path.GetTempFileName() + ".jpg";
        await DownloadFileTaskAsync(new HttpClient(), new Uri(url), tmp);

        await client.SendPhotoAsync(chatId: msg.Chat, replyToMessageId: msg.MessageId, caption: caption,
            photo: (InputFile.FromStream(File.OpenRead(tmp), Path.GetFileName(tmp))));
    }

    public static async Task ReplyWithImagesAsync(this ITelegramBotClient client, Message msg, List<string> urls)
    {
        List<string> tmps = new();
        foreach (var url in urls)
        {
            var tmp = Path.GetTempFileName() + ".jpg";
            await DownloadFileTaskAsync(new HttpClient(), new Uri(url), tmp);
            tmps.Add(tmp);
        }

        var media = tmps.Select(t => 
            new InputMediaPhoto(InputFile.FromStream(File.OpenRead(t), Path.GetFileName(t)))).ToArray();
        await client.SendMediaGroupAsync(chatId: msg.Chat, replyToMessageId: msg.MessageId, media: media);
    }
    
    private static async Task DownloadFileTaskAsync(this HttpClient client, Uri uri, string file)
    {
        await using var s = await client.GetStreamAsync(uri);
        await using var fs = new FileStream(file, FileMode.CreateNew);
        await s.CopyToAsync(fs);
    }
}
