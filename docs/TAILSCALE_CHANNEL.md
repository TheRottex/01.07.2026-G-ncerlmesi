# Tailscale Channel

## Amaç
Server-worker kanalının private Tailscale ağında tutulması hedeflenir. Worker Funnel ile public hale getirilmemelidir.

## Model
Tercih edilen model worker'ın public server'a dışarı doğru HTTPS bağlantısı kurmasıdır. Bu nedenle laptop üzerinde inbound port gerekmez. Tailscale, server'ın laptop'a erişmesi gereken ileriki akışlarda cihaz/port/protokol ACL ile sınırlandırılmalıdır.

## Güven sınırı
Tailscale taşıma şifrelemesi yeterli kabul edilmez; worker endpointleri ayrıca HMAC servis kimliği, timestamp ve nonce ile replay koruması uygular.

## Girdiler/çıktılar
Worker heartbeat, claim ve complete requestleri server'a gider; job input ve result dönüleri bu kanaldan akar.

## Hata durumları
Tailscale veya internet yoksa worker heartbeat kesilir; server health `WorkerConnected=false` gösterir ve işler kuyrukta kalır.

## Test
Kod seviyesinde HMAC doğrulaması integration testle doğrulanır. Gerçek Tailscale grants/ACL testi ortam bağımlı olduğu için bu dilimde doğrulanmadı.

## Bilinen eksikler
ACL örnek policy dosyası ve operasyonel rotasyon runbook'u sonraki dilimde ayrıntılandırılmalıdır.