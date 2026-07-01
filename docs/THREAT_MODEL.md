# Threat Model

## Amaç
İlk dağıtım modeli için tehditleri, uygulanan kontrolleri ve bilinen riskleri listeler.

## Varsayımlar
Public Server internete açıktır. Hermes Worker laptopta private ağdadır ve public inbound port açmaz. Client'lar güvenilmeyen ortam kabul edilir.

## Tehditler ve kontroller
- İstemci UserId sahteciliği: JWT `sub` kullanılır, request profile id görmezden gelinir.
- Worker taklidi: HMAC servis tokenı, allowed worker id, timestamp ve nonce uygulanır.
- Body tampering: HMAC canonical içeriğine body SHA-256 hash dahil edildi.
- Replay: nonce SQLite `WorkerReplayNonces` tablosunda kalıcıdır ve unique `WorkerId+Nonce` ile reddedilir.
- Race condition claim: tek conditional `UPDATE ... RETURNING` ile atomik claim yapılır.
- Duplicate completion: terminal job idempotent döner; usage/audit ikinci kez yazılmaz.
- Worker offline/not-ready: server sahte cevap üretmez; worker not-ready ise claim verilmez.
- Path traversal: worker workspace id relative path ve root containment uygular.
- Log sızıntısı: secret, signature, ham body, stderr içeriği ve tam prompt audit'e yazılmaz.

## Bilinen riskler
SQLite result plaintext saklar. Symlink/junction kaçışı için gelişmiş fiziksel path testi yoktur. Tailscale ACL fiilen bu kodla kurulmaz; operasyonel yapılandırma gerektirir. Global rate limit ve backup encryption tamam değildir.

## Test
Integration testler HMAC body tamper, nonce replay, concurrent claim, duplicate completion, wrong worker, expired lease, not-ready worker, process timeout ve output limit davranışlarını doğrular.