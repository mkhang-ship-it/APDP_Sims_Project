# Parity closure — đóng gap PHP → .NET

Tài liệu này chốt trạng thái từng chức năng PHP còn thiếu sau Batch 12, kèm **cách kiểm chứng**
hoặc **lý do cụ thể** khi không port. Không có hạng mục nào bị bỏ qua mà không có dòng ở đây.

## 1. Đã đóng trong Batch 12

| Chức năng PHP | Trạng thái .NET | Kiểm chứng |
|---|---|---|
| Geofence check-in 5 km | Ported — `Domain/Events/GeoPoint.cs`, gắn vào `Event.Venue` thay vì `class_room` | `GeoPointTests` (5), `GeofenceCheckinTests` (5), Web `Geofence_checkin_rejects_far_location_and_accepts_near`; smoke: check-in Hà Nội bị từ chối "quá xa", check-in trong 5 km thành công |
| Gửi toạ độ từ trình duyệt | Ported — `navigator.geolocation` trong `Views/Checkin/Index.cshtml` | Cùng test Web trên (gửi `latitude`/`longitude`) |
| Import Excel (MemberImport) | Ported — `Application/Common/Xlsx.cs` (BCL) + nhận diện `PK` | `XlsxTests` (3), Web `Xlsx_student_import_creates_student`; smoke: import lại file xuất ra → "Kết quả", 1 dòng |
| Export Excel danh sách sinh viên | Ported — `/Students/Export?format=xlsx` | Web `Students_export_xlsx_is_a_real_workbook`; smoke: 200, MIME XLSX, 1817 B, zip có 5 entry chuẩn |
| Export Excel kế hoạch sự kiện | Ported — `EventPlanService.ExportXlsxAsync` | `EventPlanDetailsTests.Export_xlsx_*`, Web `Event_plan_xlsx_export_contains_extended_fields` |
| Trường mở rộng kế hoạch (người lập, ngày triển khai, người duyệt, đối tượng, mô tả, đơn vị chủ trì, cố vấn) | Ported — `EventPlan` + `EventPlansController.Details` | `EventPlanDetailsTests.Update_details_*`, `CsvGeofenceAndPlanTests.Plan_details_round_trip`; smoke: lưu "Ban truyền thông"/"Cố vấn học tập" hiện ngay trong trang |
| Webhook SePay | Ported ở Batch 11 | `PaymentWebhookServiceTests` (3), Web `Sepay_webhook_marks_invoice_paid` |

## 2. Không port — kèm lý do và cách bù

Mọi hạng mục dưới đây **không thể** làm đúng trong ràng buộc hiện tại (không thêm package,
không có DB, không mở endpoint ẩn danh). Phần bù đã có sẵn và dùng được.

| Chức năng PHP | Lý do không port | Đã bù bằng |
|---|---|---|
| PDF biên lai (`InvoiceController::exportPdf`, DomPDF) | Ràng buộc: không thêm package NuGet; PDF cần engine layout (không có trong BCL) | Trang in HTML `/Invoices/Receipt/{id}` → Ctrl+P → "Lưu thành PDF" |
| Ảnh QR (check-in, short URL) | Cần thư viện sinh QR (không có trong BCL) | Token 12 ký tự làm nội dung QR bất kỳ; trang check-in quét bằng `BarcodeDetector` của trình duyệt; short URL là link thường |
| Email/SMTP, email template, đính kèm thông báo | Không có hạ tầng mail; gửi mail "giả" sẽ là stub, vi phạm yêu cầu chức năng thật | Thông báo in-app đầy đủ (hộp thư, đếm chưa đọc, đánh dấu đã đọc) |
| OAuth GitHub/Microsoft | Ngoài phạm vi bảo mật đã chốt: cookie auth + PBKDF2, không Identity/OAuth | Đăng nhập username/password; 3 vai trò + ma trận quyền |
| PayOS | Cần SDK/khoá bên thứ ba | SePay webhook đã port (Apikey, idempotent); ghi nhận thủ công cho cổng còn lại |
| Mạo danh (impersonation) | Rủi ro bảo mật cao, không có yêu cầu vận hành | — (không bù) |
| API key/mobile API (`Club/ApiKeyController`) | Không có client di động; mở API key sẽ nới quyền | Trang web responsive |
| Đăng ký tài khoản bằng token CLB | Cần chống lạm dụng/throttle + duyệt — chưa có trong acceptance | Admin tạo user (seed/CSV) |
| Chatbot | Ngoài phạm vi | — |
| Quản trị trung tâm (semester, major, classroom, slot, user/role admin, quote, province/commune) | Ngoài phạm vi "quản lý CLB" của các batch; là hệ thống riêng của trường | `period` dạng chuỗi thay `semester_id`; toạ độ gắn vào sự kiện thay vì phòng học |
| Vai trò CLB tuỳ biến + `is_leader` | Tránh đổi schema membership giữa dòng | Enum Admin/Faculty/Student; cán bộ = Admin/Faculty membership |
| Theme editor đầy đủ (layout width, menu style, logo) | Cần lưu nhiều theme + upload logo ngoài phạm vi | Light/dark + màu chủ đạo + tên/mô tả site |
| Chặn iOS cho short URL | Middleware đặc thù PHP, không có yêu cầu tương ứng | — (không bù) |
| API công khai ghi kết quả bốc thăm | Tránh endpoint ghi ẩn danh | Quay số từ trang quản lý có quyền; trang công khai chỉ đọc |
| Scheduler `UpdateOverdueInvoices` | Không có scheduler trong bản .NET (không thêm package) | Nút/endpoint `POST /Invoices/MarkOverdue` |
| Session xác thực mã bảo mật dài hạn | Session in-memory (không có DB/distributed) | Session 60 phút; dữ liệu nghiệp vụ vẫn trong CSV |

## 3. Bất biến đã kiểm

| Kiểm tra | Kết quả |
|---|---|
| `dotnet build SIMS.sln -warnaserror` | 0 warning, 0 error |
| `dotnet test SIMS.sln` | **387/387** (Domain 98 · Application 155 · Infrastructure 66 · Architecture 3 · Web 65) |
| Package NuGet thêm mới | 0 (chỉ 5 package test đã có) |
| `data/` trong repo | không tồn tại (test dùng thư mục tạm) |
| `NotImplementedException` / `TODO` trong `src/` | 0 |
| Route `[AllowAnonymous]` | chỉ Login/Logout/AccessDenied, trang chủ công khai, `/lucky/{slug}`, `/s/{code}`, webhook SePay (có khoá) |
| Nguồn PHP | không sửa (worktree PHP vẫn 17 mục `git status`) |
| Commit/push | không thực hiện |
