using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class UserDb
{
    public Guid Id { get; set; }
    public long? ChatId { get; set; }
    public long TelegramId { get; set; }
    public string Username { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }

    public override string ToString()
    {
        var str = FirstName + " " + LastName;
        return str.Replace("\r", "").Replace("\n", "").Trim(' ');
    }
}

public class MessageDb
{
    public Guid Id { get; set; }
    public long? ChatId { get; set; }
    public long TelegramId { get; set; }
    public UserDb Author { get; set; }
    public DateTime Date { get; set; }
    public MessageType Type {get; set; }
    public bool IsGPT {get; set; }
}

public class BotDbContext : DbContext
{
    public DbSet<UserDb> Users { get; set; }
    public DbSet<MessageDb> Messages { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite("Data Source = " + Constants.Database);

    public void Initialize()
    {
        Database.EnsureCreated();
    }
}

public class BotState
{
    public void RecordMessage(Message msg, bool isGpt)
    {
        if (msg?.From == null || msg.Type != MessageType.Text)
            return;

        using var ctx = new BotDbContext();
        var author = ctx.Users.FirstOrDefault(u => u.TelegramId == msg.From.Id);
        if (author == null)
        {
            author = new UserDb
            {
                Id = Guid.NewGuid(),
                TelegramId = msg.From.Id,
                ChatId = msg.Chat?.Id,
                FirstName = msg.From.FirstName,
                LastName = msg.From.LastName,
            };
            ctx.Users.Add(author);
        }
        ctx.Messages.Add(
            new MessageDb
            {
                Id = Guid.NewGuid(),
                Author = author,
                ChatId = msg.Chat?.Id,
                TelegramId = msg.MessageId,
                IsGPT = isGpt,
                Type = msg.Type,
                Date = DateTime.UtcNow,
            });
        ctx.SaveChanges();
    }

    public bool CheckGPTCap(int limit)
    {
        using var ctx = new BotDbContext();
        int count = ctx.Messages
            .Count(m => m.Date > DateTime.UtcNow.AddHours(-24) && m.IsGPT == true);
        return count < limit;
    }

    public string DayStats(ChatId chatId)
    {
        if (chatId?.Identifier == null)
            return "?";

        using var ctx = new BotDbContext();
        var stats = ctx.Messages
            .Where(m => m.Date > DateTime.UtcNow.AddHours(-24) && m.Author != null && m.ChatId == chatId.Identifier.Value)
            .GroupBy(g => g.Author)
            .AsEnumerable() // SQLite ¯\_(ツ)_/¯
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select((g, index) => $"{index + 1}) {g.Key} - {g.Count()}")
            .ToArray();
        return $"Стата по флудерам за 24 часа:\n\n{string.Join("\n", stats)}";
    }

    public string GlobalStats(ChatId chatId)
    {
        if (chatId?.Identifier == null)
            return "?";

        using var ctx = new BotDbContext();
        var stats = ctx.Messages
            .Where(m => m.Author != null && m.ChatId == chatId.Identifier.Value)
            .GroupBy(g => g.Author)
            .AsEnumerable() // SQLite ¯\_(ツ)_/¯
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select((g, index) => $"{index + 1}) {g.Key} - {g.Count()}")
            .ToArray();
        return $"Стата по флудерам за всё время:\n\n{string.Join("\n", stats)}";
    }

    public string UserStats(ChatId chatId)
    {
        if (chatId?.Identifier == null)
            return "?";

        using var ctx = new BotDbContext();
        return $"У меня в базе {ctx.Users.Count(u => u.ChatId == chatId.Identifier.Value)} юзеров";
    }
}