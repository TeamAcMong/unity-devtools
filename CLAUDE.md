# unity-devtools — hướng dẫn cho Claude Code

Repo này là **dev project Unity 6** của một UPM package (`com.dreamtech.devtools`), không phải game. Thứ được phát hành
là thư mục `Packages/com.dreamtech.devtools/`. Game cài bằng `https://github.com/TeamAcMong/unity-devtools.git#<tag>`.

**Luật của package (kiến trúc, bất biến, quy ước code) nằm ở
[`Packages/com.dreamtech.devtools/CLAUDE.md`](Packages/com.dreamtech.devtools/CLAUDE.md). Đọc file đó trước khi sửa bất
cứ gì trong package.**

## Bố cục

| Đường dẫn | Là gì | Đi theo package? |
|---|---|---|
| `Packages/com.dreamtech.devtools/` | Package | ✅ |
| `Assets/Demo/` | Game demo "tap the target" nối đủ port, scene sinh bằng code, test PlayMode, build Windows cho smoke test | ❌ |
| `tools/` | compile-check, chạy Unity batch, project tạm 2022.3, smoke test trên player | ❌ |
| `deploy.sh`, `DEPLOY_UPM_SUBTREE.md` | Phát hành bằng subtree split + tag | ❌ |
| `CHANGELOG.md` (gốc) | Bản sao CHANGELOG của package: sửa cả hai cùng lúc | ❌ |

## Luật riêng của repo

- **Unity:**
  - Dev project mở bằng **6000.5.7f1**.
  - Package phải chạy từ **2022.3.62f2**.
  - Code `Assets/Demo` chỉ chạy trên dev project, nên được dùng API Unity 6.
- **Mọi file mới trong package phải có `.meta` được commit.** Sinh `.meta` bằng `python tools/unity-run.py import` (Unity
  6). Không mở dev project và project tạm 2022.3 **cùng lúc**: cả hai sẽ sinh `.meta` với GUID khác nhau.
- **Scene demo là sản phẩm sinh ra.** Muốn đổi bố cục thì sửa `Assets/Demo/Editor/DemoSceneBuilder.cs` rồi dựng lại.
  UI của demo được tạo trong code (`DemoGame.BuildUi`).
- **Commit:** conventional commits `type(scope): mô tả` bằng tiếng Việt. Scope: `devtools`, `demo`, `docs`, `release`,
  `tools`.
- **Không gọi Unity trực tiếp:** dùng `tools/unity-run.py`. Script này có hạn giờ, từ chối chạy khi project đang mở, và
  lưu log + XML kết quả vào `tools/.cache/runs`.
- **Compile trước khi mở Unity:** `python tools/compile-check.py`. Script này dùng Roslyn C# 9 với DLL của 2022.3 và
  6000.5. Phần core chỉ có netstandard; runtime biên dịch cả bản dev lẫn bản release; bản 6000 coi CS0618 / CS0619 là lỗi.
  `UnityEngine.UI.dll` lấy từ `Library/` của dev project (6000) và của project tạm (2022).
- **Test HUD cần GPU:** panel UI Toolkit chỉ dựng khi có thiết bị đồ họa. Test category `DevTools.UI` tự bỏ qua khi
  `-nographics`; chạy `python tools/unity-run.py test playmode --graphics --category DevTools.UI`. Ảnh chụp thật: dựng
  player rồi `tools/player-smoke.py` (xem `tools/.cache/smoke`).

## Ma trận test trước khi phát hành

```bash
python tools/compile-check.py                                   # 2022 + 6000: core, runtime, release, editor, demo

# Unity 6 — dev project
python tools/unity-run.py method DreamTech.DevTools.Demo.EditorTools.DemoSceneBuilder.BuildFromCommandLine
python tools/unity-run.py test editmode
python tools/unity-run.py test playmode
python tools/unity-run.py method DreamTech.DevTools.Demo.EditorTools.DemoBuild.BuildWindows
python tools/player-smoke.py                                    # HUD + lệnh trên player, ảnh ở tools/.cache/smoke

# Unity 2022.3 — project tạm (manifest trỏ file: vào package)
python tools/make-temp-project-2022.py --bootstrap
python tools/unity-run.py test editmode --unity 2022
python tools/unity-run.py test playmode --unity 2022
git status --short -- Packages                                  # lượt 2022.3 không được làm đổi .meta nào
```
