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
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Конфигурация
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger с JWT поддержкой
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Введите 'Bearer' [пробел] и ваш JWT токен"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

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

// CORS с большим количеством origin
builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientPermission", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .WithOrigins(
                  "http://localhost:3000", "https://localhost:3000",
                  "http://localhost:5500", "https://localhost:5500",
                  "http://localhost:8080", "https://localhost:8080",
                  "http://localhost:4200", "https://localhost:4200",
                  "http://localhost:5000", "https://localhost:5000",
                  "http://127.0.0.1:5500", "http://127.0.0.1:3000",
                  "http://127.0.0.1:8080",
                  "http://localhost", "http://localhost:*"
              )
              .AllowCredentials();
    });
});

// Сервисы
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

// ВАЖНО: UseCors должен быть ДО UseAuthentication и UseAuthorization
app.UseCors("ClientPermission");

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

        // Создание чата между ними
        if (testUser != null && testUser2 != null)
        {
            try
            {
                Console.WriteLine($"🔄 Создание чата между {testUser.Email} и {testUser2.Email}...");
                var chat = await chatService.GetOrCreatePrivateChatAsync(testUser.Id, testUser2.Id);
                Console.WriteLine($"✅ Создан чат ID: {chat.Id}");

                // Тестовое сообщение
                try
                {
                    var message = await chatService.SendMessageAsync(testUser.Id, new SendMessageDto
                    {
                        ChatId = chat.Id,
                        Content = "Привет! Это тестовое сообщение",
                        MessageType = "Text"
                    });
                    Console.WriteLine($"✅ Отправлено тестовое сообщение: ID {message.Id}");

                    // Тестовое уведомление
                    await notificationService.CreateNotificationAsync(testUser2.Id, new CreateNotificationDto
                    {
                        Title = "Добро пожаловать!",
                        Message = "Вы успешно зарегистрировались в чате",
                        NotificationType = "system"
                    });

                }
                catch (Exception msgEx)
                {
                    Console.WriteLine($"⚠️ Не удалось отправить тестовое сообщение: {msgEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Не удалось создать чат: {ex.Message}");
            }
        }

        // Тестовый пользователь 3
        var testUser3 = await userManager.FindByEmailAsync("demo@example.com");
        if (testUser3 == null)
        {
            testUser3 = new User
            {
                Email = "demo@example.com",
                UserName = "demo@example.com",
                DisplayName = "Демо Пользователь",
                EmailConfirmed = true
            };
            var result3 = await userManager.CreateAsync(testUser3, "demo123");
            if (result3.Succeeded)
            {
                Console.WriteLine($"✅ Создан демо пользователь: demo@example.com / demo123");

                var notificationSettings3 = new NotificationSettings
                {
                    UserId = testUser3.Id,
                    EnableNotifications = true,
                    EnableSound = true,
                    ShowBanner = true,
                    SmartNotifications = true
                };
                dbContext.NotificationSettings.Add(notificationSettings3);
                await dbContext.SaveChangesAsync();
            }
        }

    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Ошибка при создании тестовых данных: {ex.Message}");
        Console.WriteLine($"StackTrace: {ex.StackTrace}");
    }
}

app.Run();