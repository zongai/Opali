# Opali

Cross-platform YouTube client: **Windows (WinUI 3)** + **iOS**.

| Platform | Path | Build |
|----------|------|-------|
| Windows | `Opaline.App` / `Opaline.Core` | GitHub Actions → `Opaline-Windows-x64` |
| iOS | `ios/` | GitHub Actions (Xcode simulator, unsigned) |

## Features (Windows)

- Home / Search / Subscriptions / Shorts / Library
- Playback (progressive, dual-stream, HLS/DASH)
- Comments, SponsorBlock, Return YouTube Dislike
- Channel / Playlist pages, offline download
- **Translation** (Harbor-style chain: Google → MyMemory → Lingva)
  - Translate **title**, **comments**, **captions** on the watch page

Translation engines are ported from [zongai/Harbor](https://github.com/zongai/Harbor) (`TranslationServices` / `TranslationCoordinator`).

## Build locally

### Windows

```powershell
dotnet restore Opaline.Windows.sln
msbuild Opaline.App\Opaline.App.csproj /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:WindowsAppSDKSelfContained=true
```

### iOS

```bash
cd ios
xcodebuild -project Opaline.xcodeproj -scheme Opaline \
  -destination 'generic/platform=iOS Simulator' \
  CODE_SIGNING_ALLOWED=NO build
```

## CI

Push to `main` runs **both** Windows and iOS jobs in parallel (see `.github/workflows/build.yml`).

## Releases

Every push to `main` publishes a GitHub Release tagged `build-<run_number>` with:

| Asset | Description |
|-------|-------------|
| `Opaline-Windows-x64.zip` | Windows self-contained package |
| `Opaline.App.exe` | Windows main executable (use with zip contents) |
| `Opaline-iOS-unsigned.ipa` | Unsigned iOS IPA |
| `Opaline_0.0.<n>.ipa` | Same IPA with version in filename |

See: https://github.com/zongai/Opali/releases
