namespace SIMS.Application.Abstractions;

/// <summary>
/// Kho file đính kèm (văn bản, chứng nhận). Nội dung nghiệp vụ vẫn là CSV;
/// chỉ tệp nhị phân nằm ngoài CSV, dưới thư mục dữ liệu.
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(
        Stream content,
        string originalFileName,
        string folder,
        CancellationToken cancellationToken = default);

    Stream? OpenRead(string relativePath);

    bool Exists(string relativePath);

    void Delete(string relativePath);
}

/// <summary>Strategy sinh mã tham chiếu dạng {prefix}-{6 số} (DT, KL, BK).</summary>
public interface IReferenceCodeGenerator
{
    string Next(string prefix);
}
