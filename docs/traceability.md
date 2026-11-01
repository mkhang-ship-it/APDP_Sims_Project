# Traceability — Bước 3 + 4 (Auth, Students, Courses, Enrolments, Grades, Import) và các batch 5–11

Mỗi yêu cầu → nơi cài đặt (file:line) → test tương ứng.

## AUTH

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| A1 | Cookie authentication, không Identity/JWT/OAuth | `src/SIMS.Web/Program.cs:98` (`AddAuthentication`) | `Web/AuthIntegrationTests` |
| A2 | `users.csv` | `Infrastructure/Persistence/CsvUserRepository.cs` | `Web/AuthIntegrationTests.Seeded_admin_can_login_and_logout` |
| A3 | PBKDF2 SHA256 ≥100k, salt 16 bytes | `Infrastructure/Security/Pbkdf2PasswordHasher.cs:10` | `Infrastructure/Pbkdf2PasswordHasherTests` (5) |
| A4 | Vai trò Admin/Faculty/Student | `Domain/Security/Role.cs` | `Web/StudentsIntegrationTests.Student_role_gets_403_on_students_module` |
| A5 | `[Authorize(Roles=…)]` | `Web/Controllers/StudentsController.cs`, `CoursesController.cs`, `EnrolmentsController.cs` | Integration role tests (Web) |
| A6 | Menu theo vai trò | `Views/Shared/_Layout.cshtml` | Kiểm tra qua integration (Student không thấy menu) |
| A7 | 403 khi sai quyền | `AccountController.AccessDenied` (Status 403) | 2 integration test |

## STUDENTS

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| S1 | CRUD | `Application/Students/StudentService.cs`, `Web/Controllers/StudentsController.cs` | `Application/StudentServiceTests` (7), `Web/StudentsIntegrationTests` (3) |
| S2 | MSSV duy nhất | `StudentService.CreateAsync` + `IStudentRepository.ExistsByCodeAsync` | `Create_rejects_duplicate_code…`, `Duplicate_student_code_is_rejected…` |
| S3 | Email hợp lệ | `Domain/Students/Student.cs` + `StudentValidator` | `StudentTests` (email theory) |
| S4 | Ngày sinh | `Student.NormalizeDateOfBirth` | `Create_rejects_future/default_date_of_birth` |
| S5 | Chương trình học | `Student.Program` | `Create_returns_field_errors…` |
| S6 | Không đổi MSSV | `StudentService.UpdateAsync` | `Update_rejects_student_code_change` |
| S7 | Xoá (Admin) | `StudentsController.DeleteConfirmed` | `Delete_removes_student_and_audits` |

## COURSES

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| C1 | Tạo/sửa học phần | `Application/Courses/CourseService.cs` | `Application/CourseServiceTests` (7) |
| C2 | Sức chứa 1..200 | `Domain/Courses/Course.cs` (`NormalizeCapacity`) | `CourseTests.Create_rejects_invalid_capacity` |
| C3 | Học phần tiên quyết | `CourseService.ValidatePrerequisiteAsync` | `Create_rejects_missing_prerequisite`, `Create_accepts_existing_prerequisite` |
| C4 | Không đổi mã học phần | `CourseService.UpdateAsync` | `Update_rejects_code_change…` |
| C5 | Xoá (Admin) | `CoursesController.DeleteConfirmed` | `Delete_removes_course…` |
| C6 | Audit qua Decorator | `Application/Courses/AuditedCourseService.cs:11` | `Audited_decorator_writes_audit_for_writes` |

## ENROLMENTS

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| E1 | Chống trùng | `CsvEnrolmentRepository.TryAddAsync` (`:9`) + `InMemoryEnrolmentRepository` | `Enrol_rejects_duplicate_pair`, `TryAdd_rejects_duplicate_pair_without_writing` |
| E2 | Sức chứa + ghi atomic | `CsvEnrolmentRepository.TryAddAsync` (kiểm tra + ghi trong 1 `MutateAsync`) | `Enrol_rejects_when_course_is_full`, `TryAdd_rejects_when_course_is_full`, integration over-capacity |
| E3 | Trạng thái hoạt động | `Domain/Enrolments/Enrolment.cs` (`IsActive`, `Drop`) | `Drop_sets_dropped_and_is_idempotent` |
| E4 | Điều kiện tiên quyết (Strategy) | `Application/Enrolments/PrerequisitePolicy.cs` | `Enrol_rejects_when_prerequisite_not_met`, `…_succeeds_when_prerequisite_passed` |
| E5 | Rollback khi vi phạm | `TryAddAsync` trả `Fail` và không ghi file | `TryAdd_rejects_*` (đếm row không đổi) |
| E6 | Audit | `Application/Enrolments/EnrolmentService.cs` | `Enrol_succeeds_and_writes_audit`, `Drop_…_audits` |

