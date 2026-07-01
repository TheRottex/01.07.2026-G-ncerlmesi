# Job Queue

## Amaç
Public Server üzerinde kalıcı `AgentJobs` kuyruğunu, güvenli claim/lease ve completion davranışını belgelemek.

## Dosya hedef cümlesi
`Vortex.Server/Services/AgentJobServices.cs` şu hedef için oluşturuldu; doğrulanmış kullanıcı, profil ve chat isteği girdisini alır; plan limiti, idempotency, kalıcı nonce replay koruması ve worker readiness doğrulamaları yapar; SQLite `AgentJobs` kuyruğuna aktarır; atomik job lease/result/status çıktısı üretir; hata halinde queued/retrying/failed/timed-out durumlarına geçer.

## Alanlar
`JobId`, `UserId`, `AgentProfileId`, `ConversationId`, `RequestId`, `IdempotencyKey`, `Status`, `Priority`, `Input`, `CreatedAt`, `ClaimedAt`, `StartedAt`, `CompletedAt`, `LeaseExpiresAt`, `AttemptCount`, `MaxAttempts`, `ErrorCode`, `WorkerId`, `Result`, `CancellationRequested`.

## Atomik claim
Claim tek SQLite `UPDATE ... RETURNING Id` işlemiyle yapılır. `UPDATE` içinde eski status, lease, cancellation ve attempt koşulları tekrar kontrol edilir. Yalnızca satır gerçekten update edilirse lease başarılı kabul edilir. Worker readiness son heartbeat üzerinden kontrol edilir; not-ready worker'a iş verilmez.

## Completion guard ve idempotency
Completion yalnızca işi claim eden worker, aktif `Claimed`/`Running` status ve geçerli lease ile kabul edilir. Terminal durumdaki job tekrar complete edilirse mevcut terminal sonuç döner; usage counter ve audit ikinci kez yazılmaz. Başka worker veya süresi geçmiş lease completion gönderirse reddedilir.

## Transaction sınırı
Completion status update, `AgentExecutionLogs`, `AgentUsageCounters` ve audit yazımı aynı SQLite transaction içinde yürütülür. Böylece başarılı completion usage artırmadan veya audit yazmadan yarım kalmamalıdır.

## Retry ve timeout
Retryable worker failure `Retrying` olabilir. `TimedOut` error code'u `TimedOut` job status ve execution status üretir. Yan etkili işler için tool-level idempotency hâlâ ayrı gereksinimdir.

## Test
Integration testler body hash HMAC, nonce replay, concurrent claim, duplicate completion, wrong worker, expired lease ve not-ready worker davranışını doğrular.

## Bilinen eksikler
Büyük input/result için `InputReference`/`ResultReference` dış depolama hâlâ uygulanmadı.