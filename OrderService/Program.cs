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

// Early debugging - this should appear in Azure logs immediately
Console.WriteLine("=== OrderService Container Started ===");
Console.WriteLine($"Current Time: {DateTime.UtcNow}");
Console.WriteLine($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}");

try
{
    Console.WriteLine("Creating WebApplication builder...");
    var builder = WebApplication.CreateBuilder(args);

    Console.WriteLine("Checking environment variables...");
    var connString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
                    ?? builder.Configuration.GetConnectionString("DefaultConnection");
    Console.WriteLine($"Connection String: {connString}");
    Console.WriteLine($"Connection String exists: {!string.IsNullOrEmpty(connString)}");
    
    var jwtSecret = Environment.GetEnvironmentVariable("JwtSettings__SecretKey");
    Console.WriteLine($"JWT Secret exists: {!string.IsNullOrEmpty(jwtSecret)}");

    // Validate required configuration
    if (string.IsNullOrEmpty(connString))
    {
        Console.WriteLine("ERROR: Database connection string is missing!");
        throw new InvalidOperationException("Database connection string is required but not provided.");
    }

    Console.WriteLine("Configuring Serilog...");
    // Configure Serilog
    builder.Host.UseSerilog((context, config) =>
    {
        config
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console();
    });

    Console.WriteLine("Configuring Kestrel...");
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.ListenAnyIP(8081);
    });

    builder.Services.AddEndpointsApiExplorer();

    Console.WriteLine("Configuring JWT Settings...");
    // Configure JWT Settings
    var jwtSettings = new JwtSettings();
    builder.Configuration.GetSection(JwtSettings.SectionName).Bind(jwtSettings);
    
    // Override with environment variables if present
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
    
    // Check if JWT secret is empty
    if (string.IsNullOrEmpty(jwtSettings.SecretKey))
    {
        Console.WriteLine("ERROR: JWT SecretKey is empty!");
        throw new InvalidOperationException("JWT SecretKey is required but not provided.");
    }
    
    builder.Services.AddSingleton(jwtSettings);

    Console.WriteLine("Configuring JWT Authentication...");
    // Configure JWT Authentication
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

        // Configure JWT for SignalR
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

    // Configure Authorization
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

    // Register services
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

        // Add JWT Authentication to Swagger
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

        // Include XML comments
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        try
        {
            c.IncludeXmlComments(xmlPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not include XML comments: {ex.Message}");
        }
    });

    Console.WriteLine("Configuring Database...");
    builder.Services.AddDbContext<OrderDbContext>(options =>
        options.UseSqlServer(connString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(30);
        }));

    Console.WriteLine("Configuring RabbitMQ...");
    var rabbitMqHost = builder.Configuration["RabbitMQ:Host"] ?? Environment.GetEnvironmentVariable("RabbitMQ__Host") ?? "rabbitmq";
    var rabbitMqUsername = builder.Configuration["RabbitMQ:Username"] ?? Environment.GetEnvironmentVariable("RabbitMQ__Username") ?? "guest";
    var rabbitMqPassword = builder.Configuration["RabbitMQ:Password"] ?? Environment.GetEnvironmentVariable("RabbitMQ__Password") ?? "guest";

    Console.WriteLine($"RabbitMQ Config - Host: {rabbitMqHost}, Username: {rabbitMqUsername}");

    try
    {
        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<OrderService.Consumers.OrderPlacedEventConsumer>();
            x.UsingRabbitMq((context, cfg) =>
            {
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
        });
        Console.WriteLine("MassTransit configured successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error configuring MassTransit: {ex.Message}");
        throw;
    }

    builder.Services.AddHttpClient();
    builder.Services.AddControllers();
    builder.Services.AddSignalR();

    Console.WriteLine("Configuring OpenTelemetry...");
    // Configure OpenTelemetry
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

    Console.WriteLine("Building application...");
    var app = builder.Build();

    Console.WriteLine("Application built successfully!");

    // Only attempt database migration if not in development environment or if explicitly requested
    var skipMigration = Environment.GetEnvironmentVariable("SKIP_DB_MIGRATION")?.ToLower() == "true";
    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    
    if (!skipMigration)
    {
        try
        {
            Console.WriteLine("Starting database migration...");
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            
            Console.WriteLine("Testing database connection...");
            
            var maxRetries = 5;
            var delay = TimeSpan.FromSeconds(5);
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Console.WriteLine($"Database connection attempt {attempt}/{maxRetries}...");
                    
                    if (await db.Database.CanConnectAsync())
                    {
                        Console.WriteLine("Database connection successful!");
                        
                        // Check for pending migrations
                        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
                        Console.WriteLine($"Pending migrations count: {pendingMigrations.Count()}");
                        
                        foreach (var migration in pendingMigrations)
                        {
                            Console.WriteLine($"Pending migration: {migration}");
                        }
                        
                        Console.WriteLine("Applying migrations...");
                        await db.Database.MigrateAsync();
                        Console.WriteLine("Database migrations completed successfully!");
                        
                        // Verify the migrations were applied
                        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
                        Console.WriteLine($"Total applied migrations: {appliedMigrations.Count()}");
                        
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"Cannot connect to database on attempt {attempt}");
                        if (attempt == maxRetries)
                        {
                            throw new InvalidOperationException($"Database connection failed after {maxRetries} attempts");
                        }
                    }
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    Console.WriteLine($"Database connection attempt {attempt} failed: {ex.Message}");
                    Console.WriteLine($"Retrying in {delay.TotalSeconds} seconds...");
                    await Task.Delay(delay);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database migration failed: {ex.Message}");
            Console.WriteLine($"Inner exception: {ex.InnerException?.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            
            if (environment?.ToLower() == "production")
            {
                Console.WriteLine("WARNING: Running without database in production mode!");
            }
            else
            {
                throw;
            }
        }
    }
    else
    {
        Console.WriteLine("Database migration skipped due to SKIP_DB_MIGRATION=true");
    }

    Console.WriteLine("Configuring HTTP request pipeline...");
    // Configure the HTTP request pipeline.
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Service API v1");
        c.DocumentTitle = "Order Service API Documentation";
        c.DefaultModelsExpandDepth(-1);
        c.DisplayRequestDuration();
    });

    // Add Authentication & Authorization middleware
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<OrderTrackingHub>("/order-tracking-hub");
    app.MapGet("/", () => "OrderService is running 🚀");
    app.MapGet("/health", () => Results.Ok("Healthy"));

    // Expose Prometheus metrics endpoint
    app.UseOpenTelemetryPrometheusScrapingEndpoint();

    Console.WriteLine("Starting OrderService application...");
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"FATAL ERROR in OrderService: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    throw;
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