## GRADES

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| G1 | Nhập điểm 0..10 | `Application/Grades/GradeService.cs:52` + `Enrolment.AssignGrade` | `GradeServiceTests` (7, gồm 6 theory) |
| G2 | Xếp loại theo Strategy | `TenPointGradePolicy` (A≥9…F<5) | `Assign_maps_score_to_letter` |
| G3 | Không chấm điểm đã huỷ | `GradeService` + domain | `Assign_rejects_dropped_enrolment_and_missing_id` |
| G4 | Audit | `GradeService.AssignAsync` | `Assign_writes_audit` |

## IMPORT CSV (streaming)

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| I1 | Không `ReadAllLines` (streaming) | `Infrastructure/Persistence/CsvStudentImportParser.cs:11` (đọc từng dòng) | `CsvStudentImportParserTests` (5) |
| I2 | Lỗi theo Row N | `StudentImportService` (`StudentImportError.RowNumber`) | `Import_keeps_valid_rows_and_reports_errors_with_row_numbers`, integration import |
| I3 | Dòng lỗi không hỏng cả file | `StudentImportService` (skip row, continue) | `Import_…` + `Import_detects_duplicate_code_inside_file` |
| I4 | OCP parser | `IStudentImportParser` (`StudentImportService.cs:28`) + fake trong test | `StudentImportServiceTests.FakeParser` |
| I5 | Audit import (Decorator) | `AuditedStudentImportService` | `Audited_decorator_writes_summary` |
| I6 | File mẫu UTF-8 no BOM | `DefaultDataSeeder` → `data/students-invalid.csv` | Kiểm tra runtime: `invalid_csv_bom=False` |

## DASHBOARD / HẠ TẦNG

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| D1 | Dashboard counts | `Application/Dashboard/DashboardService.cs:17` | Smoke `/` (dashboard=200) |
| D2 | Ghi atomic tmp+Move | `Infrastructure/Persistence/CsvFileStore.cs:161` | `CsvFileStoreTests.Mutate_replaces_file_atomically…` |
| D3 | Khoá theo file | `CsvFileStore` static `SemaphoreSlim` | `Concurrent_appends_are_serialized_per_file` |
| D4 | LSP generic repository | `CsvRepository<T>` (`:9`) + `InMemoryRepository<T>` (tests) | Toàn bộ infra test dùng cả hai |
| D5 | ISP reader/writer | `IStudentReader`/`IStudentWriter` (`Abstractions/Persistence.cs`) | `DashboardService` chỉ phụ thuộc `IStudentReader` |
| D6 | DIP wiring | `src/SIMS.Web/Program.cs` (DI + decorator) | Build + integration |
| D7 | Audit mọi write | `IAuditLog` → `CsvAuditLog` | Assert trong từng service + integration |
| D8 | Seed courses/enrolments/users/audit | `Infrastructure/Security/DefaultDataSeeder.cs` | Smoke: 6 file CSV trong `data/` |

## BATCH 5 — Phân quyền theo module, Student↔User, CLB/multi-club

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| P1 | Ma trận quyền module/action (CSV) | `Domain/Security/RolePermission.cs`, `Application/Security/PermissionCatalog.cs`, `Infrastructure/Persistence/CsvPermissionRepository.cs` | `Application/PermissionServiceTests` (14), `Infrastructure/CsvPermissionRepositoryTests` (1) |
| P2 | Policy động `perm:module:action` | `Web/Security/PermissionAuthorization.cs` (RequirePermission, handler, policy provider) + `Program.cs` | Toàn bộ integration test role/permission |
| P3 | Áp quyền ở controller | Students/Courses/Enrolments/Clubs/MyLearning (`[RequirePermission]`) | `Step4IntegrationTests`, `Step5IntegrationTests` |
| P4 | Menu theo quyền | `Views/Shared/_Layout.cshtml` (AuthorizeAsync) | `Step5IntegrationTests.Student_menu_shows_learning_but_not_students_module` |
| L1 | Student↔User (UserId) | `Domain/Students/Student.cs` (`UserId`, `LinkUser`), `StudentService.LinkUserAsync`, auto-link theo email khi tạo | `Domain/StudentLinkingTests` (3), `Infrastructure/CsvStudentLinkingTests` (1) |
| L2 | Student xem hồ sơ/điểm của mình | `Application/Learning/MyLearningService.cs`, `Web/Controllers/MyLearningController.cs`, `Views/MyLearning/Index.cshtml` | `Application/MyLearningServiceTests` (2), `Step5IntegrationTests.Student_can_see_own_learning_profile_and_grades` |
| C1 | CLB (CRUD + danh sách) | `Domain/Clubs/Club.cs`, `Application/Clubs/ClubService.cs`, `Infrastructure/Persistence/CsvClubRepositories.cs` | `Domain/ClubTests` (6), `Application/ClubServiceTests` (5), `Infrastructure/CsvClubRepositoryTests` (2) |
| C2 | Multi-club membership | `Domain/Clubs/ClubMembership.cs` + `club_memberships.csv` + assign trong `ClubsController` | `Domain/ClubMembershipTests` (3), `ClubServiceTests.Assign_member_*` |
| C3 | Chuyển CLB (claim `club_id`) | `AccountController` (claim khi login) + `ClubsController.Switch` | `Step5IntegrationTests.Student_can_switch_between_joined_clubs` |
| C4 | Lọc dữ liệu theo CLB hiện tại | `StudentsController.Index` (CLB hiện tại + chưa gán) | `Step5IntegrationTests.Faculty_student_list_is_scoped_to_current_club` |
| C5 | Quyền CLB (view/create/edit) | PermissionCatalog + `ClubsController` + ẩn nút theo quyền trong view | `Step5IntegrationTests.Faculty_cannot_open_club_creation_without_permission`, `Admin_can_open_club_creation` |

