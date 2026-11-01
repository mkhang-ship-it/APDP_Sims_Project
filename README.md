# SIMS — ASP.NET Core (port: Auth · Students · Courses · Enrolments · Grades · Import · Permissions · Clubs · Events · Check-in · Event Plans · Finance · Documents · Discipline · Awards · Lucky Draw · Public · Notifications · Settings · Short URLs · Profile · Feature Requests · Ops)

**Đường dẫn output:** `C:/Users/ADMIN/orca/workspaces/tezonew/SIMS-dotnet`
**Giai đoạn:** Bước 4/4 — hoàn tất phạm vi đã chốt.

Source PHP gốc (`opencode-implementation`) **không bị sửa**; output độc lập, **không commit**.

## SDK

| Mục | Giá trị |
|---|---|
| Target framework | `net8.0` |
| SDK dùng để build/test | **8.0.412** (pin trong `global.json`) |
| SDK tối thiểu | 8.0.x |

## Cấu trúc

```
SIMS.sln · global.json · README.md · .gitignore
docs/
  traceability.md        # yêu cầu → file:line → test
  php-to-csharp-map.md   # map module PHP sang C#
  porting-notes.md       # quyết định, phạm vi, chức năng PHP chưa port
  parity-closure.md      # đối chiếu gap với bản PHP
  test-results.txt       # output build/test thật
  screens/               # ảnh chụp UI (3 breakpoint × light/dark)
src/
  SIMS.Domain/           # Student, Course, Enrolment, User, Role (không IO/HTTP)
  SIMS.Application/      # Result<T>, Validation, use case, Strategy, Decorator, import
  SIMS.Infrastructure/   # IRowStore (SQL Server / CSV / in-memory), repository, PBKDF2, seed
  SIMS.Web/              # MVC + cookie auth + DI + /health
tests/
  SIMS.Tests/            # xUnit: Domain, Application, Infrastructure, Architecture, Web
data/                    # (runtime) tệp đính kèm + file mẫu import (dữ liệu nghiệp vụ ở SQL Server)
```

## Lưu trữ: SQL Server (mặc định)

Dữ liệu nghiệp vụ nằm trong SQL Server; CSV chỉ còn là chế độ phụ để chạy khi không có engine.

```jsonc
"Storage": {
  "Provider": "SqlServer",                       // hoặc "Csv" để quay lại file CSV
  "ConnectionString": "Server=localhost;Database=SIMS_APDP;Integrated Security=True;TrustServerCertificate=True;Encrypt=False",
  "DataDirectory": "data"                        // tệp đính kèm + file mẫu import
}
```

- Mọi repository đọc/ghi qua `IRowStore` (`SqlRowStore`, `CsvFileStore`, `InMemoryRowStore`) — logic nghiệp vụ
  không biết engine nào bên dưới. Chỉ `RowStoreFactory` quyết định, theo `Storage:Provider`.
- Schema do app tự tạo khi khởi động: schema `sims`, mỗi bảng (38 bảng) là
  `[sims].[<tên>](Id INT IDENTITY PK, RowNo INT, Ord INT, Value NVARCHAR(MAX))` — lưu ô theo (RowNo, Ord),
  kèm unique index `IX_{Table}_RowOrd` trên `([RowNo],[Ord])`.
- Ghi nguyên tử: transaction `SERIALIZABLE` + `sp_getapplock` theo bảng, giữ đúng ngữ nghĩa
  read-modify-write của bản CSV; `AppendAsync` khoá cùng cách để `MAX(RowNo)+1` không trùng.
- Tên bảng được kiểm bằng `SqlRowStore.IsValidTableName` (`^[A-Za-z][A-Za-z0-9_]{0,99}$`) trước khi vào SQL
  **và** trước khi ghép đường dẫn CSV — chặn injection và thoát thư mục (`../../x`).
