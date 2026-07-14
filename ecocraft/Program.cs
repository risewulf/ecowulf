using System.Globalization;
using ecocraft.Components;
using ecocraft.Extensions;
using ecocraft.Models;
using MudBlazor;
using MudBlazor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using ecocraft.Services;
using ecocraft.Services.DbServices;
using ecocraft.Services.ImportData;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization();

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"]
                             ?? Path.Combine(builder.Environment.ContentRootPath, ".aspnet-dp-keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services
    .AddDataProtection()
    .SetApplicationName("ecocraft")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

// var supportedCultures = new[] { "en", "en-US", "en-GB", "fr", "fr-FR", "es-ES", "es", "de", "de-DE" }; // Ajoutez les cultures que vous supportez
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
    options.SupportedUICultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddMudMarkdownServices();

builder.Services.AddDbContextFactory<EcoCraftDbContext>(options =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
            o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
        .EnableSensitiveDataLogging()
        .UseLoggerFactory(LoggerFactory.Create(bd =>
        {
            bd
                .AddConsole()
                .AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Error);
        }))
    );

builder.Services.AddControllers();

// DB Services
builder.Services.AddScoped<CraftingTableDbService>();
builder.Services.AddScoped<ElementDbService>();
builder.Services.AddScoped<ItemOrTagDbService>();
builder.Services.AddScoped<PluginModuleDbService>();
builder.Services.AddScoped<RecipeDbService>();
builder.Services.AddScoped<ServerDbService>();
builder.Services.AddScoped<SkillDbService>();
builder.Services.AddScoped<TalentDbService>();
builder.Services.AddScoped<DynamicValueDbService>();
builder.Services.AddScoped<ModifierDbService>();
builder.Services.AddScoped<UserCraftingTableDbService>();
builder.Services.AddScoped<UserDbService>();
builder.Services.AddScoped<UserElementDbService>();
builder.Services.AddScoped<UserPriceDbService>();
builder.Services.AddScoped<UserRecipeDbService>();
builder.Services.AddScoped<UserMarginDbService>();
builder.Services.AddScoped<UserSettingDbService>();
builder.Services.AddScoped<UserServerDbService>();
builder.Services.AddScoped<UserTalentDbService>();
builder.Services.AddScoped<UserSkillDbService>();
builder.Services.AddScoped<UserAutomationInputDbService>();
builder.Services.AddScoped<UserAutomationTargetDbService>();
builder.Services.AddScoped<DataContextDbService>();
builder.Services.AddScoped<ModUploadHistoryDbService>();

// Business Services
builder.Services.AddScoped<ContextService>();
builder.Services.AddScoped<ImportDataService>();
builder.Services.AddScoped<PriceCalculatorService>();
builder.Services.AddScoped<ServerDataService>();
builder.Services.AddScoped<ServerDataEditorService>();
builder.Services.AddScoped<UserServerDataService>();
builder.Services.AddScoped<CraftingTableFuelCostService>();
builder.Services.AddScoped<ShoppingListService>();
builder.Services.AddScoped<ShoppingListDataService>();
builder.Services.AddScoped<ShoppingListGraphService>();
builder.Services.AddScoped<EconomyViewerService>();
builder.Services.AddScoped<EconomyViewerDisplayService>();

// Util Services
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<LocalizationService>();

// Authorization
builder.Services.AddScoped<Authorization>();
builder.Services.AddAuthorization(config =>
{
    config.AddPolicy("IsServerAdmin", policy =>
        policy.Requirements.Add(new IsServerAdminRequirement()));
});

// Authentication Configuration
/*builder.Services.AddAuthentication(options =>
    {
        // Configure your authentication scheme here
        // For example, using cookies or JWT tokens
        options.DefaultAuthenticateScheme = "YourAuthenticationScheme"; // Remplace par ton schéma
        options.DefaultChallengeScheme = "YourAuthenticationScheme"; // Remplace par ton schéma
    })
    .AddYourAuthenticationScheme(); // Remplace par ta méthode d'authentification (ex. .AddCookie(), .AddJwtBearer(), etc.)*/

