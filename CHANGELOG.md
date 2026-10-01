# Changelog

Định dạng theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), phiên bản theo [SemVer](https://semver.org/).

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
