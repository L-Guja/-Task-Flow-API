using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();

var app = builder.Build();

app.MapPost("/tasks", async ([FromBody] TaskCreateDto dto, [FromServices] ITaskService service) =>
{
    return await service.CreateInitialTask(dto);
});

app.MapGet("/tasks/{userId}", async (int userId, [FromServices] ITaskService service) =>
{
    return await service.GetTasksForUser(userId);
});

app.MapPut("/tasks/{taskId}/complete", async (int taskId, [FromBody] TaskCompleteDto dto, [FromServices] ITaskService service) =>
{
    return await service.CompleteTask(taskId, dto.Comment);
});

app.MapGet("/notifications/{userId}", async (int userId, [FromServices] INotificationService service) =>
{
    return await service.GetNotifications(userId);
});


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    if (!db.Users.Any())
    {
        db.Users.AddRange(new[] {
            new User { Id = 1, Name = "CEO", Role = Role.Owner },
            new User { Id = 2, Name = "Director", Role = Role.Director },
            new User { Id = 3, Name = "Manager", Role = Role.Manager },
            new User { Id = 4, Name = "Supervisor", Role = Role.Supervisor },
            new User { Id = 5, Name = "Employee", Role = Role.Employee },
        });
        db.SaveChanges();
    }
}

app.Run();


public interface ITaskService
{
    Task<IResult> CreateInitialTask(TaskCreateDto dto);
    Task<IResult> GetTasksForUser(int userId);
    Task<IResult> CompleteTask(int taskId, string comment);
}

public interface IUserRepository
{
    Task<User?> GetUserByIdAsync(int userId);
    Task<User?> GetUserByRoleAsync(Role role);
}

public interface ITaskRepository
{
    Task<TaskItem?> GetTaskByIdAsync(int taskId);
    Task<List<TaskItem>> GetTasksForUserAsync(int userId);
    Task AddTaskAsync(TaskItem task);
    Task SaveChangesAsync();
}

public interface INotificationService
{
    Task<IResult> GetNotifications(int userId);
    Task NotifyAsync(int userId, string message);
}


public enum Role { Owner, Director, Manager, Supervisor, Employee }
public enum TaskStatus { Pending, InProgress, Completed }

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public Role Role { get; set; }
}

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? Comment { get; set; }
    public int CreatedByUserId { get; set; }
    public int AssignedToUserId { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Message { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


public class TaskCreateDto
{
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public int CreatedByUserId { get; set; }
}

public class TaskCompleteDto
{
    public string Comment { get; set; } = null!;
}


public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
}


public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;

    public Task<User?> GetUserByIdAsync(int userId) => _db.Users.FindAsync(userId).AsTask();
    public Task<User?> GetUserByRoleAsync(Role role) => _db.Users.FirstOrDefaultAsync(u => u.Role == role);
}

public class TaskRepository : ITaskRepository
{
    private readonly AppDbContext _db;
    public TaskRepository(AppDbContext db) => _db = db;

    public Task<TaskItem?> GetTaskByIdAsync(int taskId) => _db.Tasks.FindAsync(taskId).AsTask();
    public Task<List<TaskItem>> GetTasksForUserAsync(int userId) => _db.Tasks.Where(t => t.AssignedToUserId == userId && t.Status != TaskStatus.Completed).ToListAsync();
    public Task AddTaskAsync(TaskItem task)
    {
        _db.Tasks.Add(task);
        return Task.CompletedTask;
    }
    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}


public class TaskService : ITaskService
{
    private readonly IUserRepository _users;
    private readonly ITaskRepository _tasks;
    private readonly INotificationService _notify;

    public TaskService(IUserRepository users, ITaskRepository tasks, INotificationService notify)
    {
        _users = users;
        _tasks = tasks;
        _notify = notify;
    }

    private static readonly Dictionary<Role, Role?> RoleFlow = new()
    {
        { Role.Owner, Role.Director },
        { Role.Director, Role.Manager },
        { Role.Manager, Role.Supervisor },
        { Role.Supervisor, Role.Employee },
        { Role.Employee, null }
    };

    public async Task<IResult> CreateInitialTask(TaskCreateDto dto)
    {
        var creator = await _users.GetUserByIdAsync(dto.CreatedByUserId);
        if (creator == null || creator.Role != Role.Owner)
            return Results.BadRequest("Only Owner can create tasks.");

        if (!RoleFlow.TryGetValue(creator.Role, out var nextRole) || nextRole is null)
            return Results.BadRequest("Next role not defined.");

        var nextUser = await _users.GetUserByRoleAsync(nextRole.Value);
        if (nextUser == null)
            return Results.BadRequest("Next role user not found.");

        var task = new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            CreatedByUserId = creator.Id,
            AssignedToUserId = nextUser.Id
        };

        await _tasks.AddTaskAsync(task);
        await _tasks.SaveChangesAsync();
        return Results.Ok(task);
    }

    public async Task<IResult> GetTasksForUser(int userId)
    {
        var tasks = await _tasks.GetTasksForUserAsync(userId);
        return Results.Ok(tasks);
    }

    public async Task<IResult> CompleteTask(int taskId, string comment)
    {
        var task = await _tasks.GetTaskByIdAsync(taskId);
        if (task == null) return Results.NotFound();

        task.Status = TaskStatus.Completed;
        task.Comment = comment;
        task.CompletedAt = DateTime.UtcNow;

        var currentUser = await _users.GetUserByIdAsync(task.AssignedToUserId);
        if (currentUser == null) return Results.BadRequest("User not found.");

        var nextRole = RoleFlow[currentUser.Role];
        if (nextRole is Role role)
        {
            var nextUser = await _users.GetUserByRoleAsync(role);
            if (nextUser != null)
            {
                var newTask = new TaskItem
                {
                    Title = task.Title,
                    Description = task.Description,
                    CreatedByUserId = task.CreatedByUserId,
                    AssignedToUserId = nextUser.Id
                };
                await _tasks.AddTaskAsync(newTask);
            }
        }
        else
        {
            await _notify.NotifyAsync(task.CreatedByUserId, $"Task '{task.Title}' fully completed by {currentUser.Name}");
        }

        await _tasks.SaveChangesAsync();
        return Results.Ok(task);
    }
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    public NotificationService(AppDbContext db) => _db = db;

    public async Task<IResult> GetNotifications(int userId)
    {
        var notes = await _db.Notifications.Where(n => n.UserId == userId).ToListAsync();
        return Results.Ok(notes);
    }

    public async Task NotifyAsync(int userId, string message)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Message = message
        });
        await _db.SaveChangesAsync();
    }
}