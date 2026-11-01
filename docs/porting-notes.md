# Porting notes — Bước 3 + 4 và các batch 5–11

## Quyết định kỹ thuật

1. **SQL Server là engine chính; CSV là chế độ phụ.** Không EF/ORM, không SQLite/JWT/OAuth/Identity.
   Mọi repository đọc/ghi qua `IRowStore` — `SqlRowStore` (mặc định), `CsvFileStore`, `InMemoryRowStore`.
   Chỉ `RowStoreFactory` chọn backend theo `Storage:Provider`.
   Auth = cookie + PBKDF2-HMAC-SHA256 (100.000 vòng, salt 16 bytes, key 32 bytes),
   so sánh `CryptographicOperations.FixedTimeEquals`.
1a. **Vì sao mô hình ô key/value thay vì 38 schema riêng.** Mỗi bảng là
   `[sims].[<tên>](Id INT IDENTITY PK, RowNo INT, Ord INT, Value NVARCHAR(MAX))`; một bản ghi = các dòng
   cùng `RowNo`. Đổi lại: không phải viết/migrate 38 bảng có kiểu thật, và toàn bộ logic nghiệp vụ cùng
   test hiện có không phải sửa một dòng. Cái giá ghi ở mục Giới hạn.
1b. **Ghi nguyên tử trên SQL.** `BEGIN TRAN (Serializable)` → `sp_getapplock @LockMode='Exclusive',
   @LockOwner='Transaction'` theo tên bảng → đọc → `DELETE` toàn bảng → insert lại → `COMMIT`.
   Cùng hợp đồng với `CsvFileStore`, nên `MutateAsync` vẫn đúng ngữ nghĩa read-modify-write.
1c. **Một bẫy đã trả giá.** Câu `INSERT … VALUES (0,@v0),(1,@v1),(2,@v2)` với cột `NVARCHAR(MAX)` và tham số
   rỗng **âm thầm chỉ ghi một dòng**. Triệu chứng: mỗi bản ghi còn 1 ô, đọc lại ra ô rỗng khắp nơi, mà không
   có exception nào. Sửa: một lệnh `INSERT` cho mỗi ô, giữ `@ord`/`@val` và gán lại `Value` trong vòng lặp.
   `SqlRowStoreTests` khoá hành vi này.
2. **Ghi nguyên tử (chế độ CSV).** `CsvFileStore` ghi `<file>.tmp-<guid>` cùng thư mục → `Flush(flushToDisk)` →
   `File.Move(overwrite: true)`; `SemaphoreSlim` tĩnh theo đường dẫn ⇒ tuần tự hoá append và read-modify-write.
3. **Sức chứa + chống trùng của Enrolments là atomic.** `CsvEnrolmentRepository.TryAddAsync` đọc–kiểm tra–ghi
   trong **một** `MutateAsync`; vi phạm thì không ghi gì (rollback tự nhiên), trả `TryEnrolResult.Fail`.
4. **Import CSV streaming.** `CsvStudentImportParser` đọc từng dòng bằng `StreamReader.ReadLineAsync`,
   không `ReadAllLines`; lỗi gắn `RowNumber`, dòng lỗi bị bỏ qua và các dòng hợp lệ vẫn được ghi.
   Parser nằm sau `IStudentImportParser` (OCP) → test dùng fake parser, không cần file thật.
5. **SOLID anchors.**
   - OCP: `IStudentImportParser` + `CsvStudentImportParser` + `FakeParser` (test).
   - LSP: `IRepository<T>` — `CsvRepository<T>` (thật) và `InMemoryRepository<T>` (test) thay thế được nhau.
   - ISP: `IStudentReader` / `IStudentWriter`; `DashboardService` chỉ phụ thuộc reader.
   - DIP: `Program.cs` wiring toàn bộ interface; service không biết CSV.
   - Patterns: Factory (`Create/Restore`), Strategy (`IPrerequisitePolicy`, `IGradePolicy`),
     Decorator (`AuditedCourseService`, `AuditedStudentImportService`), Repository.
6. **Thang điểm 10** (giả định sản phẩm, dễ đổi qua `IGradePolicy`): A≥9, B≥8, C≥7, D≥6, E≥5 (đạt), F<5.
   Điều kiện tiên quyết: đã hoàn thành học phần trước với điểm ≥ 5.
7. **Seed demo** (idempotent): 3 tài khoản, 1 sinh viên, 3 học phần (CS101 → CS102 tiên quyết), 2 đăng ký,
   1 dòng audit `system.seed`, và `data/students-invalid.csv` (UTF-8 **no BOM**) để thử import.
   Riêng `permissions.csv` được **merge**: thêm những dòng còn thiếu theo `PermissionCatalog`, giữ quyền đã có.
8. **Razor xuất Unicode trực tiếp** (`WebEncoderOptions` + `UnicodeRanges.All`) — tránh entity hoá tiếng Việt.

## Tài khoản demo

| Username | Password | Role |
|---|---|---|
| `admin` | `Admin@12345` | Admin |
| `faculty1` | `Faculty@12345` | Faculty |
| `student1` | `Student@12345` | Student |

## Dữ liệu CSV