var app = builder.Build();

// --- Turnstile (Cloudflare) bot-gate config ---
// Activé uniquement si les deux clés sont fournies (via Turnstile__SiteKey / Turnstile__SecretKey).
var tsSiteKey = app.Configuration["Turnstile:SiteKey"];
var tsSecretKey = app.Configuration["Turnstile:SecretKey"];
var tsEnabled = !string.IsNullOrWhiteSpace(tsSiteKey) && !string.IsNullOrWhiteSpace(tsSecretKey);
var turnstileHttp = new HttpClient();

var locOptions = app.Services.GetService<IOptions<RequestLocalizationOptions>>();
app.UseRequestLocalization(locOptions!.Value);

await ApplyMigrationsWithRetryAsync(app.Services, app.Logger);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// --- Turnstile bot-gate : défi Cloudflare Turnstile à chaque nouvelle session ---
if (tsEnabled)
{
    const string gateCookie = "ew_ts";
    app.Use(async (context, next) =>
    {
        var reqPath = context.Request.Path.Value ?? "/";

        // Laisser passer les challenges ACME (renouvellement du certificat), au cas où.
        if (reqPath.StartsWith("/.well-known/", StringComparison.Ordinal))
        {
            await next();
            return;
        }

        // Endpoint de vérification du jeton (appelé par la page de défi)
        if (reqPath == "/_gate/verify" && HttpMethods.IsPost(context.Request.Method))
        {
            var form = await context.Request.ReadFormAsync();
            var token = form["cf-turnstile-response"].ToString();
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "";
            var ok = false;

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    using var vr = await turnstileHttp.PostAsync(
                        "https://challenges.cloudflare.com/turnstile/v0/siteverify",
                        new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["secret"] = tsSecretKey!,
                            ["response"] = token,
                            ["remoteip"] = ip
                        }));
                    var vbody = await vr.Content.ReadAsStringAsync();
                    ok = vbody.Contains("\"success\":true") || vbody.Contains("\"success\": true");
                }
                catch
                {
                    ok = false;
                }
            }

            if (ok)
            {
                context.Response.Cookies.Append(gateCookie, "1", new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true
                    // Pas d'expiration => cookie de session (redemandé à chaque nouvelle session).
                });
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }

            return;
        }

        // Session déjà vérifiée => on laisse passer.
        if (context.Request.Cookies.ContainsKey(gateCookie))
        {
            await next();
            return;
        }

        // Sinon, on sert la page de défi pour toute requête.
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(TurnstileGateHtml(tsSiteKey!));
    });
}

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;

    if (path != null && path.StartsWith("/assets/eco-icons/"))
    {
        var serverId = context.Request.Query.TryGetValue("serverId", out var sid) ? sid.ToString() : null;
        var filePathWithServer = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets",
            serverId ?? "no_found", path.Substring("/assets/eco-icons/".Length));
        var filePathWithServerAndFixupForTags = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets",
            serverId ?? "no_found", path.Substring("/assets/eco-icons/".Length).Replace(".png", "Item.png"));

        if (serverId is not null && File.Exists(filePathWithServer))
        {
            context.Request.Path = path.Replace("eco-icons", serverId);
        }
        else if (serverId is not null && File.Exists(filePathWithServerAndFixupForTags))
        {
            context.Request.Path = path.Replace("eco-icons", serverId).Replace(".png", "Item.png");
        }
        else
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "eco-icons",
                path.Substring("/assets/eco-icons/".Length));
            if (!File.Exists(filePath))
            {
                var filePathWithFixupTags = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets",
                    "eco-icons", path.Substring("/assets/eco-icons/".Length).Replace(".png", "Item.png"));
                if (File.Exists(filePathWithFixupTags))
                {
                    context.Request.Path = path.Replace("eco-icons", "mod-icons").Replace(".png", "Item.png");
                }
                else
                {
                    context.Request.Path = path.Replace("eco-icons", "mod-icons");
                }
            }
        }
    }

    await next();
});

app.UseStaticFiles();
app.MapControllers();
app.UseAntiforgery();

//app.UseAuthentication();.
//app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

