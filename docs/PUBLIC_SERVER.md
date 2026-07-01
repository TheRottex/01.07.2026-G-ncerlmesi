# Public Server

## Amaç
Bu dosya public server sorumluluklarını belgeler: auth, plan/kota, AgentJob kuyruğu, worker auth, audit ve sonuç sahipliği.

## Dosya hedef cümlesi
`Vortex.Server/Program.cs` şu hedef için güncellendi; JWT doğrulanmış kullanıcı girdisini alır; kullanıcı/plan kontrolü yapar; `IAgentJobService` bileşenine aktarır; `AgentChatResponse` veya `AgentJobStatusDto` üretir; hata halinde 401/429/202/502 döner.

## Girişler
- Client: `Authorization: Bearer <jwt>`, `AgentChatRequest`.
- Worker: `X-Vortex-Worker-Id`, timestamp, nonce, HMAC signature, heartbeat/claim/complete payloadları.

## Çıkışlar
- Client için iş sonucu veya queued status.
- Worker için lease payloadı ve completion kabul yanıtı.

## Güven ve izin
Kullanıcı kimliği request içindeki UserId'den değil JWT `sub` claim'inden türetilir. Worker endpointleri JWT kullanmaz; HMAC servis kimliği kullanır.

## Hata durumları
- Worker secret eksikse worker endpointleri unauthorized döner.
- Kullanım limiti doluysa 429.
- Worker yetişmezse 202 ve job status URL.

## Veri saklama
`Users`, `UserAgentProfiles`, `AgentJobs`, `AgentUsageCounters`, `AgentExecutionLogs`, `AuditLogs`, `WorkerRegistrations` SQLite içinde saklanır.

## Test
Integration testler queue, worker complete, limit ve owner-only read kontrollerini çalıştırır.

## Bilinen eksikler
Forwarded headers, production rate limit ve refresh token rotasyonu bu dilimde tamamlanmadı.