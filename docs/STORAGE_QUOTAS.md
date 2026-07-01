# Storage Quotas

## Amaç
Free kullanıcılar için 5 GB mantıksal depolama kotasının server mappinginde takip edilmesi.

## Dosya hedef cümlesi
`Vortex.Server/Data/VortexDb.cs` şu hedef için güncellendi; plan seed/migration girdisini alır; free plan kotasını 5 GB'a yükseltir; `UserAgentProfiles.StorageQuotaBytes` alanına aktarır; profil DTO'sunda quota çıktısı üretir; hata halinde migration başlangıçta fail olur.

## Kapsama dahil veriler
Kullanıcı dosyaları, agent çıktıları, kalıcı hafıza, kalıcı otomasyonlar, kullanıcıya özel artifact/cache kotaya dahil edilmelidir.

## Kapsam dışı
Model ağırlıkları, runtime dosyaları ve sistem bileşenleri kullanıcı kotasına yazılmamalıdır.

## Hata durumları
Fiziksel disk veya logical quota enforcement henüz tam yazma katmanında yoktur. Bu dilimde quota alanları ve mapping hazırlandı, tam pre/post write accounting tamamlanmadı.

## Test
Integration test profil kotasının 5 GB olduğunu doğrular.

## Bilinen eksikler
Silme sonrası reconciliation, minimum free disk policy ve per-write quota guard sonraki dilimde uygulanmalıdır.