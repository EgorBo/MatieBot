using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class GUser
{
    public Guid Id { get; set; }
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

public class GMessage
{
    public Guid Id { get; set; }
    public long TelegramId { get; set; }
    public GUser Author { get; set; }
    public DateTime Date { get; set; }
}

public class BotDbContext : DbContext
{
    public DbSet<GUser> Users { get; set; }
    public DbSet<GMessage> Messages { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite("Data Source = matie.db");

    public void Initialize()
    {
        Database.EnsureCreated();
    }
}

public class BotState
{
    private readonly BotDbContext _dbContext;

    public BotState()
    {
        _dbContext = new BotDbContext();
        _dbContext.Initialize();
    }

    public void RecordMessage(Message msg)
    {
        if (msg?.From == null || msg.Type != MessageType.Text)
            return;

        var author = _dbContext.Users.FirstOrDefault(u => u.TelegramId == msg.From.Id);
        if (author == null)
        {
            author = new GUser
            {
                Id = Guid.NewGuid(),
                TelegramId = msg.From.Id,
                FirstName = msg.From.FirstName,
                LastName = msg.From.LastName,
            };
            _dbContext.Users.Add(author);
        }
        _dbContext.Messages.Add(
            new GMessage
            {
                Id = Guid.NewGuid(),
                Author = author,
                TelegramId = msg.MessageId,
                Date = DateTime.UtcNow,
            });
        _dbContext.SaveChanges();
    }

    public string DayStats()
    {
        var stats = _dbContext.Messages
            .Where(m => m.Date > DateTime.UtcNow.AddHours(-24) && m.Author != null)
            .GroupBy(g => g.Author)
            .AsEnumerable() // SQLite ¯\_(ツ)_/¯
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select((g, index) => $"{index + 1}) {g.Key} - {g.Count()}")
            .ToArray();
        return $"Стата по флудерам за 24 часа:\n\n{string.Join("\n", stats)}";
    }

    public string GlobalStats()
    {
        var stats = _dbContext.Messages
            .Where(m => m.Author != null)
            .GroupBy(g => g.Author)
            .AsEnumerable() // SQLite ¯\_(ツ)_/¯
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select((g, index) => $"{index + 1}) {g.Key} - {g.Count()}")
            .ToArray();
        return $"Стата по флудерам за всё время:\n\n{string.Join("\n", stats)}";
    }

    public GUser GetRandomUser()
    {
        // ¯\_(ツ)_/¯
        return _dbContext.Users.ToArray().MinBy(u => Guid.NewGuid());
    }

    public string UserStats()
    {
        return $"У меня в базе {_dbContext.Users.Count()} юзеров";
    }
}