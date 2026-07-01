# CODEMAP

## Server–Worker ilk dilim dosyaları

- `Vortex.Shared/Contracts.cs` — server, client ve worker arasındaki ortak DTO/enum sözleşmeleri. `AgentJobStatus`, `WorkerReadinessDto`, `WorkerJobLeaseDto`, heartbeat/completion payloadları ve ortak `SigningCanonical` HMAC helper'ını içerir.
- `Vortex.Server/Data/VortexDb.cs` — SQLite schema ve lightweight migration merkezi. `AgentJobs`, `WorkerRegistrations`, kalıcı `WorkerReplayNonces`, genişletilmiş `UserAgentProfiles` ve Free plan 5 GB quota kayıtlarını oluşturur.
- `Vortex.Server/Services/AgentServices.cs` — kullanıcıya özel Hermes profile/workspace mappingi, plan policy ve kullanım sayaçlarının mevcut servisleri. Public server chat için Hermes'i doğrudan çalıştırmaz; profile mapping üretir.
- `Vortex.Server/Services/AgentJobServices.cs` — kalıcı AgentJob kuyruğu, body-hash HMAC worker auth, kalıcı nonce replay store, worker readiness gate, atomik claim, lease/heartbeat, idempotent completion, usage counter ve audit update akışı.
- `Vortex.Server/Program.cs` — public API endpointleri. `/api/agent/chat`, `/api/agent/jobs/{id}`, `/api/worker/*`, `/health/worker` burada bağlanır. Worker endpointleri için request body buffering burada açılır.
- `Vortex.HermesWorker/Vortex.HermesWorker.csproj` — laptop worker console projesi.
- `Vortex.HermesWorker/Program.cs` — server'a outbound bağlanan, body-hash HMAC imzalayan, yalnızca ready durumda job claim eden ve gerçek Hermes executable'a timeout/output limitleriyle aktaran worker loop.
- `Vortex.Tests/HermesIsolationIntegrationTests.cs` — iki kullanıcı izolasyonu, worker claim/complete, owner-only job status, offline queued, HMAC body tamper, nonce replay, concurrent claim, duplicate completion, wrong worker, expired lease, not-ready worker, process timeout ve output limit testleri.
- `docs/JOB_QUEUE.md` — AgentJob transaction, claim, lease ve completion davranışı.
- `docs/HERMES_WORKER.md` — worker readiness, process timeout/cancellation ve bounded stdout/stderr davranışı.
- `docs/SECURITY_MODEL.md` — HMAC canonical formatı, sabit zamanlı imza karşılaştırması ve kalıcı replay nonce modeli.
- `docs/THREAT_MODEL.md` — düzeltilen race/replay/tamper riskleri ve açık kalan riskler.

## Bağlantılar
Desktop/Web istemcileri yalnızca `Vortex.Server/Program.cs` client endpointlerine bağlanır. Worker yalnızca `Vortex.Server/Program.cs` içindeki `/api/worker/*` endpointlerine HMAC ile bağlanır. Queue ve audit logic `AgentJobServices.cs` üzerinden `VortexDb.cs` tablolarına yazar. Worker process execution `Vortex.HermesWorker/Program.cs` içinde kalır.
