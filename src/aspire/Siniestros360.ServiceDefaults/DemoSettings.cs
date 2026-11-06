using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Siniestros360.ServiceDefaults;

public static class DemoSettings
{
    public static string? GetPassword(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("Demo:Enabled"))
        {
            return null;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Demo mode is allowed only in Development.");
        }

        var password = configuration["Demo:Password"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new InvalidOperationException("Demo:Password must be provided and contain at least eight characters.");
        }

        return password;
    }
}
