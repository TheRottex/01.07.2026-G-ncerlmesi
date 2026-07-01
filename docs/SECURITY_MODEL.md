# Security Model

## Amaç
İlk dilimde uygulanan güvenlik kontrollerini ve sınırları açıklar.

## Kimlik
Client kaynak erişimleri JWT'den türetilir; istemci UserId'sine güvenilmez. Worker erişimi `X-Vortex-Worker-Id`, timestamp, nonce ve HMAC signature ile doğrulanır.

## Dosya hedef cümlesi
`WorkerAuthenticationService` şu hedef için oluşturuldu; worker header girdilerini alır; allowed worker id, timestamp ve nonce replay doğrulaması yapar; worker endpointlerine aktarır; worker id çıktısı üretir; hata halinde unauthorized döner.

## Secret yönetimi
`Worker:ServiceToken`, `Worker:AllowedWorkerId`, model API keyleri ve worker adresleri client'a gönderilmez. Token/prompt/secret loglanmaması hedeflenir; job details yalnızca uzunluk ve id içerir.

## Replay koruması
Nonce bellekte tutulur ve timestamp penceresi uygulanır. Process restart sonrası nonce belleği sıfırlanır; bu bilinen risktir.

## Rate/limit
Plan daily run ve concurrent run kontrolleri server tarafında uygulanır. Geniş HTTP rate limit middleware'i bu dilimde yoktur.

## Bilinen riskler
Tam veri sızıntısı imkânsız iddiası yoktur. Result alanı SQLite içinde plaintext saklanır; hassas veri şifreleme ve backup encryption sonraki dilimde gerekir.