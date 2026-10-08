# DreamTech DevTools

Bộ công cụ play-test lắp ráp kiểu Lego cho game Unity. Mọi thứ đi qua **một registry lệnh** duy nhất, dùng chung cho các
giao diện:

- **HUD trong game** (UI Toolkit, giao diện tối kiểu app): chạy trên điện thoại, trên bản build và trong Editor. Có viên **DEV**, tab theo nhóm, sao,
  console, log. Chặn tap lọt xuống game.
- **Cửa sổ Editor** (`Tools > DreamTech > DevTools`, Ctrl+Alt+D):
  - Khi Play: chạy lệnh và xem giá trị sống.
  - Khi chưa Play: launcher play-test, preset, xoá save, bật define cho bản QA.
- **Script:** `-devboot "..."`, script lúc khởi động, preset, `DevTools.Execute(line)` cho bot/test tự động.

Tiền, kho đồ, level, đồng hồ, quảng cáo, remote config, A/B test và save cắm vào qua **port chuẩn**. Nhờ vậy mọi game có
cùng bộ lệnh và QA chỉ học một lần. Phần riêng của từng thể loại (solver bàn chơi, spawn wave, bất tử...) là **module tự
viết**.

- Unity **2022.3 → Unity 6**. Phụ thuộc: `com.unity.ugui` và các module có sẵn (imgui, audio, screencapture).
- Core (`DreamTech.DevTools`) là C# thuần, `noEngineReferences`.
- **Chỉ hoạt động trong Editor, development build, hoặc khi có define `DREAMTECH_DEVTOOLS`.** Ở bản phát hành, lớp Unity
  biên dịch thành rỗng: không HUD, không script, không đọc PlayerPrefs.

## Cài đặt

`Packages/manifest.json` của game:

```json
"com.dreamtech.devtools": "https://github.com/TeamAcMong/unity-devtools.git#0.1.0"
```

Không cần làm gì thêm: host tự khởi động khi scene đầu tiên được nạp. Bấm **F1**, hoặc chạm viên **DEV** trên màn hình.

## Nối game vào: một adapter

Viết **một class** implement các port game có, rồi cài nó ở composition root (Awake của bootstrap/GameManager):

```csharp
using DreamTech.DevTools;

public sealed class MyGameDevAdapter : ICurrencyPort, ILevelPort, ISaveSourcePort, IDevModule
{
    readonly Wallet _wallet; readonly LevelFlow _levels; readonly SaveService _save;
    public MyGameDevAdapter(Wallet w, LevelFlow l, SaveService s) { _wallet = w; _levels = l; _save = s; }

    // ICurrencyPort: nên đi qua wallet của game để event và việc lưu vẫn chạy
    public IReadOnlyList<string> CurrencyIds => new[] { "coins", "gems" };
    public long GetBalance(string id) => _wallet.Get(id);
    public void SetBalance(string id, long amount) => _wallet.Set(id, amount, reason: "devtools");

    // ILevelPort
    public int CurrentLevel => _levels.Current;
    public bool IsPlaying => _levels.IsRunning;
    public string StatusText => _levels.State.ToString();
    public void Win() => _levels.ForceResult(true);
    public void Lose() => _levels.ForceResult(false);
    public void Restart() => _levels.Restart();
    public void JumpTo(int level) => _levels.SetCurrent(level);

    // ISaveSourcePort: Inspector xem và sửa được mọi trường
    public IEnumerable<KeyValuePair<string, object>> SaveObjects { get { yield return new("Player", _save.Player); } }
    public void SaveNow() => _save.Flush();

    // IDevModule: lệnh riêng của game này
    public void Register(DevRegistry r)
    {
        r.Toggle("Cheats", "God mode", () => Player.God, v => Player.God = v);
        r.Action("Cheats", "Spawn boss", new[] { DevParam.Int("wave", 10) }, a => { Waves.SpawnBoss(a.Int(0)); return DevResult.Success("spawned"); },
                 blocked: () => _levels.IsRunning ? null : "needs a running level");
        r.Condition("boss-alive", () => Waves.BossAlive);          // cho script: waitfor boss-alive 30
        r.Preset("Rich player", "economy.set-all-balances 99999");  // tab Scenarios
    }
}

// composition root
DevTools.Install(new MyGameDevAdapter(wallet, levels, save));
```

