using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Siniestros360.ServiceDefaults.Endpoints;

public static class ApiVersionedRoutes
{
    public static RouteGroupBuilder MapApiVersion(this WebApplication app, string path, int majorVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(majorVersion, 1);

        RouteGroupBuilder group = app.NewVersionedApi()
            .MapGroup($"/api/v{{version:apiVersion}}/{path}");

        group.HasApiVersion(new ApiVersion(majorVersion, 0));
        return group;
    }
}
