using System.Net.Http.Headers;
namespace Siniestros360.DocumentsService.Infrastructure.Storage;

public sealed record ObjectStorageResult(string ObjectName, string? ETag);
public interface IObjectStorage
{
    Task<ObjectStorageResult> UploadAsync(string container, string objectName, Stream content, string contentType, CancellationToken ct);
    Task<Stream> DownloadAsync(string container, string objectName, CancellationToken ct);
    Task DeleteAsync(string container, string objectName, CancellationToken ct);
}
public sealed class LocalObjectStorage(IWebHostEnvironment environment) : IObjectStorage
{
    // Fuera del código fuente: en Windows "storage" coincidiría con la carpeta Storage/ del proyecto.
    private string Root => Path.Combine(environment.ContentRootPath, "uploads");
    private string PathFor(string container, string objectName) { var safeContainer = Path.GetFileName(container); var safeObject = string.Join(Path.DirectorySeparatorChar, objectName.Split('/', '\\').Select(Path.GetFileName)); var root = Path.GetFullPath(Path.Combine(Root, safeContainer)); var path = Path.GetFullPath(Path.Combine(root, safeObject)); if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid storage path."); return path; }
    public async Task<ObjectStorageResult> UploadAsync(string container, string objectName, Stream content, string contentType, CancellationToken ct) { var path = PathFor(container, objectName); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await using var output = File.Create(path); await content.CopyToAsync(output, ct); return new(objectName, null); }
    public Task<Stream> DownloadAsync(string container, string objectName, CancellationToken ct) => Task.FromResult<Stream>(File.OpenRead(PathFor(container, objectName)));
    public Task DeleteAsync(string container, string objectName, CancellationToken ct) { var path = PathFor(container, objectName); if (File.Exists(path)) File.Delete(path); return Task.CompletedTask; }
}
public sealed class AzureBlobObjectStorage(HttpClient client, IConfiguration configuration) : IObjectStorage
{
    private Uri UriFor(string container, string objectName) { var service = configuration["Storage:BlobServiceUri"] ?? throw new InvalidOperationException("Storage:BlobServiceUri is required."); var sas = configuration["Storage:SasToken"]; return new Uri($"{service.TrimEnd('/')}/{Uri.EscapeDataString(container)}/{string.Join('/', objectName.Split('/').Select(Uri.EscapeDataString))}{(string.IsNullOrWhiteSpace(sas) ? "" : "?" + sas.TrimStart('?'))}"); }
    public async Task<ObjectStorageResult> UploadAsync(string container, string objectName, Stream content, string contentType, CancellationToken ct) { using var request = new HttpRequestMessage(HttpMethod.Put, UriFor(container, objectName)); request.Headers.Add("x-ms-blob-type", "BlockBlob"); request.Headers.Add("x-ms-version", "2023-11-03"); request.Content = new StreamContent(content); request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType); using var response = await client.SendAsync(request, ct); response.EnsureSuccessStatusCode(); return new(objectName, response.Headers.ETag?.Tag); }
    public async Task<Stream> DownloadAsync(string container, string objectName, CancellationToken ct) { using var response = await client.GetAsync(UriFor(container, objectName), HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode(); var memory = new MemoryStream(); await (await response.Content.ReadAsStreamAsync(ct)).CopyToAsync(memory, ct); memory.Position = 0; return memory; }
    public async Task DeleteAsync(string container, string objectName, CancellationToken ct) { using var response = await client.DeleteAsync(UriFor(container, objectName), ct); if (response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode(); }
}
