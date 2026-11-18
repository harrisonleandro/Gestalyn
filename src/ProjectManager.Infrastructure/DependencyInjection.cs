using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ProjectManager.Application.Abstractions.Activity;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Application.Abstractions.Email;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Abstractions.Storage;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Dashboards;
using ProjectManager.Application.Documents;
using ProjectManager.Application.Organizations;
using ProjectManager.Application.Planning;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.Application.Workflows;
using ProjectManager.Infrastructure.Activity;
using ProjectManager.Infrastructure.Agents;
using ProjectManager.Infrastructure.Dashboards;
using ProjectManager.Infrastructure.Documents;
using ProjectManager.Infrastructure.Email;
using ProjectManager.Infrastructure.Identity;
using ProjectManager.Infrastructure.MultiTenancy;
using ProjectManager.Infrastructure.Organizations;
using ProjectManager.Infrastructure.Persistence;
using ProjectManager.Infrastructure.Planning;
using ProjectManager.Infrastructure.Projects;
using ProjectManager.Infrastructure.Storage;
using ProjectManager.Infrastructure.Tasks;
using ProjectManager.Infrastructure.Workflows;

namespace ProjectManager.Infrastructure;

/// <summary>
/// Ponto de composição da camada de infraestrutura.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Relógio do sistema injetável, substituível em testes por um TimeProvider controlado.
        services.TryAddSingleton(TimeProvider.System);

        // Organização ativa por requisição: vazia até a validação da associação do usuário (ADR-008).
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(serviceProvider => serviceProvider.GetRequiredService<TenantContext>());

        services.AddPersistence();
        services.AddIdentityServices();
        services.AddEmail();
        services.AddOrganizations();
        services.AddAgents();

        return services;
    }

    private static void AddOrganizations(this IServiceCollection services)
    {
        services.AddScoped<MembershipDirectory>();
        services.AddScoped<IMembershipDirectory>(serviceProvider => serviceProvider.GetRequiredService<MembershipDirectory>());
        services.AddScoped<OrganizationEmails>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IProjectAccess, ProjectAccessResolver>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IPlanningService, PlanningService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.TryAddScoped<IActivityContext, SystemActivityContext>();
        services.AddScoped<ActivityRecorder>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDecisionService, DecisionService>();
        services.AddScoped<IDashboardService, DashboardService>();

        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), $"Configure o diretório dos anexos em '{StorageOptions.SectionName}:RootPath'.")
            .ValidateOnStart();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
    }

    private static void AddAgents(this IServiceCollection services)
    {
        services.AddOptions<AgentCredentialOptions>()
            .BindConfiguration(AgentCredentialOptions.SectionName)
            .Validate(options => options.IsValid(), $"Configuração inválida em '{AgentCredentialOptions.SectionName}': a validade padrão deve ficar entre 1 dia e o máximo, que vai até 365 dias.")
            .ValidateOnStart();

        services.AddScoped<IAgentService, AgentService>();
    }

    private static void AddIdentityServices(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(
                options => options.IsValid(),
                $"Configuração inválida em '{JwtOptions.SectionName}': Issuer, Audience, SigningKey (mínimo de {JwtOptions.MinimumSigningKeyBytes} bytes) e durações são obrigatórios.")
            .ValidateOnStart();

        services.AddOptions<AccountLinkOptions>()
            .BindConfiguration(AccountLinkOptions.SectionName)
            .Validate(
                options => options.WebBaseUrl is { IsAbsoluteUri: true },
                $"'{AccountLinkOptions.SectionName}:WebBaseUrl' deve ser uma URL absoluta.")
            .ValidateOnStart();

        services.AddDataProtection();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;

                // NIST SP 800-63B: comprimento mínimo, sem regras de composição.
                options.Password.RequiredLength = AuthLimits.PasswordMinLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<ProjectManagerDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<PortugueseIdentityErrorDescriber>();

        // Links de confirmação e de redefinição de senha.
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(2));

        services.AddScoped<AccessTokenIssuer>();
        services.AddSingleton<RefreshTokenDerivation>();
        services.AddScoped<SessionManager>();
        services.AddScoped<AccountEmails>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
    }

    private static void AddEmail(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .Validate(options => options.IsValid(), $"Configuração inválida em '{EmailOptions.SectionName}': Host, Port e FromAddress são obrigatórios.")
            .ValidateOnStart();

        services.AddScoped<IEmailSender, SmtpEmailSender>();
    }

    private static void AddPersistence(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(ProjectManagerDatabase.ConnectionStringName) ?? string.Empty)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"A connection string 'ConnectionStrings:{ProjectManagerDatabase.ConnectionStringName}' é obrigatória.")
            .ValidateOnStart();

        services.AddDbContext<ProjectManagerDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            ProjectManagerDatabase.Configure(options, databaseOptions.ConnectionString);
        });

        services.AddHealthChecks()
            .AddDbContextCheck<ProjectManagerDbContext>(ProjectManagerDatabase.HealthCheckName);
    }
}
