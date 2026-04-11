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
using OrderService.Saga;
using OrderService.Saga.Consumers;
using MassTransit.EntityFrameworkCoreIntegration;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using OrderService.Resilience;
using Polly;

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

        // Add Idempotency-Key header to Swagger UI for [IdempotentRequest] endpoints
        c.OperationFilter<OrderService.Idempotency.IdempotencyHeaderOperationFilter>();

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        try
        {
            c.IncludeXmlComments(xmlPath);
        }
        catch (Exception)
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
            // Existing logging consumer
            x.AddConsumer<OrderService.Consumers.OrderPlacedEventConsumer>();

            // Saga service consumers
            x.AddConsumer<ProcessPaymentConsumer>();
            x.AddConsumer<ConfirmRestaurantConsumer>();
            x.AddConsumer<AssignDeliveryPartnerConsumer>();
            x.AddConsumer<RefundPaymentConsumer>();
            x.AddConsumer<OrderFulfillmentStatusConsumer>();

            // Order Fulfillment Saga
            x.AddSagaStateMachine<OrderFulfillmentSaga, OrderSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                    r.LockStatementProvider = new SqlServerLockStatementProvider();
                    r.ExistingDbContext<OrderDbContext>();
                });

            // ── Transactional Outbox ─────────────────────────────────────────
            // Consumers: outbox filter wraps each consumer in a DB transaction so
            //            Publish/Send and SaveChangesAsync commit atomically.
            // Bus outbox: replaces IPublishEndpoint / ISendEndpointProvider from DI
            //             so controllers & background services also write to the
            //             outbox table. A hosted delivery service polls the table
            //             and forwards messages to the transport.
            x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
            {
                o.UseSqlServer();

                // Enable outbox for DI-resolved IPublishEndpoint (used in controllers)
                o.UseBusOutbox();

                // How often the delivery service polls the outbox table
                o.QueryDelay = TimeSpan.FromSeconds(1);

                // Window for inbox-based duplicate detection
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(5);
            });

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

                    cfg.UseServiceBusMessageScheduler();

                    cfg.Message<OrderPlacedEvent>(x => x.SetEntityName("order-events"));

                    // Existing logging consumer
                    cfg.SubscriptionEndpoint<OrderPlacedEvent>("order-service-subscription", e =>
                    {
                        e.ConfigureConsumer<OrderService.Consumers.OrderPlacedEventConsumer>(context);
                    });

                    // Saga endpoint — subscribes to OrderPlacedEvent + all response events
                    cfg.ReceiveEndpoint("order-fulfillment-saga", e =>
                        e.ConfigureSaga<OrderSagaState>(context));

                    // Command endpoints (saga sends commands to these queues)
                    cfg.ReceiveEndpoint("process-payment", e =>
                        e.ConfigureConsumer<ProcessPaymentConsumer>(context));
                    cfg.ReceiveEndpoint("confirm-restaurant", e =>
                        e.ConfigureConsumer<ConfirmRestaurantConsumer>(context));
                    cfg.ReceiveEndpoint("assign-delivery-partner", e =>
                        e.ConfigureConsumer<AssignDeliveryPartnerConsumer>(context));
                    cfg.ReceiveEndpoint("refund-payment", e =>
                        e.ConfigureConsumer<RefundPaymentConsumer>(context));
                    cfg.ReceiveEndpoint("order-fulfillment-status", e =>
                        e.ConfigureConsumer<OrderFulfillmentStatusConsumer>(context));
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

                    cfg.UseDelayedMessageScheduler();

                    // Existing logging consumer
                    cfg.ReceiveEndpoint("order-placed-queue", e =>
                    {
                        e.ConfigureConsumer<OrderService.Consumers.OrderPlacedEventConsumer>(context);
                    });

                    // Saga endpoint — subscribes to OrderPlacedEvent + all response events
                    cfg.ReceiveEndpoint("order-fulfillment-saga", e =>
                    {
                        e.ConfigureSaga<OrderSagaState>(context);
                    });

                    // Command endpoints (saga sends commands to these queues)
                    cfg.ReceiveEndpoint("process-payment",e =>
                        e.ConfigureConsumer<ProcessPaymentConsumer>(context));
                    cfg.ReceiveEndpoint("confirm-restaurant",e =>
                        e.ConfigureConsumer<ConfirmRestaurantConsumer>(context));
                    cfg.ReceiveEndpoint("assign-delivery-partner",e=>
                        e.ConfigureConsumer<AssignDeliveryPartnerConsumer>(context));
                    cfg.ReceiveEndpoint("refund-payment", e =>
                        e.ConfigureConsumer<RefundPaymentConsumer>(context));
                    cfg.ReceiveEndpoint("order-fulfillment-status", e =>
                        e.ConfigureConsumer<OrderFulfillmentStatusConsumer>(context));
                });
            }
        });
    }
    catch (Exception)
    {
        throw;
    }

    builder.Services.AddHttpClient();

    // ── Resilient HttpClient for UserService (Retry + Circuit Breaker + Timeout) ──
    builder.Services.AddHttpClient<UserServiceClient>(client =>
    {
        var userServiceBaseUrl = Environment.GetEnvironmentVariable("UserService__BaseUrl")
                               ?? builder.Configuration["UserService:BaseUrl"]
                               ?? "http://localhost:8080";

        client.BaseAddress = new Uri(userServiceBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "OrderService/1.0");
    })
    .AddResilienceHandler("user-service", pipeline =>
    {
        // 1) Retry — exponential backoff with jitter on transient failures
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
                .HandleResult(r => r.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
                                || r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        });

        // 2) Circuit Breaker — open after 50 % failure rate over 30 s, stay open 30 s
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
                .HandleResult(r => (int)r.StatusCode >= 500)
        });

        // 3) Timeout — 10 s per attempt (inner timeout, applies per retry attempt)
        pipeline.AddTimeout(TimeSpan.FromSeconds(10));
    });

    // ── Rate Limiting ──
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString();
            }

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = "Too many requests",
                message = "Rate limit exceeded. Please try again later.",
                retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
                    ? (int)ra.TotalSeconds
                    : (int?)null
            }, cancellationToken);
        };

        // Per-user sliding window for order placement — 5 orders / min
        // (Business rule: gateway handles IP-based infra protection)
        options.AddPolicy("order-placement", context =>
        {
            var userId = context.User.FindFirst("userId")?.Value
                      ?? context.Connection.RemoteIpAddress?.ToString()
                      ?? "unknown";

            return RateLimitPartition.GetSlidingWindowLimiter(userId,
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 2
                });
        });
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

    if (!skipMigration)
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                logger.LogInformation("Starting database migration check...");

                // Step 1: Check if the Orders table already exists in the database
                bool tablesAlreadyExist = false;
                try
                {
                    var conn = db.Database.GetDbConnection();
                    if (conn.State != System.Data.ConnectionState.Open)
                        await conn.OpenAsync();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(N'Orders', N'U') IS NOT NULL THEN 1 ELSE 0 END";
                    var result = await cmd.ExecuteScalarAsync();
                    tablesAlreadyExist = result != null && Convert.ToInt32(result) == 1;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not check if tables already exist");
                }

                // Step 2: If tables exist, ALWAYS run idempotent schema repair to add any missing columns.
                // This runs regardless of migration history state.
                if (tablesAlreadyExist)
                {
                    logger.LogInformation("Orders table exists - running idempotent schema repair...");

                    var schemaRepairStatements = new[]
                    {
                        "IF COL_LENGTH('Orders', 'DeliveryLatitude') IS NULL ALTER TABLE [Orders] ADD [DeliveryLatitude] float NULL;",
                        "IF COL_LENGTH('Orders', 'DeliveryLongitude') IS NULL ALTER TABLE [Orders] ADD [DeliveryLongitude] float NULL;",
                        "IF COL_LENGTH('Orders', 'ETA') IS NULL ALTER TABLE [Orders] ADD [ETA] int NULL;",
                        "IF COL_LENGTH('Orders', 'DestinationLatitude') IS NULL ALTER TABLE [Orders] ADD [DestinationLatitude] float NULL;",
                        "IF COL_LENGTH('Orders', 'DestinationLongitude') IS NULL ALTER TABLE [Orders] ADD [DestinationLongitude] float NULL;",
                        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OrderHistories')
                          CREATE TABLE [OrderHistories] (
                              [Id] int NOT NULL IDENTITY(1,1),
                              [OrderId] int NOT NULL,
                              [Status] nvarchar(max) NOT NULL,
                              [DeliveryLatitude] float NULL,
                              [DeliveryLongitude] float NULL,
                              [Timestamp] datetime2 NOT NULL,
                              CONSTRAINT [PK_OrderHistories] PRIMARY KEY ([Id]),
                              CONSTRAINT [FK_OrderHistories_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders]([Id]) ON DELETE CASCADE
                          );",
                        "IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_OrderHistories_OrderId') CREATE INDEX [IX_OrderHistories_OrderId] ON [OrderHistories]([OrderId]);",
                        "IF COL_LENGTH('OrderHistories', 'DeliveryLatitude') IS NULL ALTER TABLE [OrderHistories] ADD [DeliveryLatitude] float NULL;",
                        "IF COL_LENGTH('OrderHistories', 'DeliveryLongitude') IS NULL ALTER TABLE [OrderHistories] ADD [DeliveryLongitude] float NULL;",
                    };

                    foreach (var sql in schemaRepairStatements)
                    {
                        db.Database.ExecuteSqlRaw(sql);
                    }

                    logger.LogInformation("Schema repair completed.");
                }

                // Step 3: Handle EF Core migration history
                var pendingMigrations = db.Database.GetPendingMigrations().ToList();
                var appliedMigrations = db.Database.GetAppliedMigrations().ToList();

                logger.LogInformation("Migration status - Applied: {AppliedCount}, Pending: {PendingCount}",
                    appliedMigrations.Count, pendingMigrations.Count);

                if (pendingMigrations.Any())
                {
                    if (tablesAlreadyExist)
                    {
                        // Tables exist but migrations aren't recorded - sync the history
                        logger.LogWarning("Tables exist but {Count} migration(s) not recorded: {Migrations}",
                            pendingMigrations.Count, string.Join(", ", pendingMigrations));

                        var efVersion = typeof(DbContext).Assembly.GetName().Version!;
                        var productVersion = $"{efVersion.Major}.{efVersion.Minor}.{efVersion.Build}";

                        db.Database.ExecuteSqlRaw(
                            @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '__EFMigrationsHistory')
                              CREATE TABLE [__EFMigrationsHistory] (
                                  [MigrationId] nvarchar(150) NOT NULL,
                                  [ProductVersion] nvarchar(32) NOT NULL,
                                  CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                              )");

                        foreach (var migration in pendingMigrations)
                        {
                            db.Database.ExecuteSqlRaw(
                                "IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = {0}) " +
                                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ({0}, {1})",
                                migration, productVersion);
                            logger.LogInformation("Marked migration '{Migration}' as applied (version: {Version})", migration, productVersion);
                        }

                        logger.LogInformation("Migration history synchronized.");
                    }
                    else
                    {
                        // Fresh database - run migrations normally
                        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                            pendingMigrations.Count, string.Join(", ", pendingMigrations));

                        db.Database.Migrate();
                        logger.LogInformation("Database migrations applied successfully.");
                    }
                }
                else
                {
                    logger.LogInformation("Database is up to date - no pending migrations.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database migration failed: {Error}", ex.Message);
                var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (environment?.ToLower() != "production")
                {
                    throw;
                }
                else
                {
                    logger.LogWarning("Continuing startup in production mode despite migration error");
                }
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
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<OrderTrackingHub>("/order-tracking-hub");
    app.MapGet("/", () => "OrderService is running 🚀");
    app.MapGet("/health", () => Results.Ok("Healthy"));

    app.UseOpenTelemetryPrometheusScrapingEndpoint();

    app.Run();
}
catch (Exception)
{
    throw;
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}