## BATCH 6 — Sự kiện, lịch tháng, check-in QR/token

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| E1 | Sự kiện: tạo/sửa, mã duy nhất, thời gian hợp lệ, sức chứa | `Domain/Events/Event.cs`, `Application/Events/EventService.cs`, `Web/Controllers/EventsController.cs` | `Domain/EventTests` (8), `Application/EventServiceTests` (4) |
| E2 | Công bố / huỷ / hoàn thành | `Event.Publish/Cancel/Complete` + `EventService` | `EventTests.Cancel_and_complete_follow_status_rules`, `EventServiceTests.Publish_and_rotate_change_state` |
| E3 | Lịch tháng | `Application/Events/EventCalendarService.cs`, `Views/Events/Calendar.cshtml` | `EventCalendarServiceTests`, `EventsIntegrationTests.Calendar_page_renders_for_faculty` |
| E4 | Mã check-in (nội dung QR) + link | `Event.CheckinToken`, `Views/Events/Details.cshtml` | `EventsIntegrationTests.Faculty_can_create_publish_and_see_checkin_panel` |
| E5 | Check-in chống trùng + sức chứa (atomic) | `CsvAttendanceRepository.TryCheckInAsync`, `Application/Checkin/CheckinService.cs` | `CsvAttendanceRepositoryTests` (3), `CheckinServiceTests` (6) |
| E6 | Quét QR trên trình duyệt (BarcodeDetector) + nhập mã | `Views/Checkin/Scanner.cshtml`, `CheckinController` | `EventsIntegrationTests.Student_checks_in_with_token_and_appears_in_attendance` |
| E7 | Quyền events/checkin | `PermissionCatalog` + `[RequirePermission]` | `EventsIntegrationTests.Student_can_view_events_but_cannot_create_them` |
| E8 | Audit check-in | `CheckinService` → `checkin.create` | `CheckinServiceTests.Checkin_succeeds_and_writes_audit` |

## BATCH 7 — Kế hoạch sự kiện (plan + 5 mục chi tiết + export CSV)

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| K1 | Một kế hoạch / sự kiện, tự tạo khi mở | `Domain/Events/EventPlan.cs`, `EventPlanService.GetOrCreateAsync`, `event_plans.csv` | `Application/EventPlanServiceTests.Get_or_create_persists_single_plan_per_event` |
| K2 | Tóm tắt kế hoạch (≤2000 ký tự) | `EventPlan.UpdateSummary` + `EventPlansController.Summary` | `Domain/EventPlanTests.Plan_summary_is_trimmed_and_rejects_too_long_text` |
| K3 | Mục tiêu / hành động / thiết bị / timeline | `Domain/Events/PlanItems.cs`, `EventPlanService.Add*Async/Remove*Async` | `EventPlanTests` (5), `EventPlanServiceTests.Adding_items_across_sections_returns_view_with_budget_total` |
| K4 | Ngân sách số lượng × đơn giá, tổng | `PlanBudgetItem.Total` + `EventPlanView.BudgetTotal` | `EventPlanTests.Budget_item_computes_total_and_rejects_bad_quantity`, service test ở trên |
| K5 | Thứ tự mục và xoá theo mục | `EventPlanService.AddItemAsync` (`order = count + 1`), `Remove` theo `section` | `EventPlanServiceTests.Remove_deletes_known_item_and_rejects_unknown` |
| K6 | Export kế hoạch | `EventPlanService.ExportCsvAsync` (BCL, UTF-8 BOM ở controller) + `EventPlansController.Export` | `EventPlanServiceTests.Export_csv_contains_all_sections_and_escapes_commas`, `Web/EventPlansIntegrationTests.Faculty_adds_items_and_exports_csv` |
| K7 | Quyền `event_plans` view/edit | `PermissionCatalog` + `[RequirePermission]` + ẩn link theo quyền | `EventPlansIntegrationTests.Student_is_denied_on_plan_module`, `Faculty_sees_seeded_plan_and_link_from_event_details` |
| K8 | Audit mọi thao tác ghi | `EventPlanService.AuditAsync` (`eventplan.*`) | Assert trong `EventPlanServiceTests` |
| K9 | Lưu trữ CSV 6 bảng | `Infrastructure/Persistence/CsvEventPlanRepositories.cs` | `Infrastructure/CsvEventPlanRepositoryTests` (3) |

