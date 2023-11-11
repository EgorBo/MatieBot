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
    public int Dalle3Cap { get; set; }

    public override string ToString()
    {
        var str = FirstName + " " + LastName;
        return str.Replace("\r", "").Replace("\n", "").Trim(' ');
    }
}

public enum CommandType
{
    None = 0,
    GPT_Drawing,
    GPT_Vision,
    GPT_Audio,
    GTP_Text
}

public class MessageDb
{
    public Guid Id { get; set; }
    public long? ChatId { get; set; }
    public long TelegramId { get; set; }
    public UserDb Author { get; set; }
    public DateTime Date { get; set; }
    public MessageType MessageType { get; set; }
    public string MessageText { get; set; }
    public CommandType CommandType { get; set; }
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

public class Database
{
    public Database()
    {
        using var ctx = new BotDbContext();
        ctx.Initialize();
    }

    public void RecordMessage(Message msg, CommandType cmdType)
    {
        if (msg?.From == null || msg.Type != MessageType.Text)
            return;

        string msgText = msg.Text?.Trim('\r', '\n', '\t', ' ') ?? "";

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
                Username = msg.From.Username,
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
                MessageText = msgText,
                MessageType = msg.Type, 
                CommandType = cmdType,
                Date = DateTime.UtcNow,
            });
        ctx.SaveChanges();
    }

    public bool CheckGptCap(int limit)
    {
        using var ctx = new BotDbContext();
        int count = ctx.Messages
            .Count(m => m.Date > DateTime.UtcNow.AddHours(-24) && m.CommandType != CommandType.None);
        return count < limit;
    }

    public bool CheckGptCapPerUser(long id)
    {
        using var ctx = new BotDbContext();

        var user = ctx.Users.FirstOrDefault(u => u.TelegramId == id);
        if (user == null)
        {
            return false;
        }

        int count = ctx.Messages
            .Count(m => m.Date > DateTime.UtcNow.AddHours(-24) && 
                        (m.CommandType == CommandType.GPT_Drawing || m.CommandType == CommandType.GPT_Vision) && 
                        m.Author.TelegramId == id);
        return count < user.Dalle3Cap;
    }

    public bool SetDalle3Cap(string username, int newCap)
    {
        if (username?.StartsWith("@") == true)
            username = username.Substring(1);

        bool success = false;
        using var ctx = new BotDbContext();
        var users = ctx.Users.Where(u => u.Username == username).ToArray();
        foreach (var user in users)
        {
            success = true;
            user.Dalle3Cap = newCap;
        }

        if (success)
        {
            ctx.SaveChanges();
            return true;
        }
        return false;
    }

    public string GetLimits(string user)
    {
        using var ctx = new BotDbContext();
        user = user?.Trim(' ')?.TrimStart('@') ?? "";
        if (user.Length > 0)
        {
            var userObj = ctx.Users.FirstOrDefault(u => u.Username == user);
            if (userObj == null)
            {
                return "User not found";
            }

            int count24 = ctx.Messages.Count(m => 
                m.Date > DateTime.UtcNow.AddHours(-24) &&
                (m.CommandType == CommandType.GPT_Drawing || m.CommandType == CommandType.GPT_Vision) &&
                m.Author.TelegramId == userObj.TelegramId);

            int countAllTime = ctx.Messages.Count(m =>
                (m.CommandType == CommandType.GPT_Drawing || m.CommandType == CommandType.GPT_Vision) &&
                m.Author.TelegramId == userObj.TelegramId);

            int cap24 = userObj.Dalle3Cap;

            return $"Пользователь @{user} послал `{count24}` запроса(ов) в Dalle-3/Vision за 24 часа. Лимит: `{cap24}`. За всё время: `{countAllTime}`.";
        }
        return "User not found";
    }
}