`users.csv`, `students.csv`, `courses.csv`, `enrolments.csv`, `clubs.csv`, `club_memberships.csv`,
`permissions.csv`, `events.csv`, `attendance.csv`, `event_plans.csv`,
`event_plan_objectives.csv`, `event_plan_actions.csv`, `event_plan_budget_items.csv`,
`event_plan_equipment.csv`, `event_plan_timeline.csv`, `invoices.csv`, `transactions.csv`, `expenses.csv`,
`document_types.csv`, `documents.csv`, `document_access.csv`, `document_requests.csv`,
`document_request_users.csv`, `disciplines.csv`, `discipline_records.csv`, `awards.csv`,
`award_recipients.csv`, `lucky_draws.csv`, `lucky_prizes.csv`, `lucky_winners.csv`,
`notifications.csv`, `notification_targets.csv`, `notification_reads.csv`, `settings.csv`,
`short_urls.csv`, `short_url_visits.csv`, `event_images.csv`, `feature_requests.csv`,
`audit.csv`, `students-invalid.csv` (mẫu).
Tệp đính kèm nằm ngoài CSV trong `data/files/` (văn bản, chứng nhận).

## Batch 5 — phân quyền module, Student↔User, CLB/multi-club

| Quyết định | Chi tiết |
|---|---|
| Phân quyền theo module | `permissions.csv` (role,module,action) + `[RequirePermission(module, action)]`; policy động `perm:module:action`, không cần khai báo từng policy. Mặc định: Admin toàn quyền, Faculty học vụ, Student dashboard + danh sách CLB + hồ sơ của mình. |
| Student↔User | `Student.UserId` (nullable) + `LinkUserAsync`; khi tạo sinh viên, nếu `users.csv` có tài khoản cùng email thì tự liên kết. |
| Trang "Học tập của tôi" | Vai trò Student xem hồ sơ + đăng ký + điểm của chính mình (chỉ đọc). |
| Multi-club | `clubs.csv` + `club_memberships.csv`; khi login gắn claim `club_id` theo membership đầu tiên; `POST /Clubs/Switch` cấp lại cookie (không đổi dữ liệu). |
| Lọc theo CLB | `StudentsController.Index` chỉ hiện sinh viên thuộc CLB hiện tại **hoặc chưa gán CLB** (giữ dữ liệu chưa phân loại). Admin thấy tất cả. |
| Quyền CLB | Admin: xem/tạo/sửa; Faculty: chỉ xem; Student: xem danh sách để chuyển CLB. |

## Batch 6 — Sự kiện, lịch, check-in QR/token

| Quyết định | Chi tiết |
|---|---|
| Mã check-in = nội dung QR | `Event.CheckinToken` (12 ký tự) và link `/Checkin?code=TOKEN`. Không thêm thư viện sinh QR: ảnh QR cần package/JS ngoài allowlist, nên bản này phát token + link; ban tổ chức có thể in link/chiếu mã. |
| Quét QR phía trình duyệt | `Views/Checkin/Scanner.cshtml` dùng **BarcodeDetector API** (có sẵn trong Chromium/Edge, không cần package); fallback nhập mã tay. |
| Chống trùng + sức chứa atomic | `CsvAttendanceRepository.TryCheckInAsync` kiểm tra + ghi trong một `MutateAsync` (khoá file). |
| Thời gian check-in | Mở từ **1 giờ trước** `StartsAtUtc` đến `EndsAtUtc`, chỉ khi sự kiện `Published`. |
| Trạng thái | `Draft → Published → Completed/Cancelled`; huỷ/hoàn thành có ràng buộc trạng thái. |
| Chưa port | **Geofence 5 km** của bản PHP (cần toạ độ lớp học + Haversine) — ghi nhận ở mục giới hạn. |

## Batch 7 — Kế hoạch sự kiện

| Quyết định | Chi tiết |
|---|---|
| Mô hình | 1 `EventPlan` / sự kiện (tự tạo khi mở trang kế hoạch) + 5 bảng mục: mục tiêu, hành động, ngân sách, thiết bị, timeline. Mỗi mục có `order` tăng dần khi thêm; xoá theo `section` + `itemId`. |
| Trường PHP được gộp | PHP tách `EventActionPlan` (hoạt động, kết quả mong đợi, thời gian bắt đầu/kết thúc, người phụ trách, thành viên hỗ trợ, rủi ro) và `EventPlanObjective` (mục tiêu, kết quả mong đợi, bên phụ trách). Bản C# gộp thành `PlanObjective.Content` và `PlanAction.Content/Owner/DueDate`; các trường kết quả mong đợi/rủi ro/thành viên hỗ trợ **chưa port** (ghi ở mục giới hạn). Tương tự, timeline dùng `TimeLabel` thay cho cặp giờ bắt đầu/kết thúc + ngày. |
| Export | `EventPlanExport.php` xuất Excel; bản C# xuất CSV bằng BCL (`section,field1..field4`) với UTF-8 BOM để Excel đọc đúng tiếng Việt. **Chưa thêm package Excel** vì ràng buộc "không thêm NuGet ngoài gói test" — nếu cần `.xlsx` thật, coordinator phải duyệt package (ví dụ ClosedXML). |
| Quyền | Module `event_plans` với `view` (Admin, Faculty) và `edit` (Admin, Faculty). Student không có quyền → 403. Link "Kế hoạch" trên trang chi tiết sự kiện chỉ hiện khi có `perm:event_plans:view`. |
| Audit | Mọi thao tác ghi phát `eventplan.create`, `eventplan.update`, `eventplan.<section>.add/remove` vào `audit.csv`. |
| Seed | Sự kiện mẫu `EVT001` có sẵn 1 mục cho mỗi nhóm để thử giao diện; seeder chỉ thêm khi kế hoạch chưa có mục tiêu (idempotent). Seeder quyền giờ **bổ sung các dòng còn thiếu** (thay vì bỏ qua khi file đã có) để dữ liệu cũ nhận được `event_plans` khi nâng cấp. |

