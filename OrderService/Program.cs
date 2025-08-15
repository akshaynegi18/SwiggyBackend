using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using MassTransit;
using OrderService.Hubs;
using Serilog;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using OrderService.Configuration;
using OrderService.Services;
using StackExchange.Redis;
using OrderService.Events;

try
{
    var builder = WebApplication.CreateBuilder(args);

    var connString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
                    ?? builder.Configuration.GetConnectionString("DefaultConnection");
    
    var jwtSecret = Environment.GetEnvironmentVariable("JwtSettings__SecretKey");

    if (string.IsNullOrEmpty(connString))
    {
        throw new InvalidOperationException("Database connection string is required but not provided.");
    }

    builder.Host.UseSerilog((context, config) =>
    {
        config
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console();
    });

    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.ListenAnyIP(8081);
    });

    builder.Services.AddEndpointsApiExplorer();

    var jwtSettings = new JwtSettings();
    builder.Configuration.GetSection(JwtSettings.SectionName).Bind(jwtSettings);
    
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JwtSettings__SecretKey")))
    {
        jwtSettings.SecretKey = Environment.GetEnvironmentVariable("JwtSettings__SecretKey");
    }
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JwtSettings__Issuer")))
    {
        jwtSettings.Issuer = Environment.GetEnvironmentVariable("JwtSettings__Issuer");
    }
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JwtSettings__Audience")))
    {
        jwtSettings.Audience = Environment.GetEnvironmentVariable("JwtSettings__Audience");
    }
    
    if (string.IsNullOrEmpty(jwtSettings.SecretKey))
    {
        throw new InvalidOperationException("JWT SecretKey is required but not provided.");
    }
    
    builder.Services.AddSingleton(jwtSettings);

    var redisEnabled = builder.Configuration.GetValue<bool>("Redis:EnableCaching", true);
    var redisConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Redis") 
                              ?? Environment.GetEnvironmentVariable("Redis__ConnectionString")
                              ?? builder.Configuration.GetConnectionString("Redis") 
                              ?? builder.Configuration["Redis:ConnectionString"];
    
    if (redisEnabled && !string.IsNullOrEmpty(redisConnectionString))
    {
        try
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Attempting to connect to Redis at: {ConnectionString}", redisConnectionString);
                
                var configuration = ConfigurationOptions.Parse(redisConnectionString);
                configuration.AbortOnConnectFail = false;
                configuration.ConnectRetry = 3;
                configuration.ConnectTimeout = 5000;
                configuration.SyncTimeout = 5000;
                
                var multiplexer = ConnectionMultiplexer.Connect(configuration);
                
                var database = multiplexer.GetDatabase();
                database.StringSet("test_connection", "OK", TimeSpan.FromSeconds(10));
                var testResult = database.StringGet("test_connection");
                
                if (testResult == "OK")
                {
                    logger.LogInformation("Redis connection successful!");
                    database.KeyDelete("test_connection");
                }
                else
                {
                    logger.LogWarning("Redis connection test failed");
                }
                
                return multiplexer;
            });
            
            builder.Services.AddScoped<IRedisCacheService, RedisCacheService>();
        }
        catch (Exception ex)
        {
            builder.Services.AddScoped<IRedisCacheService, NoOpCacheService>();
        }
    }
    else
    {
        builder.Services.AddScoped<IRedisCacheService, NoOpCacheService>();
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
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/order-tracking-hub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("CustomerOnly", policy => 
            policy.RequireRole("Customer"));
        
        options.AddPolicy("DeliveryPartnerOnly", policy => 
            policy.RequireRole("DeliveryPartner"));
        
        options.AddPolicy("AdminOnly", policy => 
            policy.RequireRole("Admin"));
        
        options.AddPolicy("CustomerOrAdmin", policy => 
            policy.RequireRole("Customer", "Admin"));
    });

    builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();


