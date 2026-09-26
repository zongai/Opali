# Opaline iOS ↔ Windows parity (2026-09-26)

Source of truth: `/tmp/opaline-ios-src/Opaline-main` (from `Opaline-main.zip`).

## Aligned in this pass

| Area | iOS | Windows |
|------|-----|---------|
| Home browse | TVHTML5 always (`executeBrowse` / `executeBrowseAnonymous`) | **TVHTML5** always; auth when signed in |
| Home empty shell | — | parallel search fallback, Shorts filtered |
| Shorts | `/reel/reel_watch_sequence` + `ShortsSeed` | same endpoint + cold/videoId seed |
| Comments | protobuf continuation + `/next` | `BuildCommentsContinuation` port + `/next` |
| Subscriptions | TV + bearer | TV + bearer |
| visitorData | session cache from responseContext | `VisitorData` captured on responses |
| Player | Android / multi-client | ANDROID progressive + HLS/DASH DualStream |
| SponsorBlock / RYD | ✓ | ✓ |
| OAuth device | ✓ | ✓ |

## Still partial / not ported

- SABR delivery, full lockup entity feeds for home
- Captions, downloads, mini-player queue
- Server playlist edit depth, channel tabs parity
- PO Token BotGuard minting (stub/remote only)
