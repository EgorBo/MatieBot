﻿using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using File = System.IO.File;

public static class TelegramExtensions
{
    private static async Task WithRetry(Func<Task> taskFunc)
    {
        try
        {
            await taskFunc();
        }
        catch
        {
            // single retry
            await Task.Delay(Random.Shared.Next(1000, 2000));
            await taskFunc();
        }
    }
    
    public static async Task ReplyAsync(this ITelegramBotClient client, Message msg, string text, bool parse = true)
    {
        if (string.IsNullOrEmpty(text))
            return;

        await WithRetry(async () =>
        {
            try
            {
                await client.SendTextMessageAsync(chatId: msg.Chat,
                    replyToMessageId: msg.MessageId, parseMode: parse ? ParseMode.Markdown : null, text: text);
            }
            catch
            {
                if (parse)
                {
                    // try again without parse (it fails time to time)
                    await ReplyAsync(client, msg, text, false);
                    return;
                }
                throw;
            }
        });
    }
    
    public static async Task ReplyWithImageAsync(this ITelegramBotClient client, Message msg, string url, string caption = "")
    {
        await WithRetry(async () =>
        {
            try
            {
                var tmp = Path.GetTempFileName() + ".jpg";
                await DownloadFileTaskAsync(new Uri(url), tmp);
                await client.SendPhotoAsync(chatId: msg.Chat, replyToMessageId: msg.MessageId, caption: caption,
                    photo: (InputFile.FromStream(File.OpenRead(tmp), Path.GetFileName(tmp))));
                //File.Delete(tmp); // TODO: close streams correctly first
            }
            catch
            {
                throw new InvalidOperationException($"ReplyWithImageAsync failed for url='{url}', caption={caption}");
            }
        });
    }

    public static async Task ReplyWithImagesAsync(this ITelegramBotClient client, Message msg, List<string> urls)
    {
        await WithRetry(async () =>
        {
            List<string> tmps = new();
            foreach (var url in urls)
            {
                var tmp = Path.GetTempFileName() + ".jpg";
                await DownloadFileTaskAsync(new Uri(url), tmp);
                tmps.Add(tmp);
            }

            var media = tmps.Select((tmp, index) =>
                new InputMediaPhoto(InputFile.FromStream(File.OpenRead(tmp), $"photo{index}.jpg"))).ToArray();
            await client.SendMediaGroupAsync(chatId: msg.Chat, replyToMessageId: msg.MessageId, media: media);
            //tmps.ForEach(File.Delete); // TODO: close streams correctly first
        });
    }
    
    private static async Task DownloadFileTaskAsync(Uri uri, string file)
    {
        using HttpClient client = new();
        await using var s = await client.GetStreamAsync(uri);
        await using var fs = new FileStream(file, FileMode.CreateNew);
        await s.CopyToAsync(fs);
    }

    public static async Task<string[]> UploadPhotosToAzureAsync(this ITelegramBotClient client, Message msg)
    {
        List<string> links = new();
        foreach (var group in msg.Photo!.GroupBy(p => p.FileId))
        {
            var photo = group.OrderByDescending(p => p.Width).First();
            if (photo.Width <= 100)
                continue;
            links.Add(await UploadPhotoToAzureAsync(client, photo));
        }
        return links.ToArray();
    }

    public static async Task<string> UploadPhotoToAzureAsync(this ITelegramBotClient client, PhotoSize photo)
    {
        var fileInfo = await client.GetFileAsync(photo.FileId);
        string tmpLocalFile = Path.GetTempFileName() + ".jpg";
        await using (var jpgStream = new FileStream(tmpLocalFile, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            await client.DownloadFileAsync(fileInfo.FilePath!, jpgStream);

        // Upload to Azure Blob Storage
        var blobServiceClient = new BlobServiceClient(Constants.AzureBlobCS);
        string containerName = "telega";
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        string blobName = Guid.NewGuid() + ".jpg";
        var blobClient = containerClient.GetBlobClient(blobName);
        await using (FileStream uploadFileStream = File.OpenRead(tmpLocalFile))
            await blobClient.UploadAsync(uploadFileStream, true);

        await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = "image/jpg" });
        File.Delete(tmpLocalFile);
        return blobClient.Uri.AbsoluteUri;
    }
}
