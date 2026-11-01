namespace SIMS.Application.Validation;

/// <summary>Một lỗi validation gắn với tên trường (rỗng = lỗi chung).</summary>
public sealed record ValidationError(string Field, string Message);
