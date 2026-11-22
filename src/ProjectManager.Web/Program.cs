using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ProjectManager.Web;
using ProjectManager.Web.Api;
using ProjectManager.Web.Authentication;
using ProjectManager.Web.Components;
using ProjectManager.Web.Markdown;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOptions<ApiOptions>()
    .Bind(builder.Configuration.GetSection(ApiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<ApiHealthClient>(ConfigureApiClient);
builder.Services.AddHttpClient<ApiAccountClient>(ConfigureApiClient);
builder.Services.AddHttpClient<ApiInvitationClient>(ConfigureApiClient);

// Telas da Fase 09 (ADR-015): clientes por área sobre o GestalynApi, que leva o token do cookie da requisição.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IApiRequestContext, HttpApiRequestContext>();
builder.Services.AddHttpClient<GestalynApi>(ConfigureApiClient);
builder.Services.AddScoped<OrganizationsApi>();
builder.Services.AddScoped<MembershipCache>();
builder.Services.AddScoped<ProjectsApi>();
builder.Services.AddScoped<PlanningApi>();
builder.Services.AddScoped<TasksApi>();
builder.Services.AddScoped<WorkflowApi>();
builder.Services.AddScoped<DocumentsApi>();
builder.Services.AddScoped<AgentsApi>();
builder.Services.AddScoped<WorkReportsApi>();
builder.Services.AddSingleton<MarkdownRenderer>();
builder.Services.AddOptions<DisplayOptions>()
    .Bind(builder.Configuration.GetSection(DisplayOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<Display>();

// Sessão da Web (BFF): cookie HttpOnly com os tokens da API, renovados no servidor.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenRefreshCoordinator>();
builder.Services.AddScoped<TokenRefreshCookieEvents>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".Gestalyn.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.LoginPath = "/conta/entrar";
        options.AccessDeniedPath = "/conta/entrar";
        options.SlidingExpiration = false;
        options.EventsType = typeof(TokenRefreshCookieEvents);
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseWebSecurityHeaders();
app.UseNoStoreForAgentPages();
app.UseAuthentication();
app.UseAuthorization();
app.UseLastOrganizationCookie();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAccountEndpoints();
app.MapWebEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

static void ConfigureApiClient(IServiceProvider serviceProvider, HttpClient client)
{
    var options = serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value;
    client.BaseAddress = options.BaseUrl;
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

    // Origem das alterações na auditoria da API (ADR-014): informativa, não concede acesso.
    client.DefaultRequestHeaders.Add("X-Gestalyn-Client", "web");
}
