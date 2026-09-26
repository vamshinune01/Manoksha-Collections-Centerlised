using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Application;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Identity.Endpoints;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Modules.Identity.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Manoksha.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, IdentityModelConfiguration>();
        services.AddOptions<IdentityOptions>().Bind(configuration.GetSection(IdentityOptions.SectionName));
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddSingleton<KeyMaterial>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<SecretProtector>();
        services.AddSingleton<PasswordService>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<SessionService>();
        services.AddScoped<OtpService>();
        services.AddScoped<InternalAuthService>();
        services.AddScoped<ExternalAuthService>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<RoleAdministrationService>();
        services.AddScoped<MeService>();
        services.AddScoped<CustomerProfileService>();
        services.AddScoped<IStartupSeeder, IdentitySeeder>();
        services.AddScoped<IInternalUserAccounts, InternalUserAccounts>();
        services.AddScoped<IResellerAccounts, ResellerAccounts>();
        services.AddScoped<IDevelopmentSeeder, IdentityDevelopmentSeeder>();
    }

    public void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TokenService>((options, tokens) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = tokens.AccessTokenValidationParameters();
                options.Events = new JwtBearerEvents { OnTokenValidated = SessionValidation.OnTokenValidated };
            });

        services.AddSingleton<IAuthorizationPolicyProvider, ManokshaPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, AudienceHandler>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddAuthorizationBuilder()
            // Secure by default: every endpoint requires authentication unless it explicitly allows anonymous.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        AdminIdentityEndpoints.Map(endpoints);
    }
}
