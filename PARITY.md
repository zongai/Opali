# Opaline iOS ↔ Windows parity (2026-09-26)

## Implemented / extended this pass

| Area | Status |
|------|--------|
| Home true feed | TV → ANDROID → search fallback; tileRenderer parse (TV shelves) |
| Shorts | /reel/reel_watch_sequence |
| Comments | protobuf continuation |
| Captions | player tracks + IOS client fallback (no pot timedtext) |
| Downloads | progressive file download to LocalAppData |
| Channel Tabs | Videos / Shorts / Live / Playlists via ChannelTabParams |
| Playlist edit | browse/edit_playlist add/remove video |
| Mini player + queue | bottom bar + PlaybackQueue |
| PO Token | remote /get_pot on stream URLs (WEB then ANDROID) |
| SABR | stub only (SabrDelivery.IsSupported=false) — needs local UMP proxy |
| BotGuard PO mint | not local; remote provider only |

## Still hard / not full

- Personalized home without Element protocol / account history tiles
- Full SABR + LocalMediaServer
- On-device BotGuard (requires WebView JS challenge stack)
- Full playlist UI (add-to-playlist picker)
