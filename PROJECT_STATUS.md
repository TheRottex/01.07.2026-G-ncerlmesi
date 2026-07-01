# PROJECT STATUS

Tarih: 2026-07-01

## Mevcut korunan durum

- Web kayıt/giriş, Desktop callback ve JWT kullanıcı bağlamı korunmuştur.
- Kullanıcıdan gelen `UserId` veya `RequestedProfileId` değerine güvenmeyen profil izolasyonu korunmuştur.
- Plan politikaları, günlük agent run limiti, kullanım sayaçları ve audit kayıtları server tarafında işletilmeye devam eder.

## Bu aşamanın hedefi

İlk dağıtım dilimi uygulandı: public `Vortex.Server` üzerinde kalıcı AgentJob kuyruğu ve laptop üzerinde çalışacak `Vortex.HermesWorker` pull modeli hazırlandı.

## Tamamlananlar

- `AgentJobs` kalıcı iş kuyruğu eklendi.
- Worker claim/lease mekanizması eklendi.
- Worker heartbeat ve `/health/worker` readiness endpoint'i eklendi.
- Worker endpointleri için HMAC servis kimliği, timestamp ve nonce replay kontrolü eklendi.
- User-HermesProfile-Workspace mapping'i opaque workspace id ile genişletildi.
- Free plan storage quota seed/migration değeri 5 GB'a yükseltildi.
- `/api/agent/chat` artık public server'da Hermes çalıştırmaz; işi kuyruğa yazar ve worker sonucu gelirse yalnızca ilgili kullanıcıya döner.
- Worker çevrimdışıysa server sahte cevap üretmez; iş kuyrukta kalır ve client `202 Accepted` ile job status alır.
- Worker console projesi eklendi: `Vortex.HermesWorker`.
- Worker gerçek Hermes executable yapılandırılmamışsa başarı üretmez; readiness/hata döndürür.
- Completion sırasında `AgentUsageCounters`, `AgentExecutionLogs` ve `AuditLogs` güncellenir.
- Owner-only job status endpoint'i eklendi: `/api/agent/jobs/{id}`.
- Bu dilime ait zorunlu belgeler eklendi/güncellendi.
- `CODEMAP.md` oluşturuldu.
- `graphify update .` çalıştırıldı.

## Henüz yapılmayanlar

- Gerçek Tailscale ACL/grants kurulumu yapılmadı.
- Gerçek Hermes binary/credential olmadığı için uçtan uca gerçek model yürütmesi manuel doğrulanmadı.
- Google OAuth ve GitHub OAuth eklenmedi.
- Sesli sohbet, TTS, maskot, ağız animasyonu veya gelişmiş UI eklenmedi.
- Worker-side automation scheduler uygulanmadı; mevcut scheduled task endpoint'i metadata kaydı tutar, gerçek worker zamanlayıcı çalıştırması tamam değildir.
- Büyük input/result için `InputReference`/`ResultReference` dış depolaması tamamlanmadı.
- Tam storage quota pre/post write enforcement ve reconciliation görevi tamamlanmadı.
- Symlink/junction kaçışlarına karşı kapsamlı filesystem testi eklenmedi.
- Production forwarded headers, global rate limit, refresh token rotasyonu ve backup encryption henüz tamam değil.

## Çalışan servisler / bileşenler

- `Vortex.Server`: build ve test ortamında çalışıyor.
- `Vortex.HermesWorker`: derleniyor; gerçek Hermes executable bekliyor.
- Server-worker API sözleşmesi: integration test ile simüle worker üzerinden doğrulandı.
- Job queue: claim, heartbeat, complete ve owner-only status test edildi.

## Sahte veya doğrulanmamış kalan parçalar

- Gerçek Hermes runtime çağrısı kodda process adapter olarak mevcut, ancak `HERMES_EXECUTABLE_PATH` sağlanmadığı için gerçek çalıştırma doğrulanmadı.
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
```

## Migration durumu

`VortexDb.InitializeAsync` içinde lightweight SQLite migration eklendi:

- `UserAgentProfiles` için `WorkerId`, `WorkspaceId`, `PlanId`, `StorageQuotaBytes`, `StorageUsedBytes`, `ProfileStatus`, `DisabledAt`, `LastUsedAt` kolonları garanti edilir.
- Free plan kotası eski düşük değerlerden 5 GB'a yükseltilir.
- `WorkerRegistrations`, `AgentJobs` ve AgentJobs indexleri oluşturulur.

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
Test: başarılı, 9/9 test geçti
Graphify: AST update başarılı, 1226 nodes / 2141 edges / 91 communities
```

## Bilinen güvenlik riskleri

- Worker HMAC nonce bellekte tutulur; server restart sonrası replay penceresi kalıcı değildir.
- `AgentJobs.Result` şu dilimde SQLite içinde plaintext saklanır.
- Tailscale ACL politikası kodla uygulanmadı; operasyonel ortamda ayrıca kurulmalıdır.
- Tam prompt ve hassas dosya içerikleri loglanmamalıdır; mevcut audit detayları id/uzunluk seviyesinde tutulur, ancak future logging değişiklikleri aynı kurala uymalıdır.
- Worker workspace path doğrulaması temel traversal/root containment içerir; symlink/junction hardening eklenmelidir.

## Sonraki kesin adım

Gerçek laptop ortamında Tailscale private bağlantı, `Worker:AllowedWorkerId`/`Worker:ServiceToken`, `VORTEX_WORKER_TOKEN` ve `HERMES_EXECUTABLE_PATH` değerleriyle canlı `Vortex.HermesWorker` çalıştırılıp tek bir gerçek Hermes job'unun uçtan uca doğrulanması.
