# Changelog

Định dạng theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), phiên bản theo [SemVer](https://semver.org/).

## [Unreleased]

### Fixed
- **Điện thoại: chạm bị nhận thành kéo và danh sách nhảy lung tung.** `ScrollView` của UI Toolkit tự kéo cảm ứng từ ~10 px, kể cả khi đang đè lên một nút, nằm gọn trong độ rung của một cú chạm (ngưỡng của HUD ~20 px theo DPI), rồi hai cơ chế cùng cuộn nên nội dung giật. Thanh tab và danh sách giờ dùng **`DevScrollView`** tự viết (khung cắt + nội dung dịch bằng translate + lăn chuột + thanh vị trí), không có kéo cảm ứng riêng: chỉ còn `DragScrollManipulator`.
- **Điện thoại: ô nhập level trên thẻ nhanh bị trả về số cũ.** Thẻ đồng bộ ô về level hiện tại mỗi 0.2 s khi "không đang gõ", mà trạng thái gõ được đọc từ focus UI, thứ bàn phím hệ điều hành không giữ. Ô giờ là ô chữ với bàn phím số; đã chạm / gõ thì không bị đồng bộ hay dựng lại cho tới khi Go / bước level / đóng thẻ. Go blur trước để nhận số vừa gõ, số không hợp lệ thì báo lỗi trên thẻ thay vì nhảy.

### Added
- Test PlayMode với ngưỡng cỡ điện thoại (40 px) và chạm `isPrimary` như ngón tay thật: rung trên thẻ / nút Run / tab không cuộn và vẫn bấm được, kéo bám đúng ngón tay sau ngưỡng; lăn chuột; ô level giữ số đang gõ và Go nhảy đúng.

## [0.4.0] - 2026-10-08

### Added
- **Thẻ nhanh** cạnh viên DEV (chạm để mở): ‹ level ›, ô level + Go, Win / Lose / Restart, lệnh gắn sao không tham số, nút **All tools** mở panel. Mở về phía giữa màn hình, luôn nằm trong vùng an toàn. `DevToolsSettings.PillTap` chọn thẻ nhanh hoặc panel; `DevToolsHud.ShowQuickCard` / `IsQuickCardOpen`; lệnh `hud.quick-card`.
- Viên DEV **tự dính mép** trái / phải khi thả (`PillSnapToEdge`, mặc định bật).
- **`DevCommandStyle`** (`Positive` / `Danger` / `Warning`) tô màu và icon nút Run; lệnh `Confirm` chưa đặt kiểu hiện Danger. `DevCommand.With(quick, confirm, style)`. Win / Lose / Restart chuẩn đã có kiểu.
- Module **Info**: build, thiết bị, màn hình, phiên, scene, đường dẫn; **Copy report** chép toàn bộ (kèm mọi watch) vào clipboard; **Log report**.
- Module **Creative**: ẩn toàn bộ UI game (canvas + UIDocument), ẩn một canvas theo tên, ẩn viên DEV; chỉ khôi phục đúng những gì đã ẩn. `DevCreative.AddGroup(name, targets)` cho game đặt tên nhóm UI.
- HUD đã ẩn: **chạm nhanh 3 lần góc trên-trái** để hiện lại (`CornerTapsToShow`), bên cạnh cú chạm 3 ngón.
- Icon mới: dấu tích, tải lại, mũi tên trái / phải, mở rộng.
- Viên DEV gọn thành **quả bóng tròn** kiểu AssistiveTouch: thân tối, quầng sáng, vòng màu nhấn, icon thanh trượt; lún khi bấm, đỏ + huy hiệu ở góc khi có lỗi; cách mép 6 px. Giữ 0.5 s vẫn mở rộng thành thanh (icon + fps + watch ghim).
- Thẻ nhanh **phóng ra từ phía quả bóng** (mờ + phóng to, 0.16 s).
- **Icon cho tab và tiêu đề nhóm** (cờ, xu, play, thanh trượt, đồng hồ, bánh răng, dữ liệu, ảnh, mắt, terminal, info, cúp, tim, quà, người, giỏ hàng); game đặt icon cho nhóm riêng bằng `DevToolsHud.SetCategoryIcon(category, "trophy")`.

### Changed
- Viên DEV và thẻ nhanh được đặt vị trí bằng `translate` thay vì `left` / `top`: không còn vòng lặp layout khi viên nằm sát mép phải.
- Bản build smoke của demo bật Run In Background khi build (không đổi ProjectSettings), để smoke không đứng khi cửa sổ mất focus.

### Fixed
- Chạm trên điện thoại không còn bị nhận nhầm là kéo: ngưỡng kéo theo DPI (~2.5 mm ngón tay, tối thiểu 8 px logic) cho thanh tab, danh sách và viên DEV; rung lệch trục huỷ cú kéo.
- Danh sách không còn giật một đoạn bằng ngưỡng khi bắt đầu kéo; quán tính chỉ chạy với cú vuốt thật (900 px/s, thả trong 50 ms).

## [0.3.1] - 2026-10-05

### Changed
- Viên DEV gọn hơn: mặc định chỉ có chấm trạng thái + số fps (vd `● 60`); khi có lỗi console hiện thêm huy hiệu tròn đỏ
  chứa số lỗi (tối đa `99+`). Viên vẫn ngả sắc đỏ nhẹ khi có lỗi nhưng không dài ra.

### Added
- Nhấn giữ viên DEV ~0,5 giây (giờ không scale, không kéo quá ngưỡng kéo) để chuyển Compact / Expanded: Expanded hiện thêm
  watch đã ghim như bản cũ. Có hiệu ứng phồng + sáng viền khi đang giữ; thả tay sau khi chuyển không mở panel. Chạm ngắn vẫn mở
  panel, kéo vẫn di chuyển viên (và không kích hoạt nhấn giữ). Trạng thái lưu ở PlayerPrefs (`DevToolsKeys.HudPillExpanded`, nằm
  trong `DevToolsKeys.All` nên sống sót khi xoá save).
- `DevToolsSettings.PillStyle` (Compact / Detailed, mặc định Compact): kiểu ban đầu khi chưa có giá trị PlayerPrefs. Asset cũ
  đọc vẫn đúng.
- `DevToolsHud.PillExpanded`; lệnh `hud.pill-expanded` và `logs.log-test-error` (ghi một lỗi để thử huy hiệu).
- Viên DEV tự kẹp lại vào safe area khi đổi độ rộng; chuỗi dựng bằng StringBuilder dùng lại, chỉ gán lại text khi đổi.
- Test PlayMode `DevTools.UI` (`HudPillTests`): chạm mở panel, nhấn giữ bật/tắt Expanded và không mở panel, kéo không bật/tắt,
  nội dung compact so với expanded, huy hiệu lỗi `99+`.

## [0.3.0] - 2026-10-02

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
- HUD: kéo để cuộn thanh tab và danh sách bằng mọi loại con trỏ (cảm ứng, chuột, bút) qua `DragScrollManipulator`; trước đây nút đang bấm giữ capture nên kéo bắt đầu trên tab / Run không cuộn được. Kéo không kích hoạt nút bên dưới, chạm vẫn hoạt động; có quán tính theo thời gian không scale.
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
