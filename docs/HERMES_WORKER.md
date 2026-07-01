# Hermes Worker

## Amaç
Laptop üzerindeki `Vortex.HermesWorker` public server'a dışarı doğru bağlanan pull tabanlı worker'dır. Laptop üzerinde public inbound port açmaz.

## Dosya hedef cümlesi
`Vortex.HermesWorker/Program.cs` şu hedef için oluşturuldu; server URL/worker token/Hermes executable ve output limit girdilerini alır; HMAC body-hash imzası, readiness ve workspace path doğrulamaları yapar; işi lokal Hermes executable'a güvenli argument list ile aktarır; bounded stdout sonucunu server'a üretir; hata halinde retryable failure, timeout veya limit exceeded döndürür.

## Katman
Console worker uygulaması, `Vortex.Shared` sözleşmelerini ve ortak `SigningCanonical` helper'ını kullanır.

## Haberleşme
- `POST /api/worker/heartbeat`
- `POST /api/worker/jobs/claim`
- `POST /api/worker/jobs/{id}/heartbeat`
- `POST /api/worker/jobs/{id}/complete`

## Kimlik ve izin
`VORTEX_WORKER_ID` ve `VORTEX_WORKER_TOKEN` ile method, path, timestamp, nonce ve request body SHA-256 hash değerlerini imzalayan HMAC üretilir. Token/signature/ham body loglanmaz.

## Readiness
Worker executable, model readiness ve storage healthy değilse heartbeat gönderir ama claim isteği göndermez. Server da not-ready worker'a claim vermez. Hermes executable eksikse `NotConfigured` server state'i üretilebilir.

## Process yaşam döngüsü
Hermes process `UseShellExecute=false` ve `ArgumentList` ile çalışır. `WorkerJobLeaseDto.MaxRunSeconds` timeout olarak uygulanır. Timeout veya cancellation olursa process tree sonlandırılır. İş çalışırken periyodik job heartbeat gönderilir.

## Output sınırları
`Worker:MaxStdoutBytes` / `VORTEX_WORKER_MAX_STDOUT_BYTES` ve `Worker:MaxStderrBytes` / `VORTEX_WORKER_MAX_STDERR_BYTES` ile bounded read yapılır. Limit aşılırsa process sonlandırılır ve `StdoutLimitExceeded` veya `StderrLimitExceeded` döner.

## Workspace davranışı
`data/users/<opaque-workspace-id>/workspace` ve yardımcı klasörler oluşturulur. WorkspaceId güvenli relative path değilse iş reddedilir.

## Test yöntemi
Integration testler worker HMAC, process timeout ve stdout limitini doğrular. Gerçek Hermes binary ile manuel test hâlâ `HERMES_EXECUTABLE_PATH` set edilerek yapılmalıdır.

## Bilinen eksikler
Streaming output, OS-level sandbox ve symlink/junction kaçışlarına karşı gelişmiş fiziksel path doğrulaması yoktur.