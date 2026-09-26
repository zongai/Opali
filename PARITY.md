# Opaline iOS ↔ Windows parity

## BotGuard / PO Token
- **WebView2BotGuardMinter**: hidden WebView2 host, YouTube origin, JS mint attempt
  (botguard.bg / attestation Create probe)
- **PoTokenService**: local WebView2 first → remote bgutil `/get_pot` fallback
- Same conclusion as iOS: GVS often rejects pure on-device tokens; remote remains primary reliability path

## SABR TV abr + cookie
- `AbrStateTv` field-for-field (viewport, DRC, decodeCeilings, trackAuthorization)
- `nextRequestPolicy` (UMP 35): playbackCookie field 7 → streamerContext field 3 on next request
- backoffMs field 4; request `&rn=` sequence; seek drops held range

## SABR UMP
- Full UMP reader + session + localhost fMP4 (see prior commit)

## Home / Playlist / Translation
- See earlier commits


## SABR without ANDROID ustreamerConfig
- Deep-scan player JSON for videoPlaybackUstreamerConfig / onesieUstreamerConfig
- Re-fetch /player as TV then WEB and merge SABR fields when ANDROID omits them
- Onesie config used as SABR request field-5 fallback

## Onesie
- iOS: extracts `onesieUstreamerConfig` only — **no separate Onesie HTTP delivery**
- Windows: `OnesieConfigResolver` priority = videoPlayback → onesie; feeds SABR field 5
- SABR still requires `serverAbrStreamingUrl` + a resolvable ustreamer blob

## HLS 自建 / SIDX
- `SidxParser` — ISO BMFF sidx box (iOS HLSGenerator+Sidx)
- `HlsPlaylistGenerator` — media (EXT-X-MAP + BYTERANGE) + master (AUDIO group)
- `HlsSelfBuiltDelivery` — Range fetch index, local master.m3u8; segments use absolute googlevideo URLs
- StreamUrlResolver: after official HLS/DASH, before progressive

## AV1 / Auto-dub / Offline Watch
- `Av1Support`: preference gate for av01 in adaptive ladder (Windows MF decode)
- `AutoDubPreference`: language match + ignore AI `.10` tracks; used in SelectBestAdaptive
- Offline: `IDownloadService.TryGetLocalMediaPath`; Watch load failure → local file playback
- Settings: Prefer AV1, Auto-dub, Ignore AI dubs, language code

## AutoDubSource probe chain
- `FetchAudioTrackListAsync` via IOS `/player` (no pot), distinct audioTrack.id
- `AutoDubProbe` deadline (~400ms) races listing; `AutoDubPreference.AutoDubTrack`
- Original = `*.4`, AI dub = `*.10`; StreamUrlResolver commits preferred track id before adaptive resolve
