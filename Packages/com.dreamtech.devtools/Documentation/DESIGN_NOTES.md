# Ghi chú thiết kế — DreamTech DevTools

## Nguồn gốc

Bản đầu được viết cho một game tile-match nội bộ (Unity 2022.3, ~200 lệnh, 21 nhóm). Công cụ đó chạy được trên bản
build Windows và được điều khiển bằng AutoPilot. Package này tách phần không phụ thuộc game ra (khoảng 2/3 mã). Phần
gắn với game đó được thay bằng **port chuẩn**: tiền, kho đồ, level, đồng hồ, quảng cáo, remote config, A/B, save.

## Vì sao một registry cho mọi giao diện

Mỗi lệnh chỉ viết một lần và có mặt ở HUD, cửa sổ Editor, script khởi động, preset và bot kiểm thử. Một lệnh mà QA dùng
tay trên điện thoại cũng chính là lệnh CI dùng trong script, nên không có hai cài đặt khác nhau cho cùng một việc.

## Vì sao có port chuẩn

Mọi game mobile đều có ví, kho đồ, level, đồng hồ theo ngày, quảng cáo, remote config, A/B và save. Nếu mỗi game tự đặt
tên lệnh thì QA phải học lại cho từng game. Port cố định tên và hành vi: `economy.set-balance`, `level.win`,
`time.advance-days` giống nhau ở mọi dự án. Phần riêng của thể loại vẫn viết tự do bằng `IDevModule`.

## Các quyết định và lỗi đã gặp

| Quyết định | Lý do / bằng chứng |
|---|---|
| Gate bằng `#if` trong code, không bằng `defineConstraints` của asmdef | Ở game đầu tiên, asmdef có constraint `DEVELOPMENT_BUILD` vẫn được biên dịch vào `Managed/`. Nhưng DLL không có trong `ScriptingAssemblies.json`, nên player không nạp và không `RuntimeInitializeOnLoadMethod` nào chạy. Bỏ constraint xong vẫn thiếu, và chỉ hết khi xoá `Library/PlayerDataCache` cũ. Vì vậy nguyên nhân chắc chắn là cache; vai trò của constraint thì chưa tách được. `#if` cho assembly có mặt ở mọi bản build, nên tránh được cả hai |
| HUD là panel UI Toolkit sortingOrder 32000, không còn blocker uGUI | Panel UITK là một raycaster của EventSystem nên chặn tap dưới nó; `engine.ui-at-point` cho thấy `DevToolsPanelSettings(Clone)` là hit trên cùng. Cũng hết hiện tượng chữ uGUI của game lộ qua panel (panel mặc định đục hẳn). Blocker `Image` cũ phải có `cullTransparentMesh = false` mới raycast được; hướng đó không còn dùng |
| PanelSettings của HUD là asset trong Resources, không tạo lúc chạy | Unity 6000.5: text engine mới cần dữ liệu ICU, chỉ được gán cho PanelSettings asset. PanelSettings tạo bằng `CreateInstance` làm mọi Label ném `NullReferenceException` ở `ComputeNativeTextSize` và log đầy lỗi "ICU Data not available". Asset tạo ở Unity 6 vẫn nạp và dựng đúng trên 2022.3 |
| "Đóng popup" không gọi `CloseAll` của game | Ở game đầu tiên, Home nằm trên layer Normal nên `CloseAll` đóng luôn Home. Package không biết UI của game, nên để lệnh riêng của game (IDevModule) tự làm việc này |
| Mỗi watch có `Interval` riêng; HUD chỉ tính watch đang hiện | Ở game đầu tiên, một watch ray-cast mọi tile được tính 5 lần/giây ở mọi tab, và tab Quick tụt xuống 20 fps |
| Script chạy một lệnh mỗi frame | Game cần một frame để phản ứng. Có `waitfor` cho những gì lâu hơn |
| `DevClock` + offset lưu qua các lần chạy | Game đọc `DateTime.Now` trực tiếp thì không tua được. Nhiều kiểm tra ngày chạy lúc mở game (đăng nhập cộng dồn): ở game đầu tiên, lưu offset rồi khởi động lại làm bộ đếm tăng từ 1 lên 2 |
| `DevAdOutcome` là bảng tra, không phải adapter SDK | Nhánh "fake ad" của game thường luôn thành công. Hai dòng trong wrapper là đủ để chơi thử nhánh lỗi mà không cần mock SDK |
| Phím đọc từ sự kiện IMGUI | `Input.GetKeyDown` ném exception khi project chỉ bật Input System |
| Test HUD cần thiết bị đồ họa | Panel UI Toolkit chỉ dựng khi có GPU: chạy `unity-run.py test playmode --graphics --category DevTools.UI` (batch + GPU chạy được, `-nographics` thì bỏ qua). Ảnh chụp nằm ở `tools/player-smoke.py` trên development build của demo |

## Kiểm chứng (0.1.0)

- `tools/compile-check.py`: core (chỉ netstandard), runtime (dev), runtime (release), editor và demo, với DLL của
  2022.3.62f2 và 6000.5.7f1. Không lỗi, không cảnh báo. 6000 coi obsolete là lỗi.
- EditMode trên 6000.5.7f1 và 2022.3.62f2: 35/35.
- PlayMode: 6000.5.7f1 đạt 9 test, bỏ qua 1 test HUD (IMGUI không chạy ở batch). 2022.3.62f2 đạt 4 test, bỏ qua 1.
  Test của demo chỉ có trên dev project 6000.
- Smoke test trên player (development build Windows của demo, 6000.5.7f1):
  - 33 lệnh chạy qua `-devboot`. Chỉ lệnh cố ý sai thất bại; không có exception.
  - Khi panel mở, blocker là hit trên cùng. Khi đóng panel, nút của game nhận tap.
  - Tua ngày cho phép nhận quà hôm sau.
  - Ép quảng cáo thất bại thì hồi sinh bị từ chối.
  - Có 6 ảnh chụp HUD.