- Driver ADO.NET thuần (`Microsoft.Data.SqlClient`); không EF/ORM.
- Chạy lại bằng CSV (không cần SQL Server): đặt `Storage:Provider=Csv` hoặc biến môi trường
  `Storage__Provider=Csv`. Test tự dùng CSV trong thư mục tạm.

## Chức năng

| Module | Nội dung |
|---|---|
| **Auth** | Cookie auth (không Identity/JWT/OAuth), PBKDF2-SHA256 100k vòng/salt 16B, 3 vai trò, 403, menu theo role |
| **Students** | CRUD, MSSV duy nhất + không đổi, email/ngày sinh/chương trình, import CSV/Excel streaming |
| **Courses** | Tạo/sửa/xoá, sức chứa 1..200, học phần tiên quyết |
| **Enrolments** | Chống trùng, sức chứa (atomic trong file lock), trạng thái, tiên quyết, huỷ |
| **Grades** | Điểm 0..10 + xếp loại theo Strategy (A≥9 … F<5) |
| **Import CSV/Excel** | Streaming, lỗi theo Row N, dòng lỗi không hỏng cả file, audit tổng kết; XLSX bằng BCL (không package) |
| **Dashboard** | Sinh viên / học phần / đăng ký đang học / CLB + sự kiện sắp tới, tỷ lệ nhập điểm, thông báo chưa đọc |
| **Audit** | `data/audit.csv` cho mọi thao tác ghi + đăng nhập |
| **Permissions** | Ma trận role × module × action từ `permissions.csv`, policy động `perm:module:action`, `[RequirePermission]` ở controller + menu |
| **Clubs / multi-club** | CRUD CLB, thành viên theo CLB với vai trò riêng, chuyển CLB hiện tại (claim `club_id`), lọc sinh viên theo CLB |
| **My Learning** | Vai trò Student xem hồ sơ + đăng ký + điểm của chính mình (liên kết Student↔User) |
| **Events** | Tạo/sửa sự kiện, trạng thái nháp → công bố → hoàn thành/huỷ, sức chứa, mã check-in, danh sách + lịch tháng |
| **Check-in QR/token** | Token 12 ký tự + link `/Checkin?code=…`; quét QR bằng BarcodeDetector (fallback nhập mã); chống trùng + sức chứa atomic; geofence 5 km theo toạ độ địa điểm tổ chức (`navigator.geolocation`); audit |
| **Event Plans** | Kế hoạch theo sự kiện: tóm tắt + 7 trường mẫu PHP (người lập, ngày triển khai, người duyệt, đối tượng, mô tả, đơn vị chủ trì, cố vấn), mục tiêu, hành động, ngân sách (tổng tự tính), thiết bị, timeline; export CSV + XLSX |
| **Invoices** | Hoá đơn theo thành viên (`INV-{năm}-{6 số}`), tạo hàng loạt, thanh toán từng phần tự chuyển "đã thanh toán", cập nhật quá hạn, phiếu in HTML |
| **Expenses** | Đề nghị chi của CLB: duyệt/từ chối, đánh dấu đã chi (tự ghi giao dịch chi), thành viên gửi/sửa/xoá yêu cầu của mình |
| **Documents** | Loại văn bản, tải tệp thật (PDF/Word/ảnh/TXT ≤10 MB), duyệt/từ chối, public/private/restricted + chia sẻ, yêu cầu nộp và nộp theo yêu cầu |
| **Discipline / Merits** | Danh mục điểm cộng/trừ, áp dụng/thu hồi bản ghi, điểm CLB, điểm trường (scale 5 + 5 cán bộ), xếp hạng, "Điểm của tôi", export CSV |
| **Awards** | Giải thưởng `BK-######`, trạng thái chờ trao/đã trao/thu hồi, người nhận + chứng nhận đính kèm |
| **Lucky Draw** | Chương trình bốc thăm có slug công khai, mã bảo mật, giải thưởng, quay số bằng RNG, lịch sử người trúng |
| **Public** | Trang chủ công khai (CLB, sự kiện đang diễn ra, chương trình bốc thăm đang mở) + trang `/lucky/{slug}` không cần đăng nhập |
| **Notifications** | Thông báo toàn bộ CLB hoặc CLB cụ thể, hộp thư, đếm chưa đọc, đánh dấu đã đọc |
| **Settings** | Cấu hình toàn hệ thống (tên/mô tả/theme/màu/email) và theo CLB (liên hệ/địa chỉ/ngân hàng), áp vào layout |
| **Short URLs** | Link rút gọn `/s/{code}`, hết hạn/tạm dừng, đếm lượt truy cập |
| **Profile** | Tự sửa tên/email và đổi mật khẩu (xác minh mật khẩu hiện tại) |
| **Event images** | Upload/xem/xoá ảnh JPG/PNG/WEBP trong chi tiết sự kiện |
| **Payments (SePay)** | Webhook có `Apikey`, ghi thanh toán hoá đơn theo mã, idempotent |
| **Ops** | Bảo trì (503 cho khách, Admin đi qua), nhật ký audit có lọc, xuất CSV/XLSX sinh viên |
| **Feature requests** | Thành viên gửi đề xuất, nhân viên đổi trạng thái/phản hồi |

