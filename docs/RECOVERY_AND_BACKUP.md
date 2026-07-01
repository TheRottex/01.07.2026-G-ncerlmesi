# Recovery and Backup

## Amaç
İlk dilim verilerinin kaybı ve geri yükleme sınırlarını tanımlar.

## Saklanan veriler
Server SQLite içinde kullanıcılar, profil mappingleri, AgentJobs, execution logs, usage counters ve audit logs saklanır. Worker laptopta workspace data root altında kullanıcı workspace dosyalarını saklar.

## Recovery davranışı
Worker koparsa lease süresi dolan işler yeniden claim edilebilir. Retryable failure `Retrying` durumuna geçer. Server restart sonrası queued işler kaybolmaz.

## Backup gereksinimi
Server DB yedeği hassas veri içerir; encrypted backup gerekir. Worker data root backup'ı kullanıcı dosyaları ve memory/artifact içerebilir; cihaz şifrelemesi ve seçili backup policy gerekir.

## Hata durumları
DB bozulursa işler ve audit kayıtları kaybolabilir. Bu dilimde otomatik DB backup ve restore tooling yoktur.

## Test
Integration test sadece kuyruk kalıcılığı davranışını API seviyesinde doğrular; gerçek disk restore testi yapılmadı.

## Bilinen eksikler
Hesap silme sonrası server+worker cleanup ve backup retention policy sonraki dilimdedir.