## Batch 8 — Hoá đơn, thanh toán, chi phí

| Quyết định | Chi tiết |
|---|---|
| Mô hình | `invoices.csv` (1 hoá đơn / thành viên), `transactions.csv` (thu hoặc chi), `expenses.csv` (đề nghị chi). Tiền là `decimal`, làm tròn 2 chữ số, giới hạn dưới 10 tỷ. |
| Mã hoá đơn | `INV-{năm}-{6 số}` sinh bằng `RandomNumberGenerator` (Strategy `IInvoiceCodeGenerator`), kiểm tra trùng trong file trước khi ghi. |
| Học kỳ | PHP dùng `semester_id`; module học kỳ chưa port nên bản C# lưu `period` (chuỗi tự do, ≤40 ký tự) để vẫn lọc/hiển thị theo kỳ. |
| Thanh toán | Nhiều lần cho một hoá đơn; `paid = Σ transaction thu` (sửa lỗi PHP `total_paid` chỉ trả về khi status = paid); đủ tiền thì tự chuyển `Paid`. Chặn trả vượt số còn lại, trả 0 và ngày trước ngày tạo. |
| Quá hạn | Chỉ đánh dấu khi `Pending` và `DueDate < hôm nay`; có nút "Cập nhật quá hạn" (thay command scheduler của PHP). Không tự đổi trạng thái khi chỉ xem danh sách. |
| Chi phí | Quản trị tạo → `Approved` ngay (như `store()` của PHP); thành viên gửi → `Pending` (như `myStore()`); duyệt/từ chối chỉ từ `Pending`; "đã chi" chỉ từ `Approved` và tự ghi một giao dịch chi tiền mặt. |
| Phiếu in | `exportPdf` của PHP dùng DomPDF. Bản C# dùng trang HTML in được (`/Invoices/Receipt/{id}`) — **không thêm package PDF** vì ràng buộc package; trình duyệt có thể "In → Lưu PDF". |
| Quyền | `invoices` (view/create/edit/delete/mark_paid/my_invoices) và `expenses` (view/create/edit/delete/approve/reject/my_*). Admin toàn quyền; Faculty quản lý CLB; Student chỉ xem hoá đơn của mình và gửi/ sửa/ xoá yêu cầu chi của mình khi còn chờ duyệt. |
| Bảo vệ dữ liệu | Mọi action kiểm tra `club_id` của bản ghi với claim hiện tại (Admin bỏ qua); phiếu in cho phép chủ hoá đơn hoặc người có `invoices:view`. |
| Seed | `EVT001`… giữ nguyên; thêm 1 hoá đơn demo 200.000 đ cho `student1` (đã thu 50.000 đ) và 1 chi phí đã duyệt 150.000 đ. |
| Ghi chú nhiều dòng | Ghi chú/mô tả được đổi xuống dòng thành khoảng trắng trước khi lưu CSV (định dạng CSV đọc theo dòng). |

## Batch 9 — Văn bản, kỷ luật/điểm rèn luyện, khen thưởng

| Quyết định | Chi tiết |
|---|---|
| Tệp đính kèm | Dùng `IFileStorage` + `LocalFileStorage`: ghi dưới `data/files/<folder>/<guid>.<ext>`; CSV chỉ lưu đường dẫn tương đối. Cho phép PDF/Word/JPG/PNG/TXT ≤10 MB; tên tệp do hệ thống sinh (chống ghi đè/path traversal). Đây là ngoại lệ có chủ đích của quy ước "dữ liệu nghiệp vụ = CSV": tệp nhị phân không nhét vào CSV. |
| Văn bản | Trạng thái `Pending → Approved/Rejected` (từ chối bắt buộc lý do); hiển thị public/private/restricted; danh sách người được xem lưu `document_access.csv`. Tải xuống cho phép nhân viên cùng CLB hoặc người tải/người được chia sẻ. |
| Yêu cầu nộp | Nhân viên tạo theo loại văn bản + hạn nộp + danh sách người nhận (mỗi người một dòng). Thành viên nộp tệp → tạo văn bản `Pending` và gắn vào yêu cầu; không thể nộp lại/xoá yêu cầu đã có bài nộp. |
| Kỷ luật/điểm | Danh mục điểm trong khoảng −1000..1000; bản ghi `Active/Revoked`; điểm CLB = tổng điểm các bản ghi đang hiệu lực. |
| Điểm trường | Giữ công thức PHP: `(điểm CLB / điểm CLB cao nhất) × 5`, tối thiểu 0, cộng 5 nếu là cán bộ. Không có trường `role.is_leader` trong bản .NET nên **cán bộ = thành viên CLB có vai trò Admin/Faculty**. Điểm được tính khi xem (không lưu `school_points.csv`), xếp hạng theo điểm CLB giảm dần. |
| Học kỳ | Tiếp tục dùng `period` (chuỗi) thay `semester_id`; điểm tổng hợp trên toàn bộ bản ghi hiệu lực, không tách theo kỳ. |
| Khen thưởng | Mã `BK-######`, trạng thái `Pending/Awarded/Revoked`, người nhận kèm chứng nhận tuỳ chọn (PDF/JPG/PNG). Xoá giải thưởng xoá người nhận và tệp chứng nhận. |
| Quyền | `documents` (view/create/edit/delete/approve/my_documents/upload_own/requests/request_create); `discipline` (view/create/edit/delete/revoke/summary/my_points); `awards` (view/create/edit/delete). Admin toàn quyền; Faculty quản lý CLB; Student chỉ văn bản của mình + điểm của mình. |
| Seed | 1 loại văn bản + 1 văn bản công khai đã duyệt + 1 yêu cầu nộp cho `student1`; 2 danh mục kỷ luật + 3 bản ghi (+10, −5 cho sinh viên; +10 cho giảng viên); 1 giải thưởng đã trao cho `student1`. |