## Ràng buộc đã áp dụng

| Quy tắc | Thực thi |
|---|---|
Domain không tham chiếu project, không `System.IO`/`System.Net.Http` | csproj + `Architecture/DomainIsolationTests` |
Không EF/ORM, không Identity/JWT/OAuth | chỉ ADO.NET (`Microsoft.Data.SqlClient`) ở Infrastructure |
Ghi nguyên tử, khoá theo bảng (SQL applock / CSV file lock) | `SqlRowStore`, `CsvFileStore` + test đồng thời |
`Result<T>` + validation tách riêng | `Application/Common`, `Application/Validation` |
SOLID: OCP parser, LSP `IRowStore`/`IRepository<T>`, ISP reader/writer, DIP wiring | `Program.cs`, `Abstractions`, `Persistence` |
Patterns: Factory, Strategy (grade/prerequisite), Repository, Decorator (audit) | `Domain` factory, `Application/Grades`, `Courses/AuditedCourseService`, `Students/Import` |
Nullable + ImplicitUsings | bật toàn bộ project |

## Package

Một package runtime duy nhất cho lưu trữ SQL Server, còn lại là package test:

| Package | Project |
|---|---|
`Microsoft.Data.SqlClient` 5.1.6 | SIMS.Infrastructure (SQL Server, ADO.NET) |
`Microsoft.NET.Test.Sdk` 17.8.0 · `xunit` 2.5.3 · `xunit.runner.visualstudio` 2.5.3 · `coverlet.collector` 6.0.0 | SIMS.Tests |
`Microsoft.AspNetCore.Mvc.Testing` 8.0.21 | SIMS.Tests (WebApplicationFactory) |

## Lệnh

```powershell
cd C:/Users/ADMIN/orca/workspaces/tezonew/SIMS-dotnet

dotnet build SIMS.sln -warnaserror
dotnet test  SIMS.sln

dotnet run --project src/SIMS.Web/SIMS.Web.csproj --urls http://127.0.0.1:5097
# http://127.0.0.1:5097  ·  /health
```

## Tài khoản demo

| Username | Password | Role |
|---|---|---|
`admin` | `Admin@12345` | Admin |
`faculty1` | `Faculty@12345` | Faculty |
`student1` | `Student@12345` | Student (chỉ xem trang chủ) |

Seed chạy một lần khi `data/users.csv` rỗng. `student1` được liên kết với hồ sơ **SV0001** và thuộc 2 CLB
(**CLB01**, **CLB02**) để thử multi-club; `faculty1` thuộc **CLB01**.

## Dữ liệu: bảng và cột