## BATCH 8 — Tài chính: hoá đơn, thanh toán, chi phí

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| F1 | Hoá đơn theo thành viên, mã `INV-{năm}-{6 số}` duy nhất | `Domain/Finance/Invoice.cs`, `Application/Finance/InvoiceService.CreateAsync`, `RandomInvoiceCodeGenerator` | `Domain/FinanceDomainTests` (6), `Application/InvoiceServiceTests` (9) |
| F2 | Tạo hoá đơn hàng loạt cho nhiều thành viên CLB | `InvoiceService.CreateAsync` + `InvoicesController.Create` | `InvoiceServiceTests.Create_makes_one_invoice_per_member_with_unique_codes`, `Web/FinanceIntegrationTests.Faculty_creates_invoice_for_member_and_collects_payments` |
| F3 | Thanh toán từng phần, đủ tiền tự chuyển "đã thanh toán" | `InvoiceService.RecordPaymentAsync` + `Invoice.MarkPaid` | `InvoiceServiceTests.Partial_payment_then_remaining_marks_invoice_paid` |
| F4 | Chặn trả vượt số còn lại / trả lại hoá đơn đã thu đủ | `RecordPaymentAsync` (kiểm tra remaining, status) | `Payment_rejects_overpay_zero_and_date_before_creation`, `Payment_on_paid_invoice_is_rejected` |
| F5 | Quá hạn (pending + quá hạn ngày) | `Invoice.MarkOverdue` + `InvoiceService.MarkOverdueAsync` + POST `MarkOverdue` | `Invoice_status_moves_to_paid_and_overdue_once`, `Mark_overdue_updates_only_past_due_pending_invoices` |
| F6 | Sửa/xoá chỉ khi chờ thanh toán; xoá kèm giao dịch | `Invoice.IsEditable`, `InvoiceService.UpdateAsync/DeleteAsync` | `InvoiceServiceTests.Update_and_delete_follow_pending_status` |
| F7 | Chi phí: quản trị tạo (duyệt sẵn) / thành viên đề nghị (chờ duyệt) | `Domain/Finance/Expense.cs`, `ExpenseService.CreateAsync` | `ExpenseServiceTests.Staff_creation_is_approved_and_member_creation_is_pending` |
| F8 | Duyệt / từ chối / đánh dấu đã chi + ghi giao dịch chi | `Expense.Approve/Reject/MarkPaid`, `ExpenseService`, `ExpensesController` | `ExpenseServiceTests` (7), `Web/FinanceIntegrationTests` |
| F9 | Giao dịch thu–chi (một trong hai chủ sở hữu) | `Domain/Finance/Transaction.cs`, `CsvTransactionRepository` | `Transaction_requires_exactly_one_owner_and_positive_amount`, `CsvFinanceRepositoryTests` (4) |
| F10 | Danh sách "của tôi" cho sinh viên, phiếu in | `InvoiceService.ListForUserAsync`, `ExpensesController.MyExpenses`, `Views/Invoices/Receipt.cshtml` | `Web/FinanceIntegrationTests.Student_sees_own_invoices_but_not_staff_invoice_list`, `Student_submits_expense_request_and_faculty_approves_it` |
| F11 | Quyền `invoices` / `expenses` (+ `my_*`) | `PermissionCatalog` + `[RequirePermission]` + menu | 4 integration test quyền |
| F12 | Audit mọi thao tác ghi | `InvoiceService`/`ExpenseService` → `invoice.*`, `expense.*` | Assert trong service tests |

