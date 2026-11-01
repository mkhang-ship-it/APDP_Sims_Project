using SIMS.Application.Abstractions;

namespace SIMS.Infrastructure.Storage;

/// <summary>
/// Kho tệp cục bộ dưới thư mục dữ liệu (mặc định `data/files`).
/// Tên tệp do hệ thống sinh (GUID) để tránh ghi đè và path traversal.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Thư mục kho tệp không hợp lệ.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
    }

    public async Task<string> SaveAsync(
        Stream content,
        string originalFileName,
        string folder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var safeFolder = NormalizeFolder(folder);
        var fileName = Guid.NewGuid().ToString("N") + NormalizeExtension(originalFileName);
        var relativePath = safeFolder.Length == 0 ? fileName : safeFolder + "/" + fileName;
        var fullPath = Resolve(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 8192,
            useAsync: true);

        await content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        return relativePath;
    }

    public Stream? OpenRead(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var fullPath = Resolve(relativePath);

        return File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
    }

    public bool Exists(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        return File.Exists(Resolve(relativePath));
    }

    public void Delete(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            var fullPath = Resolve(relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (IOException)
        {
            // Tệp đang mở — bỏ qua, không làm hỏng thao tác nghiệp vụ.
        }
        catch (UnauthorizedAccessException)
        {
            // Không có quyền xoá — bỏ qua.
        }
    }

    private string Resolve(string relativePath)
    {
        var cleaned = relativePath.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_root, cleaned));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn tệp nằm ngoài kho dữ liệu.");
        }

        return fullPath;
    }

    private static string NormalizeFolder(string folder)
    {
        var chars = (folder ?? string.Empty)
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .ToArray();

        return new string(chars);
    }

    private static string NormalizeExtension(string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName ?? string.Empty).ToLowerInvariant();

        if (extension.Length is < 2 or > 10)
        {
            return string.Empty;
        }

        return extension[1..].All(ch => char.IsLetterOrDigit(ch)) ? extension : string.Empty;
    }
}