## Batch 10 — Bốc thăm, trang công khai, thông báo, cài đặt

| Quyết định | Chi tiết |
|---|---|
| Bốc thăm | `LuckyDraw` lưu danh sách người tham gia dạng chuỗi phân tách bằng dấu gạch đứng (thay mảng JSON của PHP), slug không dấu + 8 ký tự ngẫu nhiên, mã bảo mật tuỳ chọn. Quay số dùng `RandomNumberGenerator`, mỗi người chỉ trúng một lần, giới hạn theo số lượng giải (đếm theo tên giải). |
| Trang công khai | `/lucky/{slug}` không cần đăng nhập. Nếu có mã bảo mật, người xem phải nhập đúng mã; xác thực lưu trong **session** 60 phút. Trang tự làm mới 30 giây sau khi xác thực. |
| Khác PHP | Bản PHP cho phép API công khai tự lưu giải/người tham gia/kết quả quay. Bản C# **chỉ cho phép quay số từ trang quản lý có quyền**; trang công khai chỉ đọc — an toàn hơn và không cần JS wheel phức tạp. Ghi ở mục giới hạn. |
| Trang chủ | Khách chưa đăng nhập thấy `PublicHome` (CLB đang hoạt động, sự kiện đã công bố còn diễn ra, chương trình bốc thăm đang mở). Người đã đăng nhập vẫn thấy dashboard như cũ. |
| Thông báo | Chỉ in-app (không email/tệp đính kèm). `notifications.csv` + `notification_targets.csv` (CLB cụ thể) + `notification_reads.csv` (đã đọc). Admin gửi tất cả CLB hoặc chọn CLB; Faculty chỉ gửi/ sửa thông báo của CLB mình. |
| Hộp thư | Mọi vai trò có `notifications:inbox`; lọc theo CLB hiện tại (gồm thông báo toàn bộ + thông báo đúng CLB), đếm chưa đọc, đánh dấu một/tất cả. |
| Cài đặt | `settings.csv` key/value với `ClubId` null = toàn hệ thống. Bộ khoá cố định: tên/mô tả site, theme light/dark, màu chủ đạo, email hỗ trợ; phía CLB: email/điện thoại/địa chỉ/ngân hàng/số tài khoản. Layout đọc cấu hình để đổi tên site, `data-bs-theme` và màu nút. |
| Session | Bật `AddSession` (in-memory) chỉ để giữ trạng thái xác thực mã bảo mật; không lưu dữ liệu nghiệp vụ trong session. |
| Seed | 1 chương trình bốc thăm đang mở (mã `123456`, 2 giải, 3 người), 2 thông báo (1 toàn bộ + 1 CLB01), cấu hình site + CLB01 mặc định. |

## Batch 11 — Parity closure

| Quyết định | Chi tiết |
|---|---|
| Link rút gọn | Mã 12 ký tự (`a-z A-Z 2-9`, bỏ ký tự dễ nhầm), `/s/{code}` chuyển hướng công khai; hết hạn → 410, tạm dừng/không tồn tại → 404; mỗi lượt ghi vào `short_url_visits.csv`. Chưa sinh ảnh QR (cần package) — chỉ có link. |
| Hồ sơ | Sửa tên hiển thị/email và đổi mật khẩu (xác minh mật khẩu hiện tại, mật khẩu mới 8–128 ký tự). Không có upload ảnh đại diện. |
| Ảnh sự kiện | Upload JPG/JPEG/PNG/WEBP ≤10 MB, lưu `data/files/events`, hiển thị gallery trong chi tiết sự kiện, xoá kèm tệp. |
| Webhook SePay | `POST /api/payments/sepay/webhook`; khoá lưu ở `settings.csv` (`payments.sepay_api_key`), so sánh constant-time với header `Authorization: Apikey …`. Chấp nhận mã `INV-2026-000001` hoặc `INV2026000001`; ghi thanh toán tối đa bằng số còn lại, gọi lại không tạo giao dịch trùng. Endpoint được miễn chế độ bảo trì. |
| Bảo trì | `system.maintenance` = on → khách nhận 503 kèm thông báo; Admin, `/health`, `/Account/Login|Logout`, static files và webhook thanh toán vẫn hoạt động. Bật/tắt trong Cài đặt hệ thống. |
| Nhật ký | `CsvAuditLog` đọc thêm (tối đa 1000 dòng gần nhất), trang `/Logs` chỉ Admin, lọc theo actor/action, hiển thị 50–500 dòng. |
| Xuất sinh viên | `/Students/Export` trả CSV UTF-8 BOM theo phạm vi CLB (thay `MemberExport.php`). |
| Đề xuất tính năng | Bảng `feature_requests.csv`; thành viên tạo (CLB hiện tại), nhân viên đổi trạng thái/phản hồi, chủ đề xuất xoá khi còn mở. |
| Không thêm debug/bypass | Không có endpoint debug ẩn danh, không hạ authorization. Các route ẩn danh chỉ gồm: trang chủ công khai, đăng nhập, `/lucky/{slug}`, `/s/{code}` và webhook SePay có khoá. |

