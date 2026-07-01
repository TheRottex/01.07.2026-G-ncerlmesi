# CODEMAP

## İlk dağıtım dilimi dosyaları

- `Vortex.Shared/Contracts.cs` — server, client ve worker arasındaki ortak DTO/enum sözleşmeleri. `AgentJobStatus`, `WorkerReadinessDto`, `WorkerJobLeaseDto`, heartbeat ve completion payloadlarını içerir.
- `Vortex.Server/Data/VortexDb.cs` — SQLite schema ve lightweight migration merkezi. `AgentJobs`, `WorkerRegistrations`, genişletilmiş `UserAgentProfiles` ve Free plan 5 GB quota kayıtlarını oluşturur.
- `Vortex.Server/Services/AgentServices.cs` — kullanıcıya özel Hermes profile/workspace mappingi, plan policy ve kullanım sayaçlarının mevcut servisleri. Public server artık chat için Hermes'i doğrudan çalıştırmaz; profile mapping üretir.
- `Vortex.Server/Services/AgentJobServices.cs` — kalıcı AgentJob kuyruğu, worker HMAC auth, lease/claim, heartbeat, complete, usage counter ve audit update akışı.
- `Vortex.Server/Program.cs` — public API endpointleri. `/api/agent/chat`, `/api/agent/jobs/{id}`, `/api/worker/*`, `/health/worker` burada bağlanır.
- `Vortex.HermesWorker/Vortex.HermesWorker.csproj` — laptop worker console projesi.
- `Vortex.HermesWorker/Program.cs` — server'a outbound bağlanan, HMAC imzalayan, job claim eden ve gerçek Hermes executable'a aktaran worker loop.
- `Vortex.Tests/HermesIsolationIntegrationTests.cs` — iki kullanıcı izolasyonu, worker claim/complete, owner-only job status, offline queued ve limit davranışı testleri.
- `docs/*.md` — deployment, public server, worker, Tailscale, queue, mapping, isolation, quota, security/threat, operations ve recovery belgeleri.

## Bağlantılar
Desktop/Web istemcileri yalnızca `Vortex.Server/Program.cs` client endpointlerine bağlanır. Worker yalnızca `Vortex.Server/Program.cs` içindeki `/api/worker/*` endpointlerine HMAC ile bağlanır. Queue ve audit logic `AgentJobServices.cs` üzerinden `VortexDb.cs` tablosuna yazar.
