# User Hermes Mapping

## Amaç
Vortex kullanıcılarının Hermes logical profile ve opaque workspace kimlikleriyle eşleşmesini tanımlar.

## Dosya hedef cümlesi
`HermesProfileService` şu hedef için güncellendi; JWT'den türetilmiş `UserId` girdisini alır; kullanıcı planı ve opaque workspace doğrulaması yapar; `UserAgentProfiles` tablosuna aktarır; `AgentProfileDto` çıktısı üretir; hata halinde `ProvisioningFailed` durumuna düşer.

## Alanlar
`Vortex UserId`, `HermesProfileId`, `WorkerId`, `WorkspaceId`, `PlanId`, `StorageQuotaBytes`, `StorageUsedBytes`, `ProfileStatus`, `CreatedAt`, `LastUsedAt`, `DisabledAt` schema/migration ile takip edilir.

## Güven sınırı
İstemciden gelen `RequestedProfileId` güvenilmez; server her zaman JWT kullanıcısının profilini kullanır. Workspace kimliği e-posta veya tahmin edilebilir isim içermez.

## Veri saklama
Mapping server SQLite içinde saklanır. Worker tarafında fiziksel klasör `data/users/<workspace-id>/...` olarak oluşturulur.

## Test
Integration test iki kullanıcının farklı profile/workspace kimliği aldığını ve başka kullanıcı profili istense bile kendi profiliyle çalıştığını doğrular.

## Bilinen eksikler
Kullanıcı silme sonrası worker-side cleanup flow'u sonraki dilimde uygulanmalıdır.