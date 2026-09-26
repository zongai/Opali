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
