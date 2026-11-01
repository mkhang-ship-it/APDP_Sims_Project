# PHP → C# map (Bước 3 + 4 + batch 5–12)

Đã port: Auth, Students, Courses, Enrolments, Grades, Import CSV, Dashboard, Audit,
**phân quyền theo module**, **Student↔User**, **CLB/multi-club**, **sự kiện/check-in**,
**geofence 5 km**, **kế hoạch sự kiện**, **tài chính (hoá đơn/chi phí)**, **văn bản**,
**kỷ luật/điểm rèn luyện**, **khen thưởng**, **bốc thăm + trang công khai**, **thông báo**, **cài đặt**,
**link rút gọn**, **hồ sơ**, **ảnh sự kiện**, **webhook SePay**, **bảo trì**, **nhật ký**,
**đề xuất tính năng**, **nhập/xuất Excel**.
Chưa port: xem mục cuối + `docs/parity-closure.md`.

| PHP (Laravel) | C# (SIMS-dotnet) | Ghi chú |
|---|---|---|
| `Auth/LoginController.php` | `Web/Controllers/AccountController.cs` | Cookie auth; thêm claim `club_id` khi login |
| `users` + `User.php` | `Domain/Security/User.cs` + `users.csv` | Thêm cột `email` để nối Student↔User |
| `CheckPermission` middleware + `hasPermissionForModuleCode()` | `Web/Security/PermissionAuthorization.cs` + `permissions.csv` + `PermissionCatalog` | Ma trận module/action thay cho role thuần |
| `modules`, `permissions`, `role_permissions` | `Domain/Security/RolePermission.cs` + `permissions.csv` | 6 module × action đã port (mở rộng dần) |
| `Member*` / `Club/MemberController` | `Domain/Students/Student.cs`, `StudentService` | Thêm `UserId`, `ClubId` |
| `members` (theo CLB) | `students.csv` + lọc theo `club_id` claim | Sinh viên chưa gán CLB hiển thị cho mọi CLB (có ghi chú) |
| `clubs` + `ClubController` | `Domain/Clubs/Club.cs`, `Application/Clubs/ClubService.cs`, `ClubsController` | CRUD + danh sách + đếm thành viên |
| `user_club_roles` (multi-club) | `Domain/Clubs/ClubMembership.cs` + `club_memberships.csv` | Vai trò riêng trong từng CLB |
| `SwitchClubController` + `session('club_id')` | `ClubsController.Switch` + claim `club_id` (cookie) | Đổi CLB không cần session server |
| `Course.php`, `CourseController.php` | `Domain/Courses/Course.cs`, `CourseService` | Capacity + prerequisite |
| *(mới)* | `Enrolment`, `EnrolmentService`, `GradeService` | Đăng ký + điểm |
| `MemberImport.php` (CSV + Excel) | `StudentImportService` + `CsvStudentImportParser` + `Application/Common/Xlsx.cs` | Streaming, lỗi theo dòng; XLSX nhận diện qua chữ ký `PK`, chuyển sang CSV nội bộ (tối đa 5 MB) |
| `Event.php`, `EventController.php`, `EventCheckin.php` | `Domain/Events/Event.cs` + `Attendance`, `EventService`, `CheckinService`, `EventsController`, `CheckinController` | Lịch tháng, trạng thái, token check-in (nội dung QR), chống trùng + sức chứa; `Event.Venue` (`GeoPoint`) + bán kính 5 km |
| `Club/CheckinController` (QR + toạ độ) | `CheckinController` + `Views/Checkin/Scanner.cshtml`, `Checkin/Index.cshtml` | Quét QR bằng `BarcodeDetector` (BCL trình duyệt), `navigator.geolocation` gửi `latitude`/`longitude`, nhập mã tay fallback |
| `class_room.latitude/longitude` (geofence gốc) | `Domain/Events/GeoPoint.cs` + `Event.Venue` + `Attendance.DistanceKm` | PHP gắn geofence vào phòng học; .NET không có entity Classroom nên gắn vào địa điểm tổ chức của sự kiện |
| `EventPlan.php`, `EventPlanController.php` | `Domain/Events/EventPlan.cs`, `Application/EventPlans/EventPlanService.cs`, `EventPlansController` | 1 kế hoạch/sự kiện, tóm tắt + 7 trường mẫu PHP (người lập, ngày triển khai, người duyệt, đối tượng, mô tả, đơn vị chủ trì, cố vấn) + 5 mục chi tiết, audit |
| `EventPlanObjective.php` / `EventActionPlan.php` / `EventPlanBudgetItem.php` / `EventPlanEquipment.php` / `EventPlanTimeline.php` | `Domain/Events/PlanItems.cs` (5 entity), 6 file CSV | Mục tiêu/hành động/ngân sách/thiết bị/timeline; trường PHP gộp lại, xem `porting-notes` |
| `EventPlanExport.php` (Excel) | `EventPlanService.ExportCsvAsync` + `ExportXlsxAsync` + `EventPlansController.Export` | CSV UTF-8 BOM và XLSX thật bằng BCL (`Application/Common/Xlsx.cs`), không thêm package |
| `Invoice.php`, `Club/InvoiceController.php` | `Domain/Finance/Invoice.cs`, `Application/Finance/InvoiceService.cs`, `InvoicesController` | Tạo hàng loạt theo thành viên, thanh toán từng phần, quá hạn; `semester_id` → `period` (chuỗi) |
| `Transaction.php` | `Domain/Finance/Transaction.cs` + `transactions.csv` | Thu (hoá đơn) / chi (chi phí), phương thức Sepay/PayOS/Paykit/Cash |
| `Expense.php`, `Club/ExpenseController.php` | `Domain/Finance/Expense.cs`, `Application/Finance/ExpenseService.cs`, `ExpensesController` | Duyệt/từ chối/đã chi; quản trị tạo duyệt sẵn, thành viên tạo chờ duyệt |
| `InvoiceController::exportPdf` (DomPDF) | `InvoicesController.Receipt` + `Views/Invoices/Receipt.cshtml` | Phiếu in HTML (Ctrl+P → PDF), **không thêm package PDF** |
| `SePayWebhookListener`, PayOS callback | chưa port | Không có cổng thanh toán/không thêm package; ghi nhận thanh toán thủ công |
| `UpdateOverdueInvoices` command | `POST /Invoices/MarkOverdue` | Không có scheduler trong bản .NET; cập nhật theo yêu cầu |
| `DocumentType.php`, `Club/DocumentTypeController.php` | `Domain/Documents/Documents.cs`, `DocumentService`, `DocumentsController` | Mã `DT-######`, liên kết mẫu tải ngoài |
| `Document.php`, `DocumentUserAccess.php`, `Club/DocumentController.php`, `Club/UserDocumentController.php` | `Document`, `DocumentAccess`, `DocumentService`, `LocalFileStorage` | Upload thật (`data/files`), duyệt/từ chối, public/private/restricted, tải có kiểm tra quyền |
| `DocumentRequest.php`, `DocumentRequestUser.php`, `Club/DocumentRequestController.php` | `DocumentRequest`, `DocumentRequestUser`, `DocumentRequestService` | Tạo yêu cầu + hạn nộp, thành viên nộp file, liên kết văn bản đã nộp |
| `Discipline.php`, `Club/DisciplineController.php` | `Domain/Disciplines/Disciplines.cs`, `DisciplineService` | Danh mục điểm dương/âm (`KL-######`) |
| `DisciplineRecord.php`, `Club/DisciplineRecordController.php` | `DisciplineRecord`, `DisciplineService` | Áp dụng/sửa/thu hồi; `semester_id` → `period` |
| `SchoolPoint.php`, `Club/MyPointsController.php` | `DisciplineService.GetMemberPointsAsync/GetUserPointsAsync` | Điểm trường = scale 5 + 5 cán bộ; tính trực tiếp, không lưu bảng |
| `Award.php`, `AwardRecipient.php`, `Club/AwardController.php` | `Domain/Awards/Awards.cs`, `AwardService`, `AwardsController` | Mã `BK-######`, trạng thái, người nhận + chứng nhận |
| `LuckyDraw.php`, `Prize.php`, `Club/LuckyDrawController.php`, `Club/PrizeController.php` | `Domain/LuckyDraws/LuckyDraws.cs`, `LuckyDrawService`, `LuckyDrawsController` | Slug công khai, mã bảo mật, quay số bằng RNG; giải + người trúng lưu CSV |
| `LuckyDrawPublicController.php`, `public/lucky-draw/show.blade.php` | `LuckyDrawPublicController` + `Views/LuckyDrawPublic/Show.cshtml` | Trang công khai không đăng nhập, mã bảo mật xác thực qua session (60 phút) |
| `public/homepage/index.blade.php` (không có route trong PHP) | `HomeController` nhánh ẩn danh + `Views/Home/PublicHome.cshtml` | Trang chủ công khai: CLB, sự kiện đang/ sắp diễn ra, chương trình bốc thăm đang mở |
| `Notification.php`, `NotificationRead.php`, `Club/NotificationController.php` | `Domain/Notifications/Notifications.cs`, `NotificationService`, `NotificationsController` | Gửi toàn bộ CLB hoặc CLB cụ thể, hộp thư + đếm chưa đọc + đánh dấu đã đọc |
| `Setting.php`, `ThemeSetting.php`, `SettingController.php`, `Club/SettingController.php` | `Domain/Settings/Settings.cs`, `SettingsService`, `SettingsController` | Key/value toàn hệ thống và theo CLB; tên site/theme/màu áp vào layout |
| `ShortUrl.php`, `ShortUrlController.php`, `Club/ShortUrlController.php` | `Domain/ShortUrls`, `ShortUrlService`, `ShortUrlsController` | Mã 12 ký tự, hết hạn/tạm dừng, `/s/{code}` + đếm lượt; **chưa có ảnh QR** (cần package) |
| `MyProfileController`, `Club/MyProfileController` | `ProfileController` + `ProfileService` | Sửa tên/email, đổi mật khẩu có xác minh mật khẩu hiện tại |
| `EventImageController`, `Club/EventImageController` | `EventImageService` + `EventsController` | Upload/xem/xoá ảnh JPG/PNG/WEBP, lưu `data/files/events` |
| `SePayWebhookListener` | `PaymentWebhookService` + `SePayWebhookController` | `POST /api/payments/sepay/webhook`, header `Authorization: Apikey`, ghi thanh toán + idempotent |
| `maintenance` middleware (PHP) | `MaintenanceMiddleware` + settings `system.maintenance` | Khách nhận 503, Admin/health/webhook vẫn hoạt động |
| `LogController` + `system_logs` | `LogsController` + `IAuditLogReader` | Xem audit.csv, lọc actor/action (Admin) |
| `MemberExport.php` | `StudentsController.Export` | CSV UTF-8 (mặc định) và XLSX (`?format=xlsx`) theo phạm vi CLB |
| `FeatureRequest.php`, `FeatureRequestController`, `Club/FeatureRequestController` | `Domain/FeatureRequests`, `FeatureRequestService`, `FeatureRequestsController` | Thành viên gửi, nhân viên đổi trạng thái/phản hồi |
| `Club/DashboardController.php` | `DashboardService` | Đếm số liệu + CLB + sự kiện sắp tới (dashboard V1) |
| `system_logs` + `LogSystemActivity` | `audit.csv` + `IAuditLog` | Audit mọi thao tác ghi |

