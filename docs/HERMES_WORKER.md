# Hermes Worker

## Amaç
Laptop üzerindeki `Vortex.HermesWorker` public server'a dışarı doğru bağlanan pull tabanlı worker'dır. Laptop üzerinde public inbound port açmaz.

## Dosya hedef cümlesi
`Vortex.HermesWorker/Program.cs` şu hedef için oluşturuldu; server URL/worker token/Hermes executable girdilerini alır; HMAC ve workspace path doğrulamaları yapar; işi lokal Hermes executable'a aktarır; stdout sonucunu server'a üretir; hata halinde retryable failure veya readiness not-configured bildirir.

## Katman
Console worker uygulaması, `Vortex.Shared` sözleşmelerini kullanır.

## Haberleşme
- `POST /api/worker/heartbeat`
- `POST /api/worker/jobs/claim`
- `POST /api/worker/jobs/{id}/heartbeat`
- `POST /api/worker/jobs/{id}/complete`

## Kimlik ve izin
`VORTEX_WORKER_ID` ve `VORTEX_WORKER_TOKEN` ile HMAC imza üretir. Token client uygulamalara gömülmez.

## Workspace davranışı
`data/users/<opaque-workspace-id>/workspace` ve yardımcı klasörler oluşturulur. WorkspaceId güvenli relative path değilse iş reddedilir.

## Hata durumları
Hermes executable eksikse başarı dönmez. Process exit code 0 değilse stderr log içeriği değil yalnızca uzunluğu yazılır.

## Test yöntemi
Integration testler worker'ın HTTP davranışını simüle eder. Gerçek Hermes binary ile manuel test `VORTEX_SERVER_URL`, `VORTEX_WORKER_ID`, `VORTEX_WORKER_TOKEN`, `HERMES_EXECUTABLE_PATH` set edilerek yapılmalıdır.

## Bilinen eksikler
Uzun süren işler için periyodik job heartbeat thread'i ve streaming output bu dilimde yoktur.