## Batch 12 — Đóng gap cuối (geofence, Excel, kế hoạch mở rộng)

| Quyết định | Chi tiết |
|---|---|
| Geofence | PHP gắn toạ độ vào `class_room` và so khoảng cách khi check-in. Bản .NET không có entity Classroom, nên **gắn toạ độ vào địa điểm tổ chức của sự kiện** (`Event.Venue`, 2 cột `venue_latitude`/`venue_longitude`). Bán kính giữ nguyên 5 km; công thức Haversine trong `Domain/Events/GeoPoint.cs` (validate vĩ độ −90..90, kinh độ −180..180). |
| Chấp nhận/từ chối | Sự kiện có toạ độ → bắt buộc client gửi `latitude`/`longitude`; thiếu → từ chối ("cần vị trí"). Khoảng cách > 5 km → từ chối kèm khoảng cách thực. Sự kiện không có toạ độ → giữ hành vi cũ, không đòi vị trí. Khoảng cách lưu ở `attendance.csv` (`distance_km`) để đối soát. |
| Thu toạ độ | `navigator.geolocation.getCurrentPosition` trong `Views/Checkin/Index.cshtml` điền 2 input ẩn trước khi submit. Không dùng package bản đồ/định vị. |
| Excel | `Application/Common/Xlsx.cs` tự sinh/đọc OOXML bằng `System.IO.Compression` + `XDocument` (BCL), **không thêm package**. Giới hạn 50.000 dòng × 200 cột; chỉ đọc sheet đầu tiên (như nhu cầu thực tế). |
| Xuất Excel | `/Students/Export?format=xlsx` và `/EventPlans/Export?format=xlsx`; CSV vẫn là mặc định để tương thích. |
| Nhập Excel | `/Students/Import` nhận diện chữ ký `PK` (zip) → đọc XLSX → chuyển thành CSV nội bộ rồi dùng lại `CsvStudentImportParser` (một đường xử lý, một bộ thông báo lỗi). Tối đa 5 MB, giữ nguyên kiểm tra phần mở rộng/kích thước. |
| Kế hoạch mở rộng | `EventPlan` thêm 7 trường theo mẫu PHP: `PreparedBy`, `ImplementationDate`, `ApprovedBy`, `Participants`, `Description`, `ResponsibleOrganizer`, `AdvisorCoordinator`. Bảng sửa nhanh ở `/EventPlans?eventId=…` (POST `/EventPlans/Details`), audit như các thao tác khác. |
| Tương thích CSV | `CsvFileStore.ReadCoreAsync` đệm ô rỗng khi dòng cũ ngắn hơn header mới → file `events.csv`/`attendance.csv`/`event_plans.csv` cũ vẫn đọc được, không cần migrate. Dòng dài hơn header vẫn bị bỏ qua như trước. |
| SePay trùng lặp | Webhook SePay đã port ở Batch 11; ghi chú "chưa có webhook" trong giới hạn cũ đã bỏ. |

## Ma trận gap còn lại (chủ đích)

| Hạng mục PHP | Trạng thái | Lý do |
|---|---|---|
| PDF biên lai (DomPDF) | Phiếu in HTML | Cần package PDF; trình duyệt in → PDF |
| Email/SMTP, email template, đính kèm thông báo | Chưa port | Không có hạ tầng mail; thông báo hiện chỉ in-app |
| OAuth GitHub/Microsoft | Chưa port | Ngoài phạm vi bảo mật đã chốt (cookie + PBKDF2, không Identity/OAuth) |
| QR image cho check-in/short URL | Không sinh ảnh | Cần package; đã có token/link và quét bằng BarcodeDetector |
| Module học kỳ (`semesters`) | Dùng chuỗi `period` | Chưa port module trung tâm; ảnh hưởng hoá đơn/chi phí/điểm/khen thưởng |
| Quản trị trung tâm: Major, ClassRoom, Slot, Semester, User/Role admin, Quote, Session, Province/Commune | Chưa port | Ngoài phạm vi "quản lý CLB" của các batch; cần một batch central-admin riêng |
| Vai trò CLB tuỳ biến + `is_leader` | Dùng enum Admin/Faculty/Student | Tránh đổi schema membership giữa dòng; cán bộ = Admin/Faculty |
| Đăng ký tài khoản bằng token CLB | Chưa port | Cần chống lạm dụng/throttle + quy trình duyệt; chưa nằm trong acceptance |
| Mạo danh (impersonation) | Không port | Rủi ro bảo mật cao; không cần cho vận hành CLB |
| API mobile/API key (`Club/ApiKeyController`) | Chưa port | Không có bề mặt API di động trong bản .NET |
| Bốc thăm: JS vòng quay + API công khai ghi kết quả | Trang công khai chỉ đọc | Quay số thực hiện bởi nhân viên có quyền; tránh API ghi ẩn danh |
| Theme editor đầy đủ (layout width, menu style, logo) | Light/dark + màu chính | Giữ phạm vi cấu hình gọn, không cần lưu nhiều theme |
| Short URL QR download + chặn iOS | Chưa port | QR cần package; chặn iOS là middleware đặc thù PHP |