## BATCH 9 — Văn bản, kỷ luật/điểm rèn luyện, khen thưởng

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| D1 | Loại văn bản (mã `DT-######`, mô tả, liên kết mẫu) | `Domain/Documents/Documents.cs`, `DocumentService.CreateTypeAsync`, `DocumentsController` | `Domain/GovernanceDomainTests`, `Application/DocumentServiceTests` |
| D2 | Tải văn bản thật (PDF/Word/JPG/PNG/TXT ≤10 MB) | `DocumentService.UploadAsync` + `Infrastructure/Storage/LocalFileStorage.cs` | `DocumentServiceTests.Upload_*`, `Infrastructure/LocalFileStorageTests` |
| D3 | Duyệt/từ chối + lý do, người duyệt/thời điểm | `Document.Approve/Reject`, `DocumentService.ReviewAsync` | `Document_approve_and_reject_guard_reason`, `Upload_creates_pending_document_and_approve_changes_status` |
| D4 | Hiển thị public/private/restricted + danh sách người xem | `Document.SetVisibility`, `DocumentAccess`, `DocumentService.SetAccessAsync/ListAccessibleAsync` | `Restricted_document_requires_access_and_shows_in_accessible_list`, Web test quyền tải |
| D5 | Yêu cầu nộp văn bản + hạn nộp + người nhận | `Domain/Documents/DocumentRequests.cs`, `DocumentRequestService` | `DocumentRequestServiceTests` (3) |
| D6 | Thành viên nộp văn bản theo yêu cầu (liên kết 1-1) | `DocumentRequestUser.Submit`, `DocumentRequestService.SubmitAsync` | `Submit_creates_document_and_marks_assignment_processed`, Web `Document_request_flow_from_faculty_to_student` |
| D7 | Danh mục kỷ luật (mã `KL-######`, điểm ±, mô tả) | `Domain/Disciplines/Disciplines.cs`, `DisciplineService` | `DisciplineServiceTests`, `GovernanceDomainTests` |
| D8 | Áp dụng/thu hồi bản ghi kỷ luật | `DisciplineRecord.Apply/Revoke`, `DisciplinesController` | `Create_apply_and_revoke_records`, `Discipline_record_revoke_blocks_update` |
| D9 | Điểm CLB, điểm trường (scale 5 + 5 cán bộ), xếp hạng | `DisciplineService.GetMemberPointsAsync/GetUserPointsAsync` | `Points_scale_and_rank_members_with_leader_bonus`, Web `Discipline_pages_and_export_*` |
| D10 | "Điểm của tôi" cho sinh viên | `DisciplinesController.MyPoints`, `Views/Disciplines/MyPoints.cshtml` | Web `Discipline_pages_*` |
| D11 | Giải thưởng (mã `BK-######`), trạng thái, người nhận, chứng nhận | `Domain/Awards/Awards.cs`, `AwardService` | `AwardServiceTests` (5) |
| D12 | Quyền `documents`, `discipline`, `awards` (+ `my_*`) | `PermissionCatalog` + `[RequirePermission]` + menu | 4 Web test quyền |
| D13 | Audit mọi thao tác ghi | `DocumentService`, `DocumentRequestService`, `DisciplineService`, `AwardService` | Assert trong service tests |

## BATCH 10 — Bốc thăm, trang công khai, thông báo, cài đặt

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| L1 | Chương trình bốc thăm (slug công khai, mã bảo mật, thời gian, danh sách người tham gia) | `Domain/LuckyDraws/LuckyDraws.cs`, `LuckyDrawService.CreateAsync/UpdateAsync` | `Domain/EngagementDomainTests`, `Application/LuckyDrawServiceTests` (6) |
| L2 | Giải thưởng (số lượng, thứ tự, giải đặc biệt), chặn trùng tên | `LuckyPrize`, `LuckyDrawService.AddPrizeAsync/UpdatePrizeAsync` | `Prize_rules_and_duplicate_names` |
| L3 | Quay số ngẫu nhiên, mỗi người trúng một lần, dừng khi hết lượt | `LuckyDrawService.DrawAsync` (RNG) + `lucky_winners.csv` | `Draw_picks_unique_participants_and_stops_when_prize_is_full`, Web `Faculty_draws_winner_and_public_page_shows_result` |
| L4 | Xoá kết quả / xoá chương trình dọn giải + người trúng | `ClearWinnersAsync`, `DeleteAsync` | `Clear_winners_and_delete_prize_rules`, `Update_and_delete_draw_cascade_*` |
| P1 | Trang chủ công khai (CLB, sự kiện sắp tới, chương trình đang mở) | `HomeController.Index` (nhánh ẩn danh) + `Views/Home/PublicHome.cshtml` | Web `Anonymous_home_shows_public_content` |
| P2 | Trang bốc thăm công khai `/lucky/{slug}`, mã bảo mật qua session | `LuckyDrawPublicController`, `Views/LuckyDrawPublic/Show.cshtml` | Web `Public_draw_requires_security_code_and_unknown_slug_is_404` |
| N1 | Thông báo toàn bộ CLB hoặc CLB cụ thể | `Domain/Notifications/Notifications.cs`, `NotificationService`, `notification_targets.csv` | `NotificationServiceTests` (5) |
| N2 | Hộp thư, đếm chưa đọc, đánh dấu đã đọc/tất cả | `NotificationService.InboxAsync/MarkReadAsync/MarkAllReadAsync` + `notification_reads.csv` | `Inbox_visibility_unread_and_mark_read`, Web `Faculty_manages_notifications_and_student_reads_inbox` |
| N3 | Quản lý: tạo/sửa/ẩn/xoá, phân quyền Admin/Faculty | `NotificationsController` + `[RequirePermission(notifications, …)]` | Web test quyền Student 302 |
| S1 | Cấu hình toàn hệ thống (tên, mô tả, theme, màu, email hỗ trợ) | `Domain/Settings/Settings.cs`, `SettingsService`, `SettingsController.Global` | `SettingsServiceTests` (4), Web `Admin_updates_site_settings_and_public_home_reflects_them` |
| S2 | Cấu hình CLB (liên hệ, địa chỉ, ngân hàng) | `SettingsController.Club` + `settings.csv` (scope ClubId) | Web `Faculty_updates_club_settings_and_student_is_denied` |
| S3 | Áp cấu hình vào layout (tên site, theme, màu chủ đạo) | `Views/Shared/_Layout.cshtml` + `ISettingsService` | Web `data-bs-theme`, màu `#112233` |
| S4 | Audit thao tác ghi | `LuckyDrawService`, `NotificationService`, `SettingsService` | Assert trong service tests |