StaticEnvironmentAccessor.WebHostEnvironment = app.Services.GetRequiredService<IWebHostEnvironment>();

app.Run();

static string TurnstileGateHtml(string siteKey)
{
    const string html = """
<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>EcoWulf — vérification</title>
<script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>
<style>
  :root { color-scheme: dark; }
  body { margin:0; min-height:100vh; display:flex; align-items:center; justify-content:center;
         font-family: system-ui, -apple-system, sans-serif; background:#14351b; color:#e8f5e9; }
  .card { background:#1b5e20; border:1px solid #2e7d32; border-radius:16px; padding:32px 28px;
          max-width:360px; width:90%; text-align:center; box-shadow:0 10px 30px rgba(0,0,0,.4); }
  .logo { width:72px; height:72px; margin:0 auto 12px; display:block; }
  h1 { margin:0 0 4px; font-size:1.5rem; }
  p { margin:0 0 20px; color:#a5d6a7; font-size:.95rem; }
  .cf { display:flex; justify-content:center; }
</style>
</head>
<body>
  <div class="card">
    <svg class="logo" viewBox="0 0 128 128" xmlns="http://www.w3.org/2000/svg" aria-label="EcoWulf">
      <rect x="2" y="2" width="124" height="124" rx="30" fill="#43a047"/>
      <polygon points="32,34 52,60 64,52 76,60 96,34 92,70 76,92 64,104 52,92 36,70" fill="#f1f8e9"/>
      <polygon points="46,66 57,64 52,73" fill="#1b5e20"/>
      <polygon points="82,66 71,64 76,73" fill="#1b5e20"/>
      <polygon points="57,88 71,88 64,103" fill="#1b5e20"/>
    </svg>
    <h1>EcoWulf</h1>
    <p>Petite vérification pour prouver que tu n'es pas un robot.</p>
    <div class="cf"><div class="cf-turnstile" data-sitekey="__SITEKEY__" data-callback="onOk" data-theme="dark"></div></div>
  </div>
  <script>
    function onOk(token){
      fetch('/_gate/verify', {
        method:'POST',
        headers:{'Content-Type':'application/x-www-form-urlencoded'},
        body:'cf-turnstile-response=' + encodeURIComponent(token)
      }).then(function(r){
        if (r.ok || r.status === 204) { location.reload(); }
        else { alert('Échec de la vérification, réessaie.'); if (window.turnstile) turnstile.reset(); }
      }).catch(function(){ alert('Erreur réseau, réessaie.'); });
    }
  </script>
</body>
</html>
""";
    return html.Replace("__SITEKEY__", siteKey);
}

static async Task ApplyMigrationsWithRetryAsync(IServiceProvider services, ILogger logger)
{
    const int maxAttempts = 10;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EcoCraftDbContext>>();
            await using var dbContext = await factory.CreateDbContextAsync();

            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
            return;
        }
        catch (Exception ex) when (IsTransientDatabaseStartupFailure(ex) && attempt < maxAttempts)
        {
            var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 15));
            logger.LogWarning(ex,
                "Database is not ready yet while applying migrations (attempt {Attempt}/{MaxAttempts}). Retrying in {DelaySeconds}s.",
                attempt,
                maxAttempts,
                delay.TotalSeconds);

            await Task.Delay(delay);
        }
    }

    await using var finalScope = services.CreateAsyncScope();
    var finalFactory = finalScope.ServiceProvider.GetRequiredService<IDbContextFactory<EcoCraftDbContext>>();
    await using var finalContext = await finalFactory.CreateDbContextAsync();
    await finalContext.Database.MigrateAsync();
}

static bool IsTransientDatabaseStartupFailure(Exception exception)
{
    return exception switch
    {
        PostgresException { SqlState: "57P03" } => true,
        NpgsqlException { InnerException: not null } npgsqlException => IsTransientDatabaseStartupFailure(
            npgsqlException.InnerException),
        AggregateException aggregateException => aggregateException.InnerExceptions.Any(innerException =>
            IsTransientDatabaseStartupFailure(innerException)),
        _ => false,
    };
}