## Thay đổi so với bước 2/3

| Trước | Sau |
|---|---|
| `Member` sample (bước 2) | `Student` thật (bước 3) |
| `CsvStudentRepository` tự viết | `CsvRepository<T>` dùng chung (LSP) |
| 60 test | **405 test** |
| Dashboard đếm sinh viên | Dashboard đếm sinh viên/học phần/đăng ký/điểm |
| Phân quyền chỉ theo role | Ma trận module/action + policy động |
| Chỉ Admin/Faculty dùng hệ thống | Student có hồ sơ riêng + chuyển CLB |
| Dữ liệu CSV trên đĩa | **SQL Server** (mặc định) qua `IRowStore`; CSV còn là chế độ phụ |

## SQL Server (bản nộp chính)

| Hạng mục | Nội dung |
|---|---|
| Backend | `SqlRowStore` trên `Microsoft.Data.SqlClient` 5.1.6 (ADO.NET thuần, không EF) |
| Chọn backend | `Storage:Provider` = `SqlServer` (mặc định) hoặc `Csv`; `RowStoreFactory` là chỗ duy nhất quyết định |
| Schema | App tự tạo: `CREATE SCHEMA [sims]` + mỗi bảng `(Id INT IDENTITY PK, RowNo INT, Ord INT, Value NVARCHAR(MAX))` + unique index `IX_{Table}_RowOrd` trên `([RowNo],[Ord])`; 34 bảng sau seed, 38 mapping khai báo |
| Tên bảng | `SqlRowStore.IsValidTableName` — `^[A-Za-z][A-Za-z0-9_]{0,99}$`, kiểm trong ctor `SqlRowStore` **và** `RowStoreFactory.Create` (nhánh CSV ghép tên vào đường dẫn file nên `../../x` sẽ thoát thư mục). Tên không tham số hoá được trong SQL Server nên đây là hàng rào duy nhất. |
| Giao dịch | `SERIALIZABLE` + `sp_getapplock` (Exclusive, Transaction, timeout 15 s) theo tên bảng; `AppendAsync` cũng khoá để `MAX(RowNo)+1` và INSERT nguyên tử |
| DDL đồng thời | `EnsureTableAsync` khoá `sp_getapplock` owner `Session` (`sims:ddl:<bảng>`) + `TRY/CATCH` bỏ qua lỗi 1913. Không khoá thì hai request cùng tạo index và nổ "index already exists" — **đã xảy ra thật**, test `Concurrent_appends_do_not_mix_cells` bắt được |
| Trạng thái khởi tạo | `InitializedConnections` (`HashSet<string>` theo connection string) chứ không phải `static bool`: một DB lỗi không chặn tạo schema cho DB khác |
| Vẫn còn trên đĩa | `data/files/` (tệp đính kèm) và `data/students-invalid.csv` (file mẫu import) |
| Test | `dotnet test` chạy CSV trong thư mục tạm; `SqlRowStoreTests` tự bỏ qua khi máy không có SQL Server, đặt `SIMS_SQL_CONNECTION` để trỏ engine khác |
| Môi trường đã kiểm | SQL Server 2019 RTM 15.0.2000.5 Developer, Windows auth, DB `SIMS_APDP` (đổi tên từ `SIMS` ngày 2026-11-01 để khỏi trùng DB `SIMS` của dự án khác trên LocalDB) — **chưa có CU, chỉ dùng dev**, không dùng cho production |

## UI vòng 1 (V1) — design system, app shell, dashboard

Mục tiêu: giao diện SaaS hiện đại kiểu Linear/Notion, sạch/phẳng/nhiều whitespace, `#F26522`
**chỉ** làm màu nhấn trên bảng neutral; giữ nguyên route, nghiệp vụ, phân quyền và SQL Server.

