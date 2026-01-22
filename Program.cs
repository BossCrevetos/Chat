using Backend_chat.Data;
using Backend_chat.Hubs;
using Backend_chat.Models;
using Backend_chat.Services;
using Backend_chat.DTOs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Конфигурация
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// База данных
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// SignalR
builder.Services.AddSignalR();

// Identity с ослабленными требованиями к паролю для разработки
builder.Services.Configure<IdentityOptions>(options =>
{
    // Пароль: минимум 6 символов, только буквы и цифры
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.Password.RequiredUniqueChars = 1;

    // Настройки пользователя
    options.User.RequireUniqueEmail = true;
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
});

builder.Services.AddIdentity<User, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// JWT аутентификация
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey))
{
    jwtKey = Guid.NewGuid().ToString() + Guid.NewGuid().ToString();
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "BackendChat",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "BackendChatClient",
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey))
    };

    // Для SignalR - читаем токен из query string
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// CORS - ПРОСТАЯ НАСТРОЙКА БЕЗ Swagger
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Сервисы
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ChatService>();

var app = builder.Build();

// УБИРАЕМ Swagger и HTTPS для простоты
// app.UseSwagger();
// app.UseSwaggerUI();
// app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowAll");

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

// Создание базы данных при первом запуске
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    // Создание тестовых пользователей и чата
    try
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        // Тестовый пользователь 1
        var testUser = await userManager.FindByEmailAsync("test@example.com");
        if (testUser == null)
        {
            testUser = new User
            {
                Email = "test@example.com",
                UserName = "test@example.com",
                DisplayName = "Тестовый Пользователь",
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(testUser, "password123");
            if (result.Succeeded)
            {
                Console.WriteLine($"✅ Создан тестовый пользователь: test@example.com / password123");

                // Создаем настройки уведомлений
                var notificationSettings = new NotificationSettings
                {
                    UserId = testUser.Id,
                    EnableNotifications = true,
                    EnableSound = true,
                    ShowBanner = true,
                    SmartNotifications = true
                };
                dbContext.NotificationSettings.Add(notificationSettings);
                await dbContext.SaveChangesAsync();
            }
        }

        // Тестовый пользователь 2
        var testUser2 = await userManager.FindByEmailAsync("user2@example.com");
        if (testUser2 == null)
        {
            testUser2 = new User
            {
                Email = "user2@example.com",
                UserName = "user2@example.com",
                DisplayName = "Второй Пользователь",
                EmailConfirmed = true
            };
            var result2 = await userManager.CreateAsync(testUser2, "password456");
            if (result2.Succeeded)
            {
                Console.WriteLine($"✅ Создан второй пользователь: user2@example.com / password456");

                var notificationSettings2 = new NotificationSettings
                {
                    UserId = testUser2.Id,
                    EnableNotifications = true,
                    EnableSound = true,
                    ShowBanner = true,
                    SmartNotifications = true
                };
                dbContext.NotificationSettings.Add(notificationSettings2);
                await dbContext.SaveChangesAsync();
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Ошибка при создании тестовых данных: {ex.Message}");
    }
}

// Запуск на всех интерфейсах
app.Run("http://0.0.0.0:5086");