Gọi `Install` ở mọi bản build là an toàn. Ở bản phát hành nó chỉ ghi lệnh vào bộ nhớ (không reflection) và không có gì
chạy chúng. `Install` trả về một `IDisposable`: gọi `Dispose` khi đối tượng game bị huỷ (ví dụ đổi scene) để gỡ lệnh.

Module không cần tham chiếu tới đối tượng game thì chỉ cần gắn `[DevModule]`, có constructor rỗng, là được đăng ký tự động.

**IL2CPP stripping:** module được tìm bằng reflection nên bản build IL2CPP có thể cắt mất kiểu `[DevModule]` của game. Package
đã kèm `link.xml` giữ hai assembly của chính nó; với module nằm trong assembly của game, gắn `[UnityEngine.Scripting.Preserve]`
lên class (hoặc thêm `link.xml` giữ assembly đó).

**Bản phát hành:** `DREAMTECH_DEVTOOLS` chỉ dành cho bản QA. Bản không-development có define này sẽ bị cảnh báo khi build;
bật `failReleaseBuildWithDefine` trong DevToolsSettings để biến cảnh báo thành lỗi build. Ngoài Editor / development build /
define đó, `DevClock` và `DevAdOutcome` không có tác dụng.

## Port chuẩn → lệnh chuẩn

| Port | Nhóm | Lệnh và watch |
|---|---|---|
| `ICurrencyPort` | Economy | số dư từng loại tiền (loại đầu được ghim lên viên DEV), `set-balance`, `add-balance`, `set-all-balances` |
| `IInventoryPort` | Economy | `set-item`, `add-item`, `set-all-items`, watch Items |
| `ILevelPort` | Level | `win`, `lose`, `restart` (bị khoá khi không đang chơi), `jump-to`, `next`, `previous`; điều kiện `playing` / `not-playing` |
| `IClockPort` | Time: *tên* | tua ngày/giờ/phút, `just-before-midnight`, `reset` trên đồng hồ riêng của game (đồng hồ server, live-ops) |
| `IAdsPort` | Ads | `show-rewarded`, `show-interstitial` (kết quả ghi vào Log), watch trạng thái |
| `IRemoteConfigPort` | Remote config | `show-values`, `override`, `clear-override`, `clear-all-overrides` |
| `IExperimentPort` | Experiments | `force-group "exp = group"`, `clear-forced-groups`, watch nhóm hiện tại |
| `ISaveSourcePort` | Save + Inspector | `save-now`; các object save thành root của Inspector |

Id lệnh có dạng `<nhóm>.<nhãn>`, chuyển thành chữ thường và nối bằng gạch ngang (`economy.set-balance coins 500`). Trên
console chỉ cần gõ phần đầu, miễn là phần đó không trùng với lệnh khác.

## Có sẵn cho mọi game

| Nhóm | Nội dung |
|---|---|
| Tools | `help [lọc]`, `script "<a; b>"`, `preset "<tên>"`, `cancel-scripts` |
| Time | Du hành thời gian trên **`DevClock`** (xem bên dưới) |
| Ads | Ép kết quả quảng cáo game tự hiện: Rewarded / Interstitial / Banner → `Normal`, `ForceSuccess`, `ForceFail`, `ForceNoFill` |
| Inspector | `show <root> [path]`, `set <root.path> <giá trị>`, `find <chữ>`: đọc và ghi field/property public bằng reflection (list, dictionary, enum, bool, số, chuỗi) |
| Engine | time scale, pause + step frame, FPS mục tiêu, quality, tắt tiếng / âm lượng, nạp scene, screenshot (có hoặc không HUD), GC, hierarchy, tìm object, **UI at point** (xem UI nào đang nhận hay chặn một tap) |
| Logs | số lỗi / cảnh báo (viên DEV chuyển sang đỏ khi có lỗi), `show-recent` kèm stack, `clear` |
| HUD | mở panel ở một tab, đóng, ẩn toàn bộ, cỡ chữ, độ trong suốt |
| Data | PlayerPrefs get / set / delete, mở thư mục dữ liệu, **xoá save ở lần khởi động sau** |
| Creative | Dọn màn hình để quay / chụp: ẩn **toàn bộ UI game**, ẩn **một canvas** theo tên, ẩn chính viên DEV. Ẩn bằng `Canvas.enabled` (UI vẫn chạy tween / Update), hiện lại đúng những gì dev tools đã ẩn |
| Info | Build, thiết bị, màn hình, phiên chơi, scene, đường dẫn dữ liệu; **Copy report** chép tất cả (kèm mọi giá trị sống của game) vào clipboard để dán vào ticket |

### Viên DEV và thẻ nhanh

- **Chạm** viên DEV: mở **thẻ nhanh** cạnh viên (‹ level ›, ô nhập level + Go, Win / Lose / Restart, các lệnh gắn sao
  không cần tham số). Nút **All tools** mở panel đầy đủ. `PillTap = Panel` trong Settings để chạm là mở panel như cũ.
- **Kéo**: thả tay thì viên trượt về mép trái / phải gần nhất (`PillSnapToEdge`). Thẻ nhanh mở về phía giữa màn hình.
- **Giữ 0.5 s**: gọn (fps + huy hiệu lỗi) ↔ mở rộng (thêm các watch được ghim).
- Đã ẩn toàn bộ HUD: **chạm nhanh 3 lần ở góc trên-trái** (`CornerTapsToShow`) hoặc chạm 3 ngón để hiện lại. Góc này chỉ
  đọc input thô, không nuốt tap của nút game nằm ở đó.

### Kiểu lệnh: màu theo ý nghĩa

`DevCommandStyle` (`Positive` xanh lá ✓, `Danger` đỏ ✕, `Warning` cam ↻) tô nút Run của lệnh, để nhìn là biết, không phải
đọc chữ. Lệnh có `Confirm` mà chưa đặt kiểu sẽ hiện màu Danger. Win / Lose / Restart chuẩn đã có kiểu sẵn.

```csharp
r.Action("Golden Race", "Reset data", ResetData).With(confirm: true);                    // đỏ, hỏi trước
r.Action("Level", "Solve board", Solve).With(quick: true, style: DevCommandStyle.Positive); // xanh, có trong thẻ nhanh
```

### Nhóm UI của game cho tab Creative

```csharp
var handle = DevCreative.AddGroup("Top HUD", () => new Behaviour[] { topHudCanvas });  // thêm công tắc "Show Top HUD"
// ...
handle.Dispose(); // khi scene của nhóm đó unload
```

### Đồng hồ: `DevClock`

Chỗ nào game đang đọc `DateTime.Now` / `UtcNow` / `Today` để tính ngày (quà hằng ngày, chuỗi đăng nhập, ưu đãi có hạn,
mùa giải) thì đổi sang đọc `DevClock.Now` / `UtcNow` / `Today`. Khi không có offset, giá trị trả về đúng bằng
`DateTime.Now`. Sau khi đổi, `time.advance-days 1` làm "ngày mai" test được mà không phải chỉnh giờ máy:

- Offset được lưu qua các lần chạy (chỉ trong bản dev).
- Kiểm tra ngày chạy lúc mở game thì cần khởi động lại sau khi tua.
- Kiểm tra lúc nửa đêm test bằng `time.just-before-midnight`.

Game đã có đồng hồ riêng (đồng hồ server) thì implement `IClockPort` cho đồng hồ đó.

### Quảng cáo: `DevAdOutcome`

Chèn hai dòng vào wrapper quảng cáo của game:

```csharp
if (DevAdOutcome.TryIntercept(DevAdKind.Rewarded, out bool granted)) { onDone(granted); return; }
bool ready = DevAdOutcome.IsReady(DevAdKind.Rewarded, sdk.IsRewardedReady);
```

Chế độ `Normal` (mặc định, và luôn là `Normal` ở bản phát hành) không chặn gì. Các chế độ còn lại cho phép chơi thử nhánh
lỗi: hồi sinh bị từ chối, popup báo hết quảng cáo.

## Script

Một script là các dòng lệnh console, ngăn cách bằng `;` hoặc xuống dòng. Có thêm hai lệnh dựng sẵn:

- `wait <giây>`
- `waitfor <điều kiện> [timeout=60]`

Dòng bắt đầu bằng `#` hoặc `//` là chú thích. Mỗi frame chạy một lệnh, để game kịp phản ứng giữa các dòng.

```
demo.start-level
wait 0.5
level.lose
waitfor revive-offer 5
ads.rewarded-outcome ForceFail
demo.revive-with-ad
ads.rewarded-outcome Normal
```

Script khởi động chạy theo thứ tự sau:

1. `BootScript` trong settings.
2. Tham số dòng lệnh `-devboot "..."`.
3. "Run this script on every play" của cửa sổ Editor.
4. Các nút ▶ của cửa sổ Editor (chạy một lần).

## Settings

Mở `Project Settings > DreamTech DevTools` và bấm tạo `Assets/Resources/DevToolsSettings.asset`. Không có asset thì mọi
mục dùng mặc định. Các mục:

- Tiêu đề HUD.
- Phím mở / ẩn (mặc định F1, `` ` `` và F2).
- Số ngón của cú chạm ẩn/hiện HUD (mặc định 3).
- Chạm viên DEV mở gì (`PillTap`: thẻ nhanh hoặc panel), viên tự dính mép (`PillSnapToEdge`), số lần chạm góc trên-trái
  để hiện lại HUD đã ẩn (`CornerTapsToShow`, mặc định 3).
- Ẩn HUD khi khởi động.
- Ghi lệnh ra Console.
- Boot script.
- Preset dùng chung cho cả dự án.
- Key PlayerPrefs và thư mục được giữ lại khi xoá save.
- Scene mà nút Play mở trước.
- Đường dẫn player cho nút "Run player with this script".

Class settings được biên dịch ở mọi bản build, nên asset không bao giờ báo thiếu script.

## Bản QA không phải development build

Bật `DREAMTECH_DEVTOOLS` trong cửa sổ DevTools hoặc trang Project Settings (áp cho Standalone, Android, iOS). **Tắt trước
khi phát hành.**

## Giới hạn

- **HUD:** dùng UI Toolkit, tự mang UXML / USS / PanelSettings trong package (không cần asset trong project). Cần
  `com.unity.modules.uielements`. Tap tới HUD qua EventSystem của game như mọi UI Toolkit runtime (project chỉ bật Input System cần `InputSystemUIInputModule`). HUD được kiểm bằng
  `unity-run.py test playmode --graphics` và smoke test trên player (`tools/player-smoke.py` của repo).
- **Chặn tap:** HUD chặn được uGUI và những game hỏi `EventSystem.IsPointerOverGameObject`. Game đọc input thô thì gọi
  `DevToolsHud.IsPointerOverHud(screenPos)`.
- **Inspector:** chỉ ghi được số, bool, chuỗi và enum. Không ghi được member của struct lồng trong một struct khác.
- **Phím tắt:** đọc từ sự kiện IMGUI (`OnGUI` chỉ còn làm việc này) nên chạy với cả Input Manager lẫn Input System. Cú chạm nhiều ngón dùng Input System
  khi Input Manager bị tắt.
- **Phiên bản đã kiểm:** Unity 2022.3.62f2 và 6000.5.7f1. Các bản 6000.0–6000.4 chưa kiểm.
