# Job Queue

## Amaç
Public Server üzerinde kalıcı `AgentJobs` kuyruğunu belgelemek.

## Dosya hedef cümlesi
`Vortex.Server/Services/AgentJobServices.cs` şu hedef için oluşturuldu; doğrulanmış kullanıcı, profil ve chat isteği girdisini alır; plan limiti, idempotency ve worker HMAC sınırlarını uygular; SQLite `AgentJobs` kuyruğuna aktarır; job lease/result/status çıktısı üretir; hata halinde queued/retrying/failed durumlarına geçer.

## Alanlar
`JobId`, `UserId`, `AgentProfileId`, `ConversationId`, `RequestId`, `IdempotencyKey`, `Status`, `Priority`, `Input`, `CreatedAt`, `ClaimedAt`, `StartedAt`, `CompletedAt`, `LeaseExpiresAt`, `AttemptCount`, `MaxAttempts`, `ErrorCode`, `WorkerId`, `Result`, `CancellationRequested`.

## Durumlar
`Pending`, `Queued`, `Claimed`, `Running`, `WaitingForApproval`, `Completed`, `Failed`, `Cancelled`, `TimedOut`, `WorkerUnavailable`, `Retrying` sözleşmede tanımlıdır. İlk dilimde aktif geçişler: `Queued -> Claimed -> Running -> Completed/Failed/Retrying`.

## Lease ve idempotency
Worker claim sırasında `BEGIN IMMEDIATE` transaction ile tek iş seçilir ve lease süresi atanır. `UNIQUE(UserId, IdempotencyKey)` aynı kullanıcının aynı idempotency anahtarını tekrar kuyruğa sokmasını engeller.

## Hata ve tekrar
Retryable worker failure, attempt sayısı `MaxAttempts` altındaysa `Retrying` olur. Yan etkili işlerde kör tekrar yapılmaması için ileride tool-level idempotency zorunlu olmalıdır.

## Test
Integration test worker claim/complete ve offline queued davranışını doğrular.

## Bilinen eksikler
Büyük input/result için `InputReference`/`ResultReference` dış depolama bu dilimde uygulanmadı.