## Quy ước

| Laravel | .NET |
|---|---|
| Controller | `*Controller` (MVC) |
| Use case | `*Service` (Application) |
| Repository | `IRepository<T>` / `I*Repository` (Application) + `Csv*Repository` (Infrastructure) |
| Validation | `IValidator<T>` + `*Validator` |
| Phân quyền | `[RequirePermission(module, action)]` + policy `perm:module:action` |
| Pattern | Factory, Strategy, Decorator (audit), Repository |
| Dữ liệu | 1 file CSV / aggregate trong `data/` |
| Layout / view | `Views/Shared/_Layout.cshtml` + `wwwroot/css/site.css` (token, shell, dashboard, component V2/V3: `.section`/`.toolbar`/`.table-shell`/`.pager`/`.form-grid`/`.detail-list`/`.notice`/`.auth`/`.state-page`) + `wwwroot/css/theme.css` (bắc cầu Bootstrap) + `wwwroot/js/site.js` (theme, cmdk, `initTable` lọc/sort/phân trang client), thay cho Blade layout + theme của Laravel. Toàn bộ view đã viết lại ở vòng V2/V3 (2026-11-01) |
| Theme người dùng | `site.theme` + `localStorage['sims-theme']` → `data-theme`/`data-bs-theme` (bản PHP lưu theme trong bảng settings và áp ở layout) |

## Chức năng PHP chưa có bản C#

OAuth GitHub/Microsoft, đăng ký theo token CLB, chatbot, email/SMTP (thông báo hiện chỉ in-app),
PayOS, PDF, ảnh QR, mạo danh, API key/mobile API, quản trị trung tâm
(semester/major/classroom/slot/user/role/quote/session), vai trò CLB tuỳ biến (`is_leader`),
chặn iOS cho link rút gọn, API ghi trang bốc thăm công khai.
Chi tiết + lý do: `docs/parity-closure.md`.
