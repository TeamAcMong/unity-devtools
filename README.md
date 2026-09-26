# unity-devtools

Dev project của **DreamTech DevTools** (`com.dreamtech.devtools`). Đây là bộ công cụ play-test lắp ráp kiểu Lego cho game
Unity:

- HUD trong game (điện thoại, bản build, Editor).
- Cửa sổ Editor.
- Script tự động.

Cả ba dùng chung một registry lệnh. Tiền, kho đồ, level, đồng hồ, quảng cáo, remote config, A/B và save cắm vào qua port
chuẩn, nên mọi game có cùng bộ lệnh. Phần riêng của từng thể loại là module tự viết.

- Hướng dẫn dùng: [Packages/com.dreamtech.devtools/README.md](Packages/com.dreamtech.devtools/README.md)
- Thiết kế và kiểm chứng: [Documentation/DESIGN_NOTES.md](Packages/com.dreamtech.devtools/Documentation/DESIGN_NOTES.md)
- Phát triển package: [CLAUDE.md](CLAUDE.md)

```json
"com.dreamtech.devtools": "https://github.com/TeamAcMong/unity-devtools.git#0.1.0"
```

Muốn thử demo: mở project bằng Unity 6000.5, vào `Assets/Demo/DevToolsDemo.unity`, bấm Play, rồi bấm **F1** hoặc chạm
viên **DEV**.
