# Changelog

Định dạng theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), phiên bản theo [SemVer](https://semver.org/).

## [Unreleased]

### Changed
- HUD viết lại bằng UI Toolkit (UXML + USS + PanelSettings nằm trong `Runtime/Unity/Resources/DreamTechDevTools`), giao diện tối
  kiểu app điện thoại: header có tiêu đề + chip fps/lỗi + nút dock / cỡ (S, M, L) / đóng; thanh tab dạng viên, cuộn ngang bằng
  ngón tay; ô tìm kiếm có icon + nút xóa; mỗi lệnh là một thẻ (tên + gợi ý, sao nhỏ trong thẻ, nút Run màu nhấn).
- Tham số là control thật: `LongField` / `DoubleField` có nút - / +, `DropdownField` cho Choice và Enum, công tắc cho Bool,
  `TextField`. Lệnh bị chặn mờ đi kèm chip lý do; lệnh Confirm hiện khung xác nhận ngay trong thẻ (5 giây). Toggle là công tắc.
- Watch là bảng key / value gọn theo nhóm; kết quả lệnh nháy xanh / đỏ quanh thẻ (thời gian không scale) và toast nổi ở đáy panel.
- Viên DEV là viên thuốc bo tròn (chấm trạng thái, fps, watch ghim, số lỗi), kéo được, chạm để mở.
- Panel sortingOrder 32000 và đục hẳn (mặc định 1.0): chữ uGUI của game không còn lộ qua. Tap trên HUD bị chặn nhờ panel là
  raycaster của EventSystem; blocker uGUI riêng bị bỏ. `IsPointerOverHud` dùng toạ độ panel.
- Màn ngang dùng bố cục gọn (header / tab thấp hơn, panel chiếm tỉ lệ cao hơn).
- Mọi màu / cỡ / bo góc là biến USS trong `DevToolsTheme.uss`. Package thêm phụ thuộc `com.unity.modules.uielements`.

### Added
- `Editor/HudAssetBuilder` sinh lại asset PanelSettings của HUD (chỉ dành cho người phát triển package).
- `tools/player-smoke.py` chụp thêm tab Engine / Watch / Console và viên DEV.

### Fixed
- Test `DevTools.UI` chạy được ở batch mode có GPU (`--graphics`).

## [0.2.0] - 2026-10-01

### Changed
- HUD: giao diện được dọn lại, mọi màu / cỡ / thời gian gom vào một lớp `Theme` trong `DevToolsHud`.
- Dòng lệnh có tham số tách hai dòng (sao + tên + nút Run cố định, bên dưới là ô tham số chia đều bề rộng): không còn tràn ngang, nút Run luôn thấy đủ; vùng cuộn dọc không còn thanh cuộn ngang.
- Lệnh bị chặn hiện gọn một dòng (tên mờ + lý do), không còn nút xám lớn kèm dòng phụ.
- Nền panel đậm hơn và không bao giờ trong hơn 0.9 (mặc định 0.97) để chữ của game không lọt qua; viền mảnh quanh panel và ô nhập.
- Thông báo kết quả rút gọn đường dẫn file thành tên file và giới hạn độ dài, một dòng; nội dung đầy đủ vẫn ở tab Log.
- Nút nhỏ (sao, +, -, v/M/X, ×) rộng 44 đơn vị cho thao tác chạm; tiêu đề nhóm có đường kẻ và khoảng cách.
- Tab Quick / nhóm lệnh dùng danh sách dựng một lần mỗi frame thay vì LINQ trong OnGUI.

### Added
- Tab đang chọn có thanh nhấn màu bên dưới; thanh tab tự cuộn để lộ tab đang chọn khi mở panel hoặc đổi tab qua API (`ShowTab`).
- Ô Search có viền, chữ gợi ý và nút xóa (×); ô lọc trong bộ chọn giá trị dùng cùng kiểu.
- Dòng lệnh nháy xanh / đỏ (mờ dần 0.6 giây, thời gian không scale) sau khi chạy từ HUD.
- `tools/player-smoke.py --resolution WxH` để chụp smoke ở dọc / ngang.

## [0.1.2] - 2026-10-01

### Fixed
- HUD: thanh tab cuộn ngang được (kéo bằng ngón tay / chuột, lăn chuột, nút ‹ ›). Trước đây tab vượt mép phải bị ẩn hẳn khi có nhiều category.

## [0.1.1] - 2026-10-01

### Fixed
- `DevTools.Tick` không còn ném `ArgumentOutOfRangeException` khi một script chạy `tools.cancel-scripts` (duyệt trên bản chụp).
- `DevClock.SetOffset` / `DevAdOutcome.Set` không làm gì khi dev tools chưa kích hoạt; `DevClock.Now` luôn trả giờ thật ở bản phát hành.
- `DevToolsWindow.HasDefine` kiểm tra build target group đang chọn, không chỉ Standalone.
- Sửa chú thích `DevTools.Install(object)` trong `DevRegistry`.

### Added
- `ReleaseBuildGuard`: cảnh báo khi bản build không-development có `DREAMTECH_DEVTOOLS`; `DevToolsSettings.failReleaseBuildWithDefine` biến nó thành lỗi build (mặc định tắt).
- `Runtime/link.xml` giữ assembly của package trước IL2CPP stripping; README ghi rõ module của game cần `[Preserve]` hoặc `link.xml`.

### Changed
- HUD: viên DEV dựng lại nội dung tối đa 4 lần/giây, tab Quick tính chỉ số nhóm một lần, cache Canvas trong `Place`.

## [0.1.0] - 2026-09-26

### Added
- Registry lệnh dùng chung (`DevRegistry`): lệnh có tham số kiểu, toggle, watch, điều kiện, preset, root Inspector,
  gỡ theo owner, log kết quả; `Execute` không bao giờ ném.
- Script: `wait`, `waitfor <điều kiện> [timeout]`, chú thích, một lệnh mỗi frame; boot script từ settings, `-devboot`,
  PlayerPrefs (cửa sổ Editor).
- Port chuẩn và lệnh chuẩn: `ICurrencyPort`, `IInventoryPort`, `ILevelPort`, `IClockPort`, `IAdsPort`,
  `IRemoteConfigPort`, `IExperimentPort`, `ISaveSourcePort`; `DevTools.Install(adapter)` → `IDisposable`.
- `DevClock` (du hành thời gian, offset lưu qua các lần chạy) và `DevAdOutcome` (ép kết quả quảng cáo).
- Module có sẵn: Tools, Time, Ads, Inspector, Engine, Logs, HUD, Data (PlayerPrefs, xoá save ở lần khởi động sau).
- HUD IMGUI: viên DEV (fps, watch ghim, số lỗi), tab theo nhóm, Quick + sao, Scenarios, Watch, Console (gợi ý, lịch sử),
  Log, chặn tap lọt xuống game, co giãn theo màn hình / safe area.
- Cửa sổ Editor `Tools > DreamTech > DevTools` và trang `Project Settings > DreamTech DevTools`.
- Chỉ hoạt động trong Editor, development build, hoặc khi có `DREAMTECH_DEVTOOLS`.
- Kiểm chứng: Unity 2022.3.62f2 và 6000.5.7f1 (xem `Documentation/DESIGN_NOTES.md`).
