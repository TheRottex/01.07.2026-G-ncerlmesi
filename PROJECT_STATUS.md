# PROJECT STATUS

Tarih: 2026-07-01

## Mevcut korunan durum

- Web kayıt/giriş, Desktop callback ve JWT kullanıcı bağlamı korunmuştur.
- Kullanıcıdan gelen `UserId` veya `RequestedProfileId` değerine güvenmeyen profil izolasyonu korunmuştur.
- Plan politikaları, günlük agent run limiti, kullanım sayaçları ve audit kayıtları server tarafında işletilmeye devam eder.

## Bu aşamanın hedefi

Yeni özellik eklemeden mevcut Server–Worker ilk dilimindeki güvenlik ve eşzamanlılık sorunları düzeltildi.

## Tamamlananlar

- Worker HMAC canonical formatına request body SHA-256 hash eklendi.
- Server ve Worker ortak `Vortex.Shared.SigningCanonical` helper'ını kullanır.
- İmza karşılaştırması sabit zamanlı yapılmaya devam eder.
- Worker replay nonce koruması SQLite `WorkerReplayNonces` tablosuna taşındı; `WorkerId + Nonce` tekrar kullanımı reddedilir.
- AgentJob claim işlemi tek conditional `UPDATE ... RETURNING Id` mantığına taşındı.
- Claim sırasında status, lease, cancellation ve attempt koşulları update içinde tekrar kontrol edilir.
- Server not-ready worker'a iş vermez.
- Worker not-ready ise claim isteği göndermez.
- Completion yalnızca işi claim eden worker, aktif status ve geçerli lease ile kabul edilir.
- Terminal job completion idempotent hâle getirildi; duplicate completion usage/audit'i ikinci kez artırmaz/yazmaz.
- Completion status update, usage ve audit aynı transaction içinde yapılır.
- Worker Hermes process execution `MaxRunSeconds` timeout kullanır.
- Timeout/cancellation durumunda process tree sonlandırılır.
- Worker stdout/stderr okumaları bounded hâle getirildi.
- `Worker:MaxStdoutBytes` / `VORTEX_WORKER_MAX_STDOUT_BYTES` ve `Worker:MaxStderrBytes` / `VORTEX_WORKER_MAX_STDERR_BYTES` desteklenir.
- NotConfigured readiness durumu gerçek heartbeat state olarak üretilebilir.
- Güvenlik ve eşzamanlılık regression testleri eklendi.
- İlgili belgeler ve `CODEMAP.md` güncellendi.
- `graphify update .` çalıştırıldı.

## Henüz yapılmayanlar

- Gerçek Tailscale ACL/grants kurulumu yapılmadı.
- Gerçek Hermes binary/credential olmadığı için uçtan uca gerçek model yürütmesi manuel doğrulanmadı.
- Google OAuth ve GitHub OAuth eklenmedi.
- Sesli sohbet, TTS, maskot, ağız animasyonu veya gelişmiş UI eklenmedi.
- Worker-side automation scheduler uygulanmadı.
- Büyük input/result için `InputReference`/`ResultReference` dış depolaması tamamlanmadı.
- Tam storage quota pre/post write enforcement ve reconciliation görevi tamamlanmadı.
- Symlink/junction kaçışlarına karşı kapsamlı filesystem hardening hâlâ yok.
- Production forwarded headers, global rate limit, refresh token rotasyonu ve backup encryption henüz tamam değil.

## Çalışan servisler / bileşenler

- `Vortex.Server`: build ve test ortamında çalışıyor.
- `Vortex.HermesWorker`: derleniyor; process timeout/output limitleri test helper process ile doğrulandı.
- Server-worker API sözleşmesi: integration test ile simüle worker üzerinden doğrulandı.
- Job queue: atomik claim, heartbeat, guarded completion, owner-only status, duplicate completion ve expired lease davranışı test edildi.

## Sahte veya doğrulanmamış kalan parçalar

- Gerçek Hermes runtime çağrısı kodda process adapter olarak mevcut, ancak gerçek `HERMES_EXECUTABLE_PATH` ile canlı Hermes model yürütmesi doğrulanmadı.
- Testlerde worker HTTP davranışı simüle edildi; gerçek Tailscale ağı üzerinde canlı server-worker bağlantısı doğrulanmadı.
- Mevcut in-memory Hermes gateway yalnızca eski test/metadata uyumluluğu için config ile açılabilir; production default bu gateway'i kullanmaz.

## Gerekli secret ve yapılandırmalar

Server:

```text
Jwt:SigningKey
Worker:AllowedWorkerId
Worker:ServiceToken
Worker:ChatWaitSeconds
```

Worker/laptop:

```text
VORTEX_SERVER_URL
VORTEX_WORKER_ID
VORTEX_WORKER_TOKEN
VORTEX_WORKER_DATA
HERMES_EXECUTABLE_PATH
VORTEX_WORKER_MAX_STDOUT_BYTES
VORTEX_WORKER_MAX_STDERR_BYTES
```

## Migration durumu

`VortexDb.InitializeAsync` içinde lightweight SQLite migration/schema bootstrap geçerlidir:

- `UserAgentProfiles` için `WorkerId`, `WorkspaceId`, `PlanId`, `StorageQuotaBytes`, `StorageUsedBytes`, `ProfileStatus`, `DisabledAt`, `LastUsedAt` kolonları garanti edilir.
- Free plan kotası eski düşük değerlerden 5 GB'a yükseltilir.
- `WorkerRegistrations`, `WorkerReplayNonces`, `AgentJobs` ve ilgili indexler oluşturulur.

Ayrı EF migration sistemi yoktur; mevcut proje SQLite schema bootstrap yaklaşımını kullanır.

## Test sonuçları

Çalıştırılan komutlar:

```bash
dotnet build VortexAI.sln -c Release
dotnet test VortexAI.sln -c Release --no-build
graphify update .
```

Sonuç:

```text
Build: başarılı, 0 uyarı, 0 hata
Test: başarılı, 14/14 test geçti
Graphify: AST update başarılı, 1247 nodes / 2221 edges / 97 communities
```

## Bilinen güvenlik riskleri

- `AgentJobs.Result` şu dilimde SQLite içinde plaintext saklanır.
- Tailscale ACL politikası kodla uygulanmadı; operasyonel ortamda ayrıca kurulmalıdır.
- Worker workspace path doğrulaması temel traversal/root containment içerir; symlink/junction hardening eklenmelidir.
- Storage quota hâlâ dosya yazma katmanında tam enforce edilmez.
- Global HTTP rate limit ve encrypted backup tamam değildir.

## Sonraki kesin adım

Gerçek Hermes veya Tailscale entegrasyonuna geçmeden önce yalnızca workspace filesystem hardening ve storage quota write enforcement dilimini planlamak.
