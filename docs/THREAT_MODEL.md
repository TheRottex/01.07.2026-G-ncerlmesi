# Threat Model

## Amaç
İlk dağıtım modeli için tehditleri, uygulanan kontrolleri ve bilinen riskleri dürüstçe listeler.

## Varsayımlar
Public Server internete açıktır. Hermes Worker laptopta private ağdadır ve public inbound port açmaz. Client'lar güvenilmeyen ortam kabul edilir.

## Tehditler ve kontroller
- İstemci UserId sahteciliği: JWT `sub` kullanılır, request profile id görmezden gelinir.
- Worker taklidi: HMAC servis tokenı, allowed worker id, timestamp ve nonce uygulanır.
- Replay: nonce belleği ve zaman penceresi vardır.
- Worker offline: server sahte cevap üretmez; iş queued kalır.
- Path traversal: worker workspace id relative path kontrolü ve root containment uygular.
- Log sızıntısı: secret ve stderr içeriği yerine uzunluk/id loglanır.

## Bilinen riskler
Nonce storage in-memory olduğu için server restart sonrası replay penceresi tam kalıcı değildir. SQLite result plaintext saklar. Symlink/junction kaçışı için ek test yoktur. Tailscale ACL fiilen bu kodla kurulmaz; operasyonel yapılandırma gerektirir.

## Test
Integration testler auth/owner/queue kontrolünü doğrular. Penetrasyon testi yapılmadı.