# Security Model

## Amaç
Server-worker ilk dilimindeki uygulanmış güvenlik kontrollerini ve sınırları açıklar.

## Kimlik
Client kaynak erişimleri JWT'den türetilir; istemci UserId'sine güvenilmez. Worker erişimi `X-Vortex-Worker-Id`, timestamp, nonce ve HMAC signature ile doğrulanır.

## HMAC canonical formatı
Ortak canonical format:

```text
METHOD\nPATH_AND_QUERY\nTIMESTAMP\nNONCE\nBODY_SHA256_BASE64URL
```

Boş body için boş byte dizisinin SHA-256 değeri kullanılır. Server ve Worker `Vortex.Shared.SigningCanonical` helper'ını kullanır. İmza karşılaştırması sabit zamanlıdır.

## Replay koruması
Nonce artık SQLite `WorkerReplayNonces` tablosuna kalıcı yazılır. `WorkerId + Nonce` tekrar kullanılırsa reddedilir. Süresi geçen nonce kayıtları auth sırasında temizlenir.

## Job sahipliği
Worker completion yalnızca işi claim eden worker, aktif status ve geçerli lease ile kabul edilir. Kullanıcı job sonucunu yalnızca JWT owner query ile okuyabilir.

## Secret ve loglama
Worker token, signature, ham body, prompt ve tam result audit loglara yazılmaz. Audit kayıtları id/status/uzunluk seviyesindedir. Process stderr içeriği loglanmaz, sadece uzunluk/hata kodu kullanılır.

## Rate/limit
Plan daily run ve concurrent run kontrolleri server tarafında uygulanır. Geniş HTTP rate limit middleware'i bu dilimde yoktur.

## Bilinen riskler
`AgentJobs.Result` SQLite içinde plaintext saklanır. Symlink/junction hardening ve encrypted backup sonraki dilimde gerekir.