//builder.Services.AddHostedService<DeliveryPartnerSimulator>();

    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Order Service API",
            Version = "v1",
            Description = "A comprehensive API for managing food delivery orders with JWT authentication",
            Contact = new Microsoft.OpenApi.Models.OpenApiContact
            {
                Name = "Development Team",
                Email = "dev@fooddelivery.com"
            }
        });

        c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...\"",
            Name = "Authorization",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        try
        {
            c.IncludeXmlComments(xmlPath);
        }
        catch (Exception ex)
        {
        }
    });

    builder.Services.AddDbContext<OrderDbContext>(options =>
        options.UseSqlServer(connString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(30);
        }));

    var messageBrokerProvider = builder.Configuration["MessageBroker:Provider"] 
                              ?? Environment.GetEnvironmentVariable("MessageBroker__Provider") 
                              ?? "RabbitMQ";

    try
    {
        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<OrderService.Consumers.OrderPlacedEventConsumer>();
            
            if (messageBrokerProvider.Equals("AzureServiceBus", StringComparison.OrdinalIgnoreCase))
            {
                x.UsingAzureServiceBus((context, cfg) =>
                {
                    var connectionString = Environment.GetEnvironmentVariable("AzureServiceBus__ConnectionString") 
                                         ?? builder.Configuration.GetConnectionString("AzureServiceBus")
                                         ?? builder.Configuration["AzureServiceBus:ConnectionString"];
                    
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("Azure Service Bus connection string is required but not provided.");
                    }
                    
                    cfg.Host(connectionString);
                    
                    cfg.Message<OrderPlacedEvent>(x => x.SetEntityName("order-events"));
                cfg.SubscriptionEndpoint<OrderPlacedEvent>("order-service-subscription", e =>
                    {
                        e.ConfigureConsumer<OrderService.Consumers.OrderPlacedEventConsumer>(context);
                    });
                });
            }
            else
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    var rabbitMqHost = builder.Configuration["RabbitMQ:Host"] 
                                     ?? Environment.GetEnvironmentVariable("RabbitMQ__Host") 
                                     ?? "rabbitmq";
                    var rabbitMqUsername = builder.Configuration["RabbitMQ:Username"] 
                                         ?? Environment.GetEnvironmentVariable("RabbitMQ__Username") 
                                         ?? "guest";
                    var rabbitMqPassword = builder.Configuration["RabbitMQ:Password"] 
                                         ?? Environment.GetEnvironmentVariable("RabbitMQ__Password") 
                                         ?? "guest";

                    cfg.Host(rabbitMqHost, "/", h =>
                    {
                        h.Username(rabbitMqUsername);
                        h.Password(rabbitMqPassword);
                    });
                    
                    cfg.ReceiveEndpoint("order-placed-queue", e =>
                    {
                        e.ConfigureConsumer<OrderService.Consumers.OrderPlacedEventConsumer>(context);
                    });
                });
            }
        });
    }
    catch (Exception ex)
    {
        throw;
    }

    builder.Services.AddHttpClient();
    
    builder.Services.AddHttpClient("UserService", client =>
    {
        var userServiceBaseUrl = Environment.GetEnvironmentVariable("UserService__BaseUrl") 
                               ?? builder.Configuration["UserService:BaseUrl"] 
                               ?? "http://localhost:8080";
        
        client.BaseAddress = new Uri(userServiceBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        
        client.DefaultRequestHeaders.Add("User-Agent", "OrderService/1.0");
    });
    
    builder.Services.AddControllers();
    builder.Services.AddSignalR();

    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("OrderService"))
                .AddAspNetCoreInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddConsoleExporter();
        })
        .WithMetrics(metrics =>
        {
            metrics
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("OrderService"))
                .AddAspNetCoreInstrumentation()
                .AddPrometheusExporter();
        });

    var app = builder.Build();

    var skipMigration = Environment.GetEnvironmentVariable("SKIP_DB_MIGRATION")?.ToLower() == "true";
    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    
    if (!skipMigration)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            
            var maxRetries = 5;
            var delay = TimeSpan.FromSeconds(5);
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (await db.Database.CanConnectAsync())
                    {
                        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
                        
                        await db.Database.MigrateAsync();
                        
                        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
                        
                        break;
                    }
                    else
                    {
                        if (attempt == maxRetries)
                        {
                            throw new InvalidOperationException($"Database connection failed after {maxRetries} attempts");
                        }
                    }
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    await Task.Delay(delay);
                }
            }
        }
        catch (Exception ex)
        {
            if (environment?.ToLower() != "production")
            {
                throw;
            }
        }
    }

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Service API v1");
        c.DocumentTitle = "Order Service API Documentation";
        c.DefaultModelsExpandDepth(-1);
        c.DisplayRequestDuration();
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<OrderTrackingHub>("/order-tracking-hub");
    app.MapGet("/", () => "OrderService is running 🚀");
    app.MapGet("/health", () => Results.Ok("Healthy"));

    app.UseOpenTelemetryPrometheusScrapingEndpoint();

    app.Run();
}
catch (Exception ex)
{
    throw;
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