| Hạng mục | Nội dung |
|---|---|
| Hai lớp CSS | `site.css` = token + app shell + dashboard (thuần CSS thuần, không phụ thuộc Bootstrap); `theme.css` = lớp bắc cầu Bootstrap + component dashboard. Không xoá Bootstrap, không thêm Sass/build step |
| Token | Neutral scale `--n-0…--n-950`, surface/border/text, `--accent #f26522` (light) và `#ff7a3d` (dark để đủ tương phản), radius 6/8/12/16, shadow 4 cấp, `--sidebar-w: 260px`, `--topbar-h: 56px`, `--content-max: 1320px` |
| Theme | `data-theme` **và** `data-bs-theme` trên `<html>`; script inline trước khi vẽ đọc `localStorage['sims-theme']` để không nháy trắng; `site.theme` từ DB là fallback; 3 nút đổi theme (sidebar, topbar, user menu) |
| Accent từ cài đặt | `_Layout.cshtml` đọc `site.primary_color` nhưng **bỏ qua giá trị `#0d6efd`** (mặc định cũ trong seed) để `#F26522` giữ đúng vai trò. Màu khác do Admin đặt vẫn được áp |
| Shell | `.app-shell` grid; sidebar sticky 260px với nhóm menu theo quyền; topbar sticky + backdrop blur; content max 1320px; footer; breakpoint 1023px → sidebar off-canvas + scrim |
| Điều hướng | Command palette `Ctrl+K` / `/`, lọc theo tên trang, ↑↓/Enter/Esc, `aria-activedescendant`; menu người dùng có focus trap; skip link `#main-content` |
| Dashboard | Chào theo buổi + tên + vai trò; 4 KPI (sinh viên, học phần, đăng ký đang học, CLB kèm số sự kiện sắp tới); panel sự kiện sắp tới (empty state khi trống); meter tỷ lệ nhập điểm; thao tác nhanh; panel hệ thống. Student thấy dashboard rút gọn (không lộ số liệu toàn trường) |
| Dữ liệu | `DashboardSummary` thêm `Clubs`, `UpcomingEvents`, `NextEvents` (tối đa 5 sự kiện Published chưa kết thúc); `HomeController` thêm `INotificationService` + `IUserRepository` để hiện số thông báo chưa đọc. Không thêm truy vấn riêng — dùng lại `ListAsync` sẵn có |
| Bootstrap 5.1 | Bản trong `wwwroot/lib` là **5.1**, chưa có `--bs-btn-*` / `--bs-table-*` (chỉ từ 5.2). Vì vậy `theme.css` đặt thuộc tính trực tiếp (`background`, `color`, `border-color`) thay vì biến — đây là lỗi thật đã gặp: bảng chữ đen trên nền tối, nút primary vẫn xanh |
| Font | Inter (Google Fonts) + fallback `Segoe UI, system-ui`; `font-feature-settings` bật `tnum` cho số liệu |

Lỗi thật gặp và cách vá trong vòng này (chi tiết ở `docs/test-results.txt`):
`@:` + `@item.Label` rò mã định danh Razor vào nhãn nhóm; biến `--bs-*` của Bootstrap 5.2 không tồn tại
ở 5.1; `site.primary_color = #0d6efd` đè accent; focus ring bị `.btn:focus` của Bootstrap che.

## UI vòng 2 + 3 (V2/V3) — component chung cho mọi module, polish

**V2 (2026-11-01):** viết lại *toàn bộ* view Razor theo lớp component chung trong `site.css`,
không đổi controller/route/phân quyền:

| Hạng mục | Nội dung |
|---|---|
| List | `section.section` + `.toolbar` (search, filter, actions) + `.table-shell` + `.pager`; khai báo `[data-table]`/`[data-table-search]`/`th[data-sort]`/`[data-table-pager]`/`[data-table-count]` → `site.js initTable` loai lọc + sort + phân trang phía client, **không** JS riêng từng trang. Wrapper `[data-table]` đặt trên `<section>`, không đặt trên `<table>` |
| Empty state | List rỗng ẩn toolbar/pager, render partial `_EmptyState`; `_TableToolbar`/`_TableFoot` dùng chung |
| Form | `.form-grid` (2 cột, tự xếp 1 cột < 720px) + `.form-actions`; `.check-grid`/`.check-card` cho Ma trận quyền |
| Detail | `.detail-list` (dl 2 cột) + `.stat-grid` KPI; hoá đơn/club/student/event cùng khuân mẫu |
| Hộp thư | `.notice` + `.notice--unread` + `.list-stack`; unread ẩn trong tiêu đề panel + badge topbar |
| Đăng nhập | `.auth` hai cột (panel brand + form), heading giữ nguyên chuỗi `Đăng nhập SIMS` (test Assert) |
| 403/500 | `.state-page` (icon + mã + hành động); `AccessDenied` và `Error` dựng lại |
| Công khai | `.public-hero` + `.draw-grid`/`.draw-card` cho `PublicHome`; `LuckyDrawPublic/Show` vẫn `Layout = null` nhưng CSS tự chứa, nền tối, form mã bảo mật + refresh 30s |
| Theme/Badge | `.badge--ok/warn/danger/info/neutral/accent` thay `badge bg-*` Bootstrap |

**V3 (cùng ngày):** hồi quy + polish. Quét 27 endpoint (`SMOKE 26/27`, FAIL duy nhất = `/health`
text thuần không layout — đúng thiết kế). Playwright: mobile 390px `MOBILE_OVERFLOW_PX=0`;
focus ring `2px solid`; bảng `/Students` lọc thật (`rows=2 filtered=1 count="1 / 2 mục"`);
403 qua `student1 → /Settings/Global` đúng luồng AccessDenied; dark mode 6 trang.
Vá: skip-link từ inline style + `onfocus/onblur` sang class `.skip-link` trong `site.css`;
`Home/Index.cshtml` bỏ `table-responsive` + inline style cuối cùng → `.table-shell`.
Hai 404 thấy trong log ảnh là `favicon.ico` + GUID mẫu trong script chụp (id thật là GUID —
`Details(Guid id)`), không phải lỗi app; đã chụp lại bằng id thật.

Kết luận vòng 1–3: `dotnet build -warnaserror` 0/0, `dotnet test` 405/405, 44 ảnh
`docs/screens/v2-*.png` + `v3-*.png`, output thật ở `docs/test-results.txt`.

