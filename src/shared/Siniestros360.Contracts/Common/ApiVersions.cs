namespace Siniestros360.Contracts.Common;

public static class ApiVersions
{
    public const int V1 = 1;

    public static string V1Prefix => $"/api/v{V1}";

    public static string V1Path(string path) => $"{V1Prefix}/{path.TrimStart('/')}";
}
