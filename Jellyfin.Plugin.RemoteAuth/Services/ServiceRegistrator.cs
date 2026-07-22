using Jellyfin.Plugin.RemoteAuth.Auth;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.RemoteAuth.Services;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IAuthenticationProvider, RemoteAuthProvider>();
        serviceCollection.AddSingleton<StateManager>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<StateManager>());
        serviceCollection.AddScoped<RbacService>();
        serviceCollection.AddScoped<UserSyncService>();
    }
}
