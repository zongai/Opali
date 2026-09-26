# Opaline iOS ↔ Windows parity

## Home 真推荐
- TV `tvBrowseRenderer` → `tvSurfaceContentRenderer` → sectionList → **tileRenderer**
- 回退：ANDROID browse → 并行搜索
- 个性化依赖登录后的 TV token + 账户历史；无 Element 批处理时仍可能较空

## SABR
- `streamingData.serverAbrStreamingUrl` 已解析
- `SabrDelivery`：localhost Range 反代（progressive/HLS 优先）
- **UMP 解复用未移植**（`IsUmpImplemented=false`）

## BotGuard PO
- `BotGuardPoTokenClient` 扩展点（本地 mint 不可用）
- 生产路径：`PoTokenService` 远程 `/get_pot`（WEB→ANDROID）

## 播放列表选择器
- `playlist/get_add_to_playlist` + Library 回退
- Watch 页「加入播放列表」列表选择

## 翻译
- Harbor 链：Google → MyMemory → Lingva（+可选 DeepL）
