namespace SIMS.Web.Models;

/// <summary>Nội dung cho partial `_EmptyState` — dùng chung ở mọi trang danh sách.</summary>
public sealed class EmptyStateModel
{
    public string Title { get; init; } = "Chưa có dữ liệu";

    public string? Text { get; init; }

    public string? ActionText { get; init; }

    public string? ActionUrl { get; init; }
}

/// <summary>Thanh công cụ của bảng dữ liệu (tìm kiếm + số dòng mỗi trang).</summary>
public sealed class TableToolbarModel
{
    public string Placeholder { get; init; } = "Tìm trong danh sách…";

    public int PageSize { get; init; } = 10;
}