## Chức năng PHP ngoài danh sách yêu cầu (chưa port)

Xem **Ma trận gap còn lại** ở Batch 12 phía trên — các hạng mục còn lại đều kèm lý do
(package, phạm vi central-admin, bảo mật hoặc hạ tầng mail). Bản kiểm chứng từng hạng mục:
`docs/parity-closure.md`.

## Giới hạn đã biết

1. **Mỗi thao tác ghi đọc–ghi cả bảng** (mô hình ô key/value): `UPDATE` = đọc toàn bảng → xoá → insert lại,
   và mỗi bản ghi ghi ra một dòng cho mỗi ô. Đúng ngữ nghĩa nhưng O(n) — ổn cho quy mô CLB
   (hàng nghìn bản ghi), **không phải thiết kế cho dữ liệu lớn**. Nâng cấp: thêm bảng có cột thật +
   repository riêng cho bảng nóng (students, attendance, invoices), giữ `IRowStore` cho phần còn lại.
2. Không có index theo cột nghiệp vụ — tìm theo MSSV/token là quét bảng. Khoá chỉ trong phạm vi một
   SQL instance (`sp_getapplock`); nhiều instance trỏ chung DB vẫn nhất quán nhờ transaction `SERIALIZABLE`,
   nhưng chưa test tải song song nhiều app.
3. Vai trò `Student` chỉ xem hồ sơ của mình; chưa có luồng đăng ký môn tự phục vụ.
4. Ma trận quyền mới phủ 20 module đã port (dashboard, students, courses, enrolments, clubs, learning, events, checkin, event_plans, invoices, expenses, documents, discipline, awards, lucky_draws, notifications, settings, short_urls, feature_requests, logs);
   các module tương lai cần bổ sung vào `PermissionCatalog`.
5. **Lọc theo CLB**: sinh viên chưa gán CLB (`ClubId = null`) hiển thị cho mọi CLB — cần quy trình gán CLB
   khi import dữ liệu cũ; đây là quyết định có chủ đích để không mất dữ liệu chưa phân loại.
6. UI đã làm lại **cả 3 vòng V1–V3**: token + app shell + dashboard + toàn bộ module theo component
   chung (bảng lọc/sort/phân trang client, form-grid, detail-list, empty/403/500 state).
   Chưa có: modal/drawer dạng component tập trung (dùng chuyển trang), drag-rows, virtualized table.
7. Import CSV chưa hỗ trợ trường nhiều dòng trong dấu ngoặc kép (mỗi bản ghi 1 dòng).
8. Chưa có test tải/nhiều tiến trình; export Excel có giới hạn 50.000 dòng × 200 cột và chỉ đọc sheet đầu tiên khi import.
9. **Tài chính nhiều file**: hoá đơn, giao dịch và chi phí nằm ở 3 file CSV; ghi thanh toán (thêm giao dịch + đổi trạng thái hoá đơn) không phải một giao dịch nguyên tử xuyên file. Khoá chỉ đảm bảo từng file, phù hợp quy mô nhỏ.
10. **Kỳ thu là chuỗi tự do** (chưa port module học kỳ), nên không có ràng buộc khoá ngoại hay danh mục kỳ.
11. **Đối soát cổng thanh toán**: đã có webhook SePay (khoá tĩnh, không HMAC); chưa có PayOS và chưa tự khớp sao kê ngân hàng — các khoản khác do người dùng ghi nhận.
12. **Tệp đính kèm** lưu cục bộ trong `data/files` (không có sao lưu, quét virus, phiên bản tài liệu hay xem trước); giới hạn 10 MB và danh sách đuôi cho phép.
13. **Điểm trường không tách theo học kỳ**: vì chưa port module học kỳ, điểm tổng hợp trên toàn bộ bản ghi hiệu lực và `period` chỉ là nhãn; công thức xếp hạng giả định một tập thành viên duy nhất.
14. **Cán bộ CLB** được suy ra từ vai trò Admin/Faculty của membership (bản PHP dùng thuộc tính `role.is_leader` chưa có trong mô hình .NET).
15. **Trang bốc thăm công khai chỉ đọc**: việc quay số do nhân viên thực hiện; PHP cho phép API công khai tự ghi giải/người tham gia/kết quả — bản C# không mở các thao tác ghi ẩn danh.
16. **Thông báo chỉ in-app**: chưa gửi email/SMTP, chưa đính kèm ảnh/tệp như PHP.
17. **Theme giới hạn** ở light/dark + màu chủ đạo; chưa có theme editor đầy đủ (layout width, menu style, logo…).
18. **Session xác thực mã bảo mật là in-memory**: khởi động lại ứng dụng hoặc chạy nhiều instance sẽ mất trạng thái đã xác thực (dữ liệu nghiệp vụ vẫn trong CSV).
19. **SePay webhook** dùng khoá tĩnh trong `settings.csv`, không kiểm tra chữ ký HMAC (SePay dùng `Authorization: Apikey`); cần đặt khoá đủ mạnh và chỉ mở endpoint qua HTTPS.
20. **Bảo trì** đọc cấu hình từ CSV mỗi request (không cache). **Short URL** ghi một dòng mỗi lượt truy cập — file có thể tăng nhanh với lưu lượng lớn.
21. **Hồ sơ** chưa hỗ trợ ảnh đại diện; **đề xuất tính năng** chưa có đính kèm/bình luận.