Cùng một danh sách cột cho cả hai chế độ — SQL Server (`[sims].[<tên>]`, cột `RowNo`/`Ord`) và
CSV (`data/<tên>.csv`, header ở dòng đầu).

| File | Nội dung |
|---|---|
`users.csv` | `id,username,display_name,role,password_hash,is_active,created_at_utc` |
`students.csv` | `id,student_code,full_name,email,date_of_birth,program,status,user_id,club_id,created_at_utc` |
`courses.csv` | `id,code,title,capacity,prerequisite_code,status,created_at_utc` |
`enrolments.csv` | `id,student_id,course_id,status,enrolled_at_utc,score,grade_letter` |
`clubs.csv` | `id,code,name,is_active,created_at_utc` |
`club_memberships.csv` | `id,user_id,club_id,role,is_active,joined_at_utc` |
`permissions.csv` | `role,module,action` |
`events.csv` | `id,code,title,description,starts_at_utc,ends_at_utc,location,capacity,status,club_id,checkin_token,created_at_utc,venue_latitude,venue_longitude` |
`attendance.csv` | `id,event_id,student_id,method,checked_in_at_utc,latitude,longitude,distance_km` |
`event_plans.csv` | `id,event_id,summary,created_at_utc,updated_at_utc,prepared_by,implementation_date,approved_by,participants,description,responsible_organizer,advisor_coordinator` |
`event_plan_objectives.csv` | `id,event_plan_id,content,order` |
`event_plan_actions.csv` | `id,event_plan_id,content,owner,due_date,order` |
`event_plan_budget_items.csv` | `id,event_plan_id,item_name,quantity,unit_price,order` |
`event_plan_equipment.csv` | `id,event_plan_id,name,quantity,note,order` |
`event_plan_timeline.csv` | `id,event_plan_id,time_label,activity,order` |
`invoices.csv` | `id,invoice_code,user_id,club_id,name,total_amount,due_date,status,period,note,created_at_utc,updated_at_utc` |
`transactions.csv` | `id,invoice_id,expense_id,type,paid_amount,payment_date,payment_method,note,club_id,created_at_utc` |
`expenses.csv` | `id,name,amount,requested_by,approved_by,approved_at_utc,requested_at_utc,status,club_id,period,description,created_at_utc,updated_at_utc` |
`document_types.csv` | `id,club_id,code,name,description,template_url,is_active,created_at_utc` |
`documents.csv` | `id,club_id,document_type_id,uploaded_by,title,file_path,visibility,status,reason_rejected,reviewed_by,reviewed_at_utc,created_at_utc` |
`document_access.csv` | `id,document_id,user_id` |
`document_requests.csv` | `id,club_id,document_type_id,requested_by,deadline,created_at_utc` |
`document_request_users.csv` | `id,document_request_id,user_id,document_id,submitted_at_utc` |
`disciplines.csv` | `id,club_id,code,name,points,description,is_active,created_at_utc` |
`discipline_records.csv` | `id,user_id,club_id,discipline_id,applied_date,period,status,applied_by,notes,created_at_utc` |
`awards.csv` | `id,club_id,code,title,period,award_date,status,notes,created_at_utc` |
`award_recipients.csv` | `id,award_id,user_id,attachment,created_at_utc` |
`lucky_draws.csv` | `id,club_id,title,slug,security_code,participants,start_at_utc,end_at_utc,is_active,created_by,created_at_utc` |
`lucky_prizes.csv` | `id,lucky_draw_id,name,quantity,order,is_special` |
`lucky_winners.csv` | `id,lucky_draw_id,participant,prize_name,won_at_utc` |
`notifications.csv` | `id,title,content,target_type,sender,is_active,created_at_utc,updated_at_utc` |
`notification_targets.csv` | `id,notification_id,club_id` |
`notification_reads.csv` | `id,notification_id,user_id,read_at_utc` |
`settings.csv` | `id,club_id,key,value,updated_at_utc` (club_id rỗng = cấu hình toàn hệ thống) |
`short_urls.csv` | `id,club_id,code,target_url,title,created_by,expires_at_utc,is_active,created_at_utc` |
`short_url_visits.csv` | `id,short_url_id,visited_at_utc,ip_address,user_agent` |
`event_images.csv` | `id,event_id,file_path,caption,uploaded_by,created_at_utc` |
`feature_requests.csv` | `id,club_id,title,content,created_by,status,response,created_at_utc,updated_at_utc` |
`files/` | tệp đính kèm (văn bản, chứng nhận) — ngoài CSV, tên do hệ thống sinh |
`audit.csv` | `timestamp_utc,actor,action,entity,entity_id,details` (append-only) |
`students-invalid.csv` | file mẫu có dòng lỗi (UTF-8 no BOM) để thử import |

