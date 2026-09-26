# Opaline iOS ↔ Windows parity

## SABR UMP demux (ported)
- `UmpReader` — UMP varint framing (type/size/payload)
- `SabrProtobuf` — write/read for VideoPlaybackAbrRequest
- `MediaHeader` — MEDIA_HEADER fields (itag, seq, init, timing)
- `SabrSegmentCollector` — MEDIA / MEDIA_END / redirect / error
- `SabrRequestBuilder` — startup + segment (Android clientInfo)
- `SabrSession` — POST serverAbrStreamingUrl, accumulate video/audio fMP4
- `SabrDelivery` — localhost Range server for demuxed buffers
- WatchPage: `ServerAbrStreamingUrl` + `VideoPlaybackUstreamerConfig`
- StreamUrlResolver tries UMP before HLS/progressive when config present

## Still partial
- Full TV sabrAbrState / playback cookie round-trip
- Continuous background segment fetch while seeking
- BotGuard local PO (remote /get_pot only)