## BATCH 11 — Parity closure

| # | Yêu cầu | Cài đặt | Test |
|---|---|---|---|
| C1 | Link rút gọn (`/s/{code}`), hết hạn/tạm dừng, đếm lượt truy cập | `Domain/ShortUrls/ShortUrls.cs`, `ShortUrlService`, `ShortUrlsController` | `Domain/ClosureDomainTests`, `ShortUrlServiceTests` (3), `CsvClosureRepositoryTests`, Web flow |
| C2 | Hồ sơ cá nhân: sửa tên/email, đổi mật khẩu có xác minh mật khẩu cũ | `User.UpdateProfile`, `ProfileService`, `ProfileController` | `ProfileServiceTests` (2), Web `Profile_update_and_password_change` |
| C3 | Ảnh sự kiện: upload JPG/PNG/WEBP, xem, xoá (file thật) | `Domain/Events/EventImages.cs`, `EventImageService`, `EventsController` | `EventImageServiceTests` (2), Web `Event_image_upload_download_and_delete` |
| C4 | Webhook SePay: xác thực Apikey, ghi thanh toán theo mã hoá đơn, idempotent | `PaymentWebhookService`, `SePayWebhookController`, settings `payments.sepay_api_key` | `PaymentWebhookServiceTests` (3), Web `Sepay_webhook_marks_invoice_paid` |
| C5 | Chế độ bảo trì: khách nhận 503, Admin/health/webhook vẫn qua | `Web/Security/MaintenanceMiddleware.cs` + settings | Web `Maintenance_blocks_guests_but_allows_admin` |
| C6 | Nhật ký hệ thống có lọc (audit.csv) | `IAuditLogReader` + `CsvAuditLog.ReadAsync` + `LogsController` | `CsvClosureRepositoryTests.Audit_log_read_*`, Web `Logs_export_*` |
| C7 | Xuất CSV danh sách sinh viên (thay MemberExport) | `StudentsController.Export` | Web `Logs_export_and_feature_request_flow` |
| C8 | Đề xuất tính năng: thành viên gửi, nhân viên đổi trạng thái/phản hồi | `Domain/FeatureRequests`, `FeatureRequestService`, `FeatureRequestsController` | `FeatureRequestServiceTests` (2), Web flow |
| C9 | Ma trận gap còn lại + lý do | `docs/porting-notes.md` mục "Parity closure" | — |

## BATCH 12 — Đóng gap cuối (geofence, Excel, kế hoạch mở rộng)

| # | Yêu cầu PHP | Cài đặt .NET | Test |
|---|---|---|---|
| D1 | Check-in giới hạn bán kính 5 km quanh địa điểm tổ chức | `Domain/Events/GeoPoint.cs` (Haversine, validate toạ độ, `CheckinRadiusKm = 5`); `Event.Venue`; `Attendance.CheckinLocation`/`DistanceKm`; `CheckinService` từ chối khi thiếu vị trí hoặc quá xa | `GeoPointTests` (5), `GeofenceCheckinTests` (5), `CsvGeofenceAndPlanTests` (4), Web `Geofence_checkin_rejects_far_location_and_accepts_near` |
| D2 | GPS do trình duyệt gửi lên form check-in | `Views/Checkin/Index.cshtml` (`navigator.geolocation` → input ẩn `latitude`/`longitude`); `CheckinController` truyền toạ độ | Cùng test Web D1 |
| D3 | Nhập Excel danh sách sinh viên | `Application/Common/Xlsx.cs` (đọc/ghi XLSX bằng BCL, 50k dòng/200 cột); `StudentsController.Import` nhận diện `PK` và chuyển XLSX → CSV | `XlsxTests` (3), Web `Xlsx_student_import_creates_student` |
| D4 | Xuất Excel danh sách sinh viên | `StudentsController.Export?format=xlsx` dùng `Xlsx.Write` | Web `Students_export_xlsx_is_a_real_workbook` |
| D5 | Thông tin kế hoạch sự kiện theo mẫu PHP (người lập, ngày triển khai, người duyệt, đối tượng, mô tả, đơn vị chủ trì, cố vấn) | `EventPlan` + `UpdateDetails` + `EventPlanDetailsInput`; `EventPlanService.UpdateDetailsAsync`; `EventPlansController.Details` | `EventPlanDetailsTests` (4), Web `Event_plan_xlsx_export_contains_extended_fields` |
| D6 | Xuất kế hoạch ra Excel | `EventPlanService.ExportXlsxAsync` + `EventPlans/Export?format=xlsx` | `EventPlanDetailsTests.Export_xlsx_*`, Web D5 |
| D7 | Tương thích ngược CSV khi thêm cột | `CsvFileStore.ReadCoreAsync` đệm ô rỗng cho dòng cũ thiếu cột | `CsvGeofenceAndPlanTests.Old_*_are_still_loaded` (3) |