## Giao diện (UI vòng 1–3)

Redesign frontend theo hướng SaaS hiện đại (Linear/Notion): nền neutral, `#F26522` chỉ làm màu nhấn.

| Hạng mục | Nội dung |
|---|---|
| CSS | `wwwroot/css/site.css` = design token + app shell + dashboard; `wwwroot/css/theme.css` = lớp bắc cầu Bootstrap 5.1 + component dashboard. Không Sass, không build step |
| Shell | Sidebar 260px (menu theo quyền, chia nhóm), topbar sticky + blur, content tối đa 1320px, footer; dưới 1024px sidebar thành off-canvas + scrim |
| Điều hướng nhanh | Command palette `Ctrl+K` hoặc `/` lọc theo tên trang; user menu có focus trap; skip link `#main-content` |
| Dashboard | Chào theo buổi, 4 KPI (sinh viên · học phần · đăng ký · CLB), sự kiện sắp tới, tỷ lệ nhập điểm, thao tác nhanh, panel hệ thống — dữ liệu thật từ SQL Server |
| Light/dark | `data-theme` + `data-bs-theme`, lưu `localStorage['sims-theme']`, script pre-paint chống nháy trắng; `site.theme` trong DB là fallback |
| Accent | `#F26522` (light) / `#FF7A3D` (dark). `site.primary_color` chỉ được áp khi khác mặc định cũ `#0d6efd` |
| Accessibility | `:focus-visible` 2px accent, skip link, `aria-expanded`/`aria-activedescendant`, focus trap, `role="status"` cho alert, `prefers-reduced-motion` |

### UI vòng 2 + 3 — toàn bộ module theo component chung

Toàn bộ view Razor viết lại theo component V2 trong `site.css`: `.section` + `.toolbar` (search
khai báo `[data-table-search]`, lọc/sắp xếp/phân trang chạy phía trình duyệt qua `site.js`),
`.table-shell`, `.pager`, badge variants, `.form-grid`, `.detail-list`, `.stat-grid`, `.notice`
(hộp thư), `.auth` (đăng nhập hai cột), `.state-page` (403/500), `.public-hero` + `.draw-card`
(trang công khai + bốc thăm), `.skip-link`. Empty state (`_EmptyState`), rỗng → ẩn toolbar/pager.
V3: skip-link về CSS class, `table-responsive` cuối cùng thay bằng `.table-shell`, kiểm 390px
(tràn ngang 0px), dark mode, focus ring, 403, bảng lọc/sort thật.

Ảnh chụp 3 breakpoint × 2 theme (desktop 1440, tablet 834, mobile 390): [`docs/screens/`](docs/screens)
(`v1-*` 20 tệp, `v2-*`/`v3-*` 44 tệp).
Chi tiết lỗi thật gặp và cách vá: [`docs/porting-notes.md`](docs/porting-notes.md), [`docs/test-results.txt`](docs/test-results.txt).

## Kiểm chứng

| Lệnh | Kết quả |
|---|---|
`dotnet build SIMS.sln -warnaserror` | **Build succeeded — 0 Warning(s), 0 Error(s)** |
`dotnet test SIMS.sln` | **405/405 passed** (Domain 98 · Application 155 · Infrastructure 84 · Architecture 3 · Web 65) |
Runtime smoke | `/health` 200; login `faculty1`; `/Courses` có CS101/CS102; `/Enrolments` có SV0001; dashboard 200; seed tạo 9 file CSV (invalid sample **no BOM**) |
| Smoke batch 5 | `student1` → `/MyLearning` 200 (SV0001, 8.5, B), `/Students` 302; menu chỉ có "Học tập của tôi"; chuyển CLB 302 và áp dụng; admin tạo CLB + gán thành viên; faculty bị chặn `/Clubs/Create` (302) |
| Smoke batch 6 | `/Events` 200 (EVT001 published, EVT002 draft), `/Events/Calendar` 200, chi tiết có token 12 ký tự + link check-in; `student1` check-in 302 → hiện SV0001 trong danh sách; check-in lần 2 báo "đã check-in"; Student `/Events` 200 nhưng `/Events/Create` 302; seed tạo 11 file CSV |
| Smoke batch 7 | `/EventPlans?eventId=…` 200 với kế hoạch mẫu (mục tiêu, ngân sách "Nước uống"); thêm mục tiêu qua POST 302 → hiện ngay; `/EventPlans/Export` 200 `text/csv`, BOM UTF-8, có dòng `objective,…`; trang chi tiết sự kiện có link "Kế hoạch"; `student1` 302 → `/Account/AccessDenied` (403); khách 302 → `/Account/Login`; eventId lạ 404; seed tạo **16 file CSV** |
| Smoke batch 8 | `/Invoices` 200 với hoá đơn mẫu `INV-…` (200.000 đ, đã thu 50.000 đ), chi tiết + phiếu in 200; tạo hoá đơn qua POST 302 → thu 40.000 đ (vẫn "Chờ thanh toán") → thu 60.000 đ → "Đã thanh toán"; `/Expenses` 200, tạo chi phí 70.000 đ (duyệt sẵn) → "Đã chi"; `student1` `/Invoices` 302 AccessDenied nhưng `/Invoices/MyInvoices` 200; gửi yêu cầu chi 30.000 đ → faculty duyệt 302 và hiện "Đã duyệt"; seed tạo **19 file CSV** |
| Smoke batch 9 | `/Documents` 200 với văn bản mẫu, tải về 200 (68 B); faculty tải văn bản mới (302) → duyệt (302) → "Đã duyệt"; tạo yêu cầu nộp cho `student1`; `student1` `/Documents` 302 AccessDenied nhưng `/Documents/My` 200, tải văn bản riêng 302, `/Documents/Accessible` 200 thấy văn bản công khai, nộp bài theo yêu cầu (302) → faculty thấy "đã nộp"; `/Disciplines` + `/Disciplines/Records` + `/Disciplines/Summary` 200, export CSV 200 có `school_points`/`student1`, `/Disciplines/MyPoints` 200; `/Awards` + chi tiết 200, thêm người nhận 302 → "Người nhận (2)"; seed tạo **27 file CSV + 4 tệp trong `data/files`** |
| Smoke batch 10 | Khách `/` 200 thấy trang công khai (sự kiện "Ngày hội công nghệ", CLB01, chương trình bốc thăm); `/lucky/{slug}` đòi mã, nhập sai báo lỗi, nhập `123456` 302 → ẩn form, hiện mục người trúng; faculty `/LuckyDraws` 200, quay số 302 → "Người trúng thưởng (1)"; tạo thông báo 302; `student1` `/Notifications` 302 AccessDenied nhưng `/Notifications/Inbox` 200, đánh dấu tất cả → "0 chưa đọc"; admin `/Settings` 200, đổi tên site/theme dark/màu `#005577` → trang công khai cập nhật; faculty lưu cấu hình CLB 302; seed **34 file CSV** |
| Smoke batch 11 | Tạo short URL 302 → khách `/s/{code}` 302 đúng đích; upload ảnh sự kiện 302 → hiện trong chi tiết; admin lưu khoá SePay → webhook 200, hoá đơn chuyển "Đã thanh toán"; `/Logs` 200 có `auth.login`; bật bảo trì → khách `/` 503 kèm thông báo, Admin `/` 200; tắt → khách `/` 200; seed **35 file CSV + 2 tệp** |
| Smoke batch 12 | `/Students/Export?format=xlsx` 200 `application/vnd.openxmlformats-…sheet` 1817 B, zip có đúng 5 entry; import lại chính file → "Kết quả", 1 dòng dữ liệu; tạo sự kiện có toạ độ → chi tiết hiện "geofence 5 km"; check-in từ Hà Nội bị từ chối ("quá xa"), check-in trong 5 km thành công và lưu `distance_km`; lưu thông tin kế hoạch ("Ban truyền thông", "Cố vấn học tập") hiện ngay; `/EventPlans/Export?format=xlsx` 200 có `prepared_by` + giá trị đã lưu; seed **33 file CSV + 1 tệp** |
| Smoke SQL Server | `Storage:Provider=SqlServer` với `localhost/SIMS_APDP` (đổi tên từ `SIMS` 2026-11-01 để không trùng DB `SIMS` của dự án khác trên LocalDB): app tự tạo schema `sims` (34 bảng sau seed), `/health` 200; login `admin`; 16 route chính 200; `POST /Students/Create` ghi `SV9001` → hiện trong `/Students` và có trong `[sims].[students]`; `/Checkin` + `/EventPlans` + `/Events/Calendar` 200; dữ liệu seed nằm đúng bảng (`users` 3, `students` 2, `permissions` 166, `audit` 36 bản ghi) |
| Smoke UI V1 | **26/26 trang module trả 200 và render `.app-shell`** khi đăng nhập `admin` (thêm `/health` 200 nhưng trả text thuần, không layout — đúng thiết kế); Playwright: `PALETTE accent=#f26522`, `FOCUS outline 2px solid rgb(242,101,34)`, `CMDK open=true results=["Sinh viên"]`, `MOBILE_SIDEBAR {open:true,expanded:"true"}`, `NO_RUNTIME_ERRORS` trên 18 lần chụp |
| Smoke UI V2/V3 | 27/27 endpoint sống (`SMOKE 26/27`, dòng FAIL duy nhất là `/health` text thuần); Playwright V2/V3: `MOBILE_OVERFLOW_PX=0`, `SKIPLINK_AFTER_TRANSITION top=8 focus=true`, lọc `/Students` `rows=2 filtered=1 count="1 / 2 mục"`, `EVENTPLANS 200`, `NO_RUNTIME_ERRORS`, 44 ảnh `v2-*`/`v3-*` |

Output thật: [`docs/test-results.txt`](docs/test-results.txt).

## Giới hạn

Xem [`docs/porting-notes.md`](docs/porting-notes.md): mỗi thao tác ghi đọc–ghi cả bảng (mô hình ô key/value,
không index theo cột nghiệp vụ), khoá chỉ trong phạm vi một SQL instance,
UI đã làm lại cả 3 vòng V1–V3 (token + shell + toàn bộ module theo component chung),
import CSV/XLSX 1 bản ghi/dòng, tài chính có webhook SePay nhưng chưa có PayOS/PDF server,
tệp đính kèm lưu cục bộ (chưa sao lưu/quét virus), điểm trường chưa tách theo học kỳ,
trang bốc thăm công khai chỉ đọc, thông báo chỉ in-app, theme giới hạn light/dark + màu chủ đạo.
Ma trận gap còn lại (PDF/email/OAuth/QR image/PayOS/central admin…) nằm trong
[`docs/parity-closure.md`](docs/parity-closure.md) và [`docs/porting-notes.md`](docs/porting-notes.md).
