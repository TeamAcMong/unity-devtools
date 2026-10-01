# DreamTech DevTools — hướng dẫn cho Claude Code

Đây là bộ công cụ play-test: một registry lệnh dùng chung cho HUD trong game, cửa sổ Editor và script. Mã nguồn nằm ở repo
**TeamAcMong/unity-devtools**. Nhánh `main` là dev project Unity 6; package nằm ở `Packages/com.dreamtech.devtools`; game
cài package bằng git URL có tag.

Đọc thêm:
- Cách dùng, port, script: `README.md`.
- Lý do của các luật dưới đây và kết quả kiểm chứng: `Documentation/DESIGN_NOTES.md`.

## Phát triển

- **Sửa trong dev repo, không sửa trong game.** Package cài bằng git là chỉ đọc. Muốn thử trong một game thì tạm trỏ
  manifest của game sang `file:<repo>/Packages/com.dreamtech.devtools`, thử xong đổi lại git URL trước khi commit.
- **Hỗ trợ Unity 2022.3 → Unity 6.**
  - C# tối đa 9: không `record`, không `init`, không file-scoped namespace.
  - Không dùng API chỉ có từ 2023.1+ khi không có nhánh `#if`. Ví dụ đã gặp: `FindObjectsByType` cần nhánh cho 2022.3.
    Từ 6000.5, overload có `FindObjectsSortMode` bị obsolete.
  - `tools/compile-check.py` coi CS0618 / CS0619 là lỗi khi biên dịch với DLL của 6000.x.
- **Mỗi file mới trong package phải có `.meta` được commit.** File `.meta` chỉ do Unity 6 sinh (import dev project).
  Lượt chạy 2022.3 không được làm thay đổi `.meta` nào.

## Kiến trúc và bất biến

Phụ thuộc một chiều: `DreamTech.DevTools` (core) ← `DreamTech.DevTools.Unity` ← `DreamTech.DevTools.Editor`, và ← demo.

- **Core:**
  - `noEngineReferences`, không tham chiếu gì. Không `using UnityEngine`, không thư viện ngoài.
  - `compile-check` phần `core` biên dịch chỉ với netstandard để chặn lỗi này.
  - Thứ gì cần engine thì đặt ở lớp Unity rồi nối vào core qua hook: `DevRegistry.Clock`, event `Executed` /
    `CommandException`, `DevClock.OffsetChanged`.
- **Package không biết game nào:** không tham chiếu asmdef của game, không `Resources.Load` đường dẫn của game, không nhắc
  tên hệ thống của game.
- **Gate:**
  - Mọi file trong `Runtime/Unity` bọc trong `#if UNITY_EDITOR || DEVELOPMENT_BUILD || DREAMTECH_DEVTOOLS`, **trừ**
    `DevToolsSettings.cs`: asset settings phải luôn có script.
  - Không dùng `defineConstraints` của asmdef cho việc gate. Ở game tile-match đầu tiên dùng công cụ này, một asmdef có constraint
    `DEVELOPMENT_BUILD` đã có DLL trong `Managed/` nhưng lại thiếu trong `ScriptingAssemblies.json` của player, nên player
    không nạp nó. Nguyên nhân được xác nhận là cache dữ liệu player cũ (`Library/PlayerDataCache`). Chưa tách được constraint
    có góp phần hay không. Gate bằng `#if` cho ra một assembly có mặt ở mọi bản build (rỗng ở bản phát hành), nên tránh
    được cả hai khả năng. Sau khi đổi asmdef mà player không nạp code thì xoá `PlayerDataCache`.
  - Core luôn được biên dịch và phải rẻ khi không có host: không reflection, không I/O.
- **Port chỉ được thêm, không đổi chữ ký** nếu không bump major. Danh sách: `ICurrencyPort`, `IInventoryPort`,
  `ILevelPort`, `IClockPort`, `IAdsPort`, `IRemoteConfigPort`, `IExperimentPort`, `ISaveSourcePort`. Thành viên mới đi vào
  interface mới.
- **Id lệnh là dữ liệu của người dùng.** Script, preset, sao trong HUD và script AutoPilot/CI lưu theo id. Id =
  `Slug(Category) + "." + Slug(Label)`. **Đổi category / label của lệnh đã phát hành = làm hỏng script của người dùng:**
  giữ id cũ bằng tham số `id:` khi đổi nhãn. Enum `DevAdMode` / `DevAdKind` / `DevParamKind` chỉ thêm giá trị vào cuối.
- **`DevRegistry.Execute` không bao giờ ném exception.**
  - Lỗi tham số (`FormatException`) → `Fail` kèm tên tham số.
  - Exception của code game → `Fail`, đồng thời bắn `CommandException` để lớp Unity log stack.
  - Lệnh bị `Blocked` → `Fail` với lý do.
- **Script chạy một lệnh mỗi `Tick`** (mỗi frame): game phải kịp phản ứng giữa các dòng. `waitfor` quá hạn thì ghi
  TIMEOUT rồi chạy tiếp, không huỷ script.
- **Trùng id → đăng ký sau thắng.** Cài lại cùng adapter → gỡ cái cũ trước. `Remove(owner)` gỡ lệnh, watch, điều kiện,
  root, preset và các category đã rỗng.
- **HUD không được để tap lọt xuống game.**
  - HUD là một panel UI Toolkit có `sortingOrder` 32000, nằm trên mọi canvas của game. Panel tham gia raycast của
    EventSystem (PanelRaycaster) nên tap trên HUD không tới nút uGUI bên dưới, và `IsPointerOverGameObject` đúng trên HUD.
    Không có blocker uGUI riêng nữa. Vùng root của HUD phải `picking-mode: Ignore` để chỗ trống không chặn tap.
  - UXML / USS / theme / PanelSettings nằm trong `Runtime/Unity/Resources/DreamTechDevTools` (Resources trong package chỉ đọc
    vẫn vào player build). **PanelSettings phải là asset**, không `CreateInstance` lúc chạy: Unity 6 chỉ gán dữ liệu ICU của
    text engine cho PanelSettings asset, nếu không mọi Label ném NullReferenceException khi đo. Sửa asset bằng
    `python tools/unity-run.py method DreamTech.DevTools.Editor.HudAssetBuilder.Build`.
  - Mọi màu / cỡ / bo góc nằm ở biến USS trong `DevToolsTheme.uss`; `DevToolsHud.uss` chỉ chứa bố cục. Icon vẽ bằng
    Painter2D (`DevIcon`), không dùng ký tự: font runtime mặc định không có ★ ▾.
  - Phím tắt đọc từ sự kiện IMGUI, không đọc từ `Input`, để chạy được cả khi project chỉ bật Input System.
- **Xoá save** chạy ở `AfterAssembliesLoaded`, trước khi game nạp bất cứ gì. Key của DevTools
  (`DevToolsKeys.All`) và key trong settings được giữ lại. `DevClock.RestoreOffset` được đặt về 0 sau khi xoá.

## Trước khi phát hành

Chạy ma trận trong `CLAUDE.md` ở gốc repo, pass hết rồi mới bump version. Tag là bất biến.
