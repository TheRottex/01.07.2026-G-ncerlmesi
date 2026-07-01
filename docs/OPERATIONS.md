# Operations

## Amaç
İlk dilim için çalıştırma ve doğrulama komutlarını toplar.

## Server gerekli ayarları
- `Jwt:SigningKey`: production güçlü secret.
- `Worker:AllowedWorkerId`: izin verilen laptop worker id.
- `Worker:ServiceToken`: HMAC servis tokenı.
- `Worker:ChatWaitSeconds`: client isteğinde sonucu bekleme süresi.

## Worker gerekli ayarları
- `VORTEX_SERVER_URL`: public server HTTPS URL.
- `VORTEX_WORKER_ID`: server ayarıyla aynı worker id.
- `VORTEX_WORKER_TOKEN`: server tokenıyla aynı secret.
- `HERMES_EXECUTABLE_PATH`: gerçek Hermes executable yolu.
- `VORTEX_WORKER_DATA`: laptop data root.

## Çalıştırma
Server: `dotnet run --project Vortex.Server/Vortex.Server.csproj`.
Worker: `dotnet run --project Vortex.HermesWorker/Vortex.HermesWorker.csproj` gerekli env değerleriyle.

## Health
`/health` server readiness, `/health/worker` worker readiness içindir. Server healthy olmak Hermes/model hazır anlamına gelmez.

## Hata durumları
Worker token uyuşmazsa 401. Hermes executable eksikse worker not configured bildirir. Worker offline ise client queued status alır.

## Bilinen eksikler
Production reverse proxy forwarded headers ve TLS termination hardening sonraki dilimdedir.