## BATCH 13 — Chuyển bản nộp chính sang SQL Server

| # | Yêu cầu | Cài đặt .NET | Test |
|---|---|---|---|
| S1 | Dữ liệu nghiệp vụ trên SQL Server, giữ nguyên toàn bộ hành vi | `Infrastructure/Persistence/IRowStore.cs`, `SqlRowStore.cs`, `RowStoreFactory.cs`; `CsvRepository<T>` đọc/ghi qua `IRowStore` | `SqlRowStoreTests.Append_and_read_keeps_cell_order_and_pads`, `Mutate_updates_in_place_and_reports_change` |
| S2 | Chọn backend mà không sửa logic nghiệp vụ | `RowStoreFactory` theo `Storage:Provider` (`SqlServer` mặc định, `Csv` phụ) + `Program.cs` helper `Stores()` cho 38 bảng | `SqlRowStoreTests.Factory_*` (2), `InMemoryRowStoreTests` (3) |
| S3 | Ghi nguyên tử / read-modify-write tương đương bản CSV | `SqlRowStore.MutateAsync`: `SERIALIZABLE` + `sp_getapplock` + delete/insert trong một transaction | `SqlRowStoreTests.Mutate_returns_false_when_handler_declines` |
| S4 | Tự tạo schema khi khởi động, không cần script tay | `SqlRowStore.EnsureDatabaseAsync` (`CREATE SCHEMA [sims]`) + `EnsureTableAsync` (idempotent) + `RowStoreFactory.EnsureCreatedAsync` gọi trong `Program.cs` | Smoke SQL Server thật |
| S5 | Test không phụ thuộc engine | `SimsWebApplicationFactory` ép `Storage:Provider=Csv`; `SqlRowStoreTests` tự bỏ qua khi không kết nối được | `dotnet test` 405/405 |
| S6 | Chống injection qua tên bảng / thoát thư mục | `SqlRowStore.IsValidTableName` (`^[A-Za-z][A-Za-z0-9_]{0,99}$`) kiểm trong ctor `SqlRowStore` **và** `RowStoreFactory.Create` (nhánh CSV ghép tên vào đường dẫn) | `Invalid_table_names_are_rejected` (6 ca), `Valid_table_names_are_accepted` (3 ca) |
| S7 | Đọc/ghi nhanh, không quét toàn bảng | `EnsureTableAsync` tạo `IX_{Table}_RowOrd` UNIQUE trên `([RowNo],[Ord])`, idempotent | `36 ban ghi / 36 co unique index RowOrd` trong `sys.indexes` |
| S8 | Append đồng thời không trộn ô giữa hai bản ghi | `AppendAsync` bọc `SERIALIZABLE` + `sp_getapplock` trước khi lấy `MAX(RowNo)+1` và insert | `Concurrent_appends_do_not_mix_cells` (8 luồng) |
| S9 | DDL an toàn khi nhiều request cùng khởi tạo | `EnsureTableAsync` khoá `sp_getapplock` owner `Session` + `TRY/CATCH` bỏ qua lỗi 1913 | Chạy lặp 10 vòng test SQL, 0 thất bại (đã bắt được race thật trước khi vá) |
| S10 | Trạng thái khởi tạo theo từng DB, không phải cờ toàn cục | `SqlRowStore.InitializedConnections` (`HashSet<string>` theo connection string) thay cho `static bool` | `Factory_*`, smoke SQL Server |

## UI V1 — design token, app shell, dashboard, light/dark

