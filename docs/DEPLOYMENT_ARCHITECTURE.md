# Deployment Architecture

## Amaç ve hedef
Bu belge public Vortex.Server ile laptop üzerindeki Vortex.HermesWorker ayrımını tanımlar. Hedef, Web/Desktop istemcilerinin yalnızca HTTPS üzerinden public server'a bağlanması ve Hermes runtime'ın kullanıcının laptopunda private Tailscale ağı arkasında kalmasıdır.

## Katman ve bileşenler
- `Vortex.Server/Program.cs`: public HTTPS API, auth, iş kuyruğu ve worker endpointleri.
- `Vortex.Server/Services/AgentJobServices.cs`: kalıcı AgentJob kuyruğu, worker claim/heartbeat/complete akışı.
- `Vortex.HermesWorker/Program.cs`: laptop üzerinde çalışan pull tabanlı worker.
- `Vortex.Shared/Contracts.cs`: server-worker ve client sözleşmeleri.

## Veri akışı
Desktop/Web -> public Vortex.Server -> JWT kullanıcı doğrulaması -> `AgentJobs` kaydı -> Worker pull/claim -> lokal Hermes executable -> worker complete -> server sonucu -> yalnızca iş sahibi kullanıcı.

## Güven sınırı
İstemci ile server arası public HTTPS sınırıdır. Server ile worker arası Tailscale/HTTPS + HMAC servis kimliği sınırıdır. Worker secret, Tailscale adresi ve model anahtarları istemciye gönderilmez.

## Hata durumları
Worker yoksa iş `Queued` kalır ve client `202 Accepted` alır. Hermes executable yoksa worker readiness `NotConfigured`/hazır değil olarak raporlanır ve iş retryable failure dönebilir; sistem sahte başarı üretmez.

## Yaşam döngüsü ve saklama
İşler SQLite `AgentJobs` tablosunda kalıcıdır. Sonuçlar şu dilimde `Result` alanında saklanır; büyük/artifact sonuçları için ileride `ResultReference` dış depolamaya taşınmalıdır.

## Test yöntemi
`Vortex.Tests/HermesIsolationIntegrationTests.cs` worker endpointlerini HMAC ile simüle eder, claim/complete ve owner-only status akışını doğrular.

## Bilinen eksikler
Gerçek Tailscale ACL kurulumu ve gerçek Hermes binary ile uçtan uca manuel doğrulama ortam secret'ları olmadan yapılmadı.