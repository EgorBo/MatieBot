using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class UserDb
{
    public Guid Id { get; set; }
    public long TelegramId { get; set; }
    public string Username { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Dalle3Cap { get; set; }
    public bool IsBot { get; set; }
    public bool? IsPremium { get; set; }

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
    public UserDb Author { get; set; }
    public DateTime Date { get; set; }
    public MessageType MessageType { get; set; }
    public string MessageText { get; set; }
    public CommandType CommandType { get; set; }
    public double EstimatedCost { get; set; }
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

    public void EnsureUserExists(User user)
    {
        using var ctx = new BotDbContext();
        var author = ctx.Users.FirstOrDefault(u => u.TelegramId == user.Id);
        if (author == null)
        {
            author = new UserDb
            {
                Id = Guid.NewGuid(),
                TelegramId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Dalle3Cap = Constants.Dalle3CapPerUser,
                Username = user.Username,
                IsBot = user.IsBot,
                IsPremium = user.IsPremium,
            };
            ctx.Users.Add(author);
        }
        else
        {
            // TODO: update info
        }
        ctx.SaveChanges();
    }

    public void RecordMessage(Message msg, CommandType cmdType, CommandResult cmdResult)
    {
        if (msg?.From == null)
            return;
        EnsureUserExists(msg.From);

        string msgText = msg.Text?.Trim('\r', '\n', '\t', ' ') ?? "";

        using var ctx = new BotDbContext();
        var author = ctx.Users.FirstOrDefault(u => u.TelegramId == msg.From.Id);
        ctx.Messages.Add(
            new MessageDb
            {
                Id = Guid.NewGuid(),
                Author = author,
                ChatId = msg.Chat?.Id,
                MessageText = msgText,
                MessageType = msg.Type, 
                CommandType = cmdType,
                EstimatedCost = cmdResult.EstimatedCost,
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

    public bool CheckGptCapPerUser(long id, out int limit)
    {
        using var ctx = new BotDbContext();

        var user = ctx.Users.FirstOrDefault(u => u.TelegramId == id);
        if (user == null)
        {
            limit = -1;
            return false;
        }

        int count = ctx.Messages
            .Count(m => m.Date > DateTime.UtcNow.AddHours(-24) && 
                        (m.CommandType == CommandType.GPT_Drawing) && 
                        m.Author.TelegramId == id);
        limit = user.Dalle3Cap;
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

    public bool SetDalle3Cap(int newCap)
    {
        bool success = false;
        using var ctx = new BotDbContext();
        var users = ctx.Users.ToArray();
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

    public string GetDalle3Stats()
    {
        using var ctx = new BotDbContext();

        var users = ctx.Messages
            .Where(m => m.CommandType == CommandType.GPT_Drawing)
            .GroupBy(m => m.Author)
            .Select(i => new { User = i.Key.Username, Count = i.Count() })
            .ToArray() // SQLite fails without this
            .OrderByDescending(i => i.Count)
            .Take(10)
            .ToArray();

        string result = "";
        for (var i = 0; i < users.Length; i++)
            result += $"{i+1}) `{users[i].User}` - {users[i].Count} запросов\n";
        return result;
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
                return $"Пользователь '{user}' не найден";
            }

            DateTime date = DateTime.UtcNow.AddHours(-24);
            int count24 = ctx.Messages.Count(m => 
                m.Date > date &&
                (m.CommandType == CommandType.GPT_Drawing) &&
                m.Author == userObj);

            int countAllTime = ctx.Messages.Count(m =>
                (m.CommandType == CommandType.GPT_Drawing) &&
                m.Author == userObj);

            int cap24 = userObj.Dalle3Cap;
            return $"Пользователь `{user}` послал `{count24}` запроса(ов) в Dalle-3/Vision за 24 часа. Лимит: `{cap24}`. За всё время: `{countAllTime}`.";
        }
        return $"Пользователь '{user}' не найден";
    }

    public async Task<string> ExecuteSql(string sql)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sql))
                return "huh?";
            using var ctx = new BotDbContext();
            string[] result = ctx.Database.SqlQueryRaw<string>(sql).ToArray();
            return string.Join("\n", result);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}