| # | Yêu cầu | Cài đặt | Bằng chứng |
|---|---|---|---|
| U1 | Khác rõ Bootstrap mặc định | `wwwroot/css/site.css` (token + shell), `wwwroot/css/theme.css` (override Bootstrap 5.1 bằng thuộc tính trực tiếp vì 5.1 chưa có `--bs-btn-*`/`--bs-table-*`) | `v1-desktop-light-dashboard.png` |
| U2 | Accent `#F26522` chỉ làm màu nhấn trên nền neutral | `--accent: #f26522` (light) / `#ff7a3d` (dark); `_Layout.cshtml` chỉ override từ `site.primary_color` khi khác mặc định cũ `#0d6efd` | Probe `PALETTE … "accent":"#f26522"` |
| U3 | App shell: sidebar + topbar + content | `_Layout.cshtml` → `.app-shell` grid (`--sidebar-w: 260px`), `.app-topbar` sticky + blur, `.app-content` max 1320px | Mọi ảnh |
| U4 | Điều hướng nhanh, menu theo quyền | `_Layout.cshtml` gom menu theo nhóm (Tổng quan / Học vụ / CLB / Tài chính / Văn bản / Hệ thống / Tài khoản), mỗi mục qua `CanAsync(module, action)`; **không** dùng `@:` để tránh rác Razor | Ảnh sidebar; smoke 26 route |
| U5 | Command palette | `site.js` (Ctrl+K, `/`, ↑↓, Enter, Esc) + markup `#cmdk` trong `_Layout` | Probe `CMDK open=true results=["Sinh viên"]`, `v1-desktop-light-cmdk.png` |
| U6 | Sidebar mobile + scrim + Esc | `.sidebar-open`, `#sidebar-scrim`, `aria-expanded` | Probe `MOBILE_SIDEBAR {"open":true,"expanded":"true"}`, `v1-mobile-light-sidebar.png` |
| U7 | Dashboard dễ quét | `Views/Home/Index.cshtml`: 4 KPI (sinh viên/học phần/đăng ký/CLB), panel sự kiện sắp tới, meter tỷ lệ nhập điểm, thao tác nhanh, panel hệ thống | 6 ảnh `*-dashboard.png` |
| U8 | Dữ liệu dashboard thật | `DashboardService` thêm `Clubs`, `UpcomingEvents`, `NextEvents`; `HomeController` inject `INotificationService` + `IUserRepository` cho số thông báo chưa đọc | Build/test 405/405, ảnh dashboard có số `2 / 3 / 1 / 2` |
| U9 | Light/dark, không nháy trắng | `<html data-theme data-bs-theme>`, script pre-paint đọc `localStorage['sims-theme']`, 3 nút đổi theme (sidebar/topbar/user menu) | Ảnh `*-dark-*.png`, probe đổi theme |
| U10 | Accessibility: focus, skip link, keyboard | `:focus-visible` 2px accent (kể cả `.btn`), skip link `#main-content`, focus trap user menu, `role="status"` cho alert | Probe `FOCUS … outline 2px solid rgb(242,101,34)` |
| U11 | Không phá nghiệp vụ / route / phân quyền | Không đổi controller ngoài `HomeController` (thêm 2 dependency có sẵn trong DI); route + `[RequirePermission]` giữ nguyên | `dotnet test` 405/405, smoke 26 route 200 |

Ảnh: `docs/screens/v1-*.png` (20 tệp). Probe + lỗi thật gặp trong vòng này: `docs/test-results.txt`.

## UI V2 + V3 — toàn bộ module theo component chung

| # | Yêu cầu | Cài đặt | Bằng chứng |
|---|---|---|---|
| U12 | Mọi trang module dùng component chung, không còn markup Bootstrap thô | Toàn bộ view Razor viết lại: `.section`/`.toolbar`/`.table-shell`/`.pager`/badge variants/`.form-grid`/`.detail-list`/`.stat-grid`/`.notice`/`.auth`/`.state-page`/`.public-hero`/`.draw-card`; partials `_EmptyState`, `_TableToolbar`, `_TableFoot`; scan `Select-String` toàn views: 0 markup legacy còn sót | 26 ảnh `v2-desktop-light-*.png`, 6 ảnh `v2-desktop-dark-*.png` |
| U13 | Lọc/sắp xếp/phân trang phía client, khai báo không JS riêng | `site.js` `initTable` trên wrapper `[data-table]` (search `[data-table-search]`, sort `th[data-sort]`, pager `[data-table-pager]`, đếm `[data-table-count]`) | Probe `rows=2 filtered=1 count="1 / 2 mục"`, ảnh `v2-desktop-light-students-filtered.png` |
| U14 | Empty/loading/error state nhất quán | List rỗng ẩn toolbar/pager + `_EmptyState`; 403/500 dùng `.state-page`; login `.auth` hai cột; trang bốc thăm công khai redesign standalone | `v2-state-403.png`, `v2-login.png`, `v2-public-lucky.png` |
| U15 | Responsive 390px không tràn ngang | `@media` breakpoints cho toolbar/grid/nav; V3 bỏ `table-responsive` inline cuối cùng (`Home/Index`), dùng `.table-shell` | Probe `MOBILE_OVERFLOW_PX=0`, 5 ảnh `v2-mobile-*.png` |
| U16 | Skip-link chuẩn, focus thấy rõ | V3: skip-link từ inline style + onfocus → class `.skip-link` trong `site.css` (transition + `:focus`) | Probe `SKIPLINK_AFTER_TRANSITION top=8 focus=true`, `v3-desktop-light-skiplink.png` |
| U17 | Không phá nghiệp vụ | Không đổi controller/route/phân quyền; chỉ Razor + CSS + site.js | `dotnet test` 405/405 sau V3, `SMOKE 26/27` (FAIL duy nhất = `/health` text thuần), `NO_RUNTIME_ERRORS` |

Ảnh: `docs/screens/v2-*.png` + `v3-*.png` (44 tệp). Probe vòng V2/V3: `docs/test-results.txt`.

## Tổng test

`dotnet test` → **405 passed / 405** · Domain **98** · Application **155** · Infrastructure **84** · Architecture **3** · Web **65**
(vượt mức tối thiểu: Domain ≥10, Application ≥6, Infrastructure ≥5, Web ≥8)

Output thật: `docs/test-results.txt`.
