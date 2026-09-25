# Opaline for Windows

完整移植 — 基于 [Opaline](https://github.com/verback2308/Opaline) iOS 客户端的 WinUI 3 桌面版。

> **当前环境限制**：本仓库在 Linux 沙箱中生成，**无法在此编译/运行**。请在 Windows 10/11 上打开并构建。

## 功能清单（v0.2）

| # | 功能 | 状态 |
|---|------|------|
| 1 | **流签名 / n-parameter / PO Token** | ✅ `SignatureTimestampService` + `NSolverService` + `PoTokenService` + `StreamUrlResolver` |
| 2 | **自适应流选择** | ✅ progressive 优先；adaptive 返回 video+audio 对（播放器目前播 progressive / video URL） |
| 3 | **OAuth 设备码登录** | ✅ `OAuthClient` + Settings 登录 UI + Bearer 注入 InnerTube |
| 4 | **SponsorBlock** | ✅ 拉取片段 + 播放时自动跳过 |
| 4b | **Return YouTube Dislike** | ✅ 点赞/踩数显示 |
| 5 | **InnerTube 解析增强** | ✅ `signatureCipher` 解码、auth 头、Shorts 过滤 |
| 6 | **Shorts 竖滑** | ✅ `FlipView` 垂直浏览 + 无限加载 |
| 6b | **Library** | ✅ 本地观看历史 + Watch Later |

## 架构

```
Opaline.Core/          # 无 UI 依赖
  Api/                 # InnertubeClient (+ Auth partial)
  Auth/                # OAuth 设备码
  Playback/            # STS / n-solver / pot / StreamUrlResolver
  Services/
    SponsorBlock/
    Ryd/
  Storage/             # OAuth tokens + history
  Models/
  Config/AppUrls.cs

Opaline.App/           # WinUI 3
  Views/ ViewModels/ Controls/ Services/ Converters/
```

## 在 Windows 上构建

```powershell
cd Opaline.Windows
dotnet restore
dotnet build Opaline.App\Opaline.App.csproj -c Debug -p:Platform=x64
```

前置：.NET 8 SDK、Developer Mode、Windows 10 1903+。

## 播放管线（v0.3）

1. **HLS / DASH 清单** → `AdaptiveMediaSource`（系统级自适应，音视频一体）
2. **Progressive MP4** → 单 URL
3. **Adaptive 分离流** → `DualStreamPlayer` 双 `MediaPlayer` 同步（视频轨静音 + 独立音轨，漂移 >80ms 自动校正）
4. **签名** → `signatureCipher` 的 `s` + URL 的 `n` 一并送远程 `/solve`，再附加 `pot`

### 仍需注意

- 远程 solver 不可用时，带 `s` 的 WEB 流会 403（可在 Settings 配置 solver 地址）
- YouTube clientVersion / player JS 会定期变化

## License

GNU GPL v3.0（与上游一致）。
