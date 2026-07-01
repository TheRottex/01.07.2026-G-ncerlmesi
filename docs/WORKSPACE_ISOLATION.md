# Workspace Isolation

## Amaç
Bir kullanıcının mesaj, hafıza, otomasyon, dosya, çıktı, log ve credential referanslarının başka kullanıcı tarafından okunmamasını hedefleyen workspace kurallarını açıklar.

## Dosya hedef cümlesi
`Vortex.HermesWorker/Program.cs` workspace resolver şu hedef için oluşturuldu; opaque workspace id girdisini alır; `PathSafety.IsSafeRelativePath` ve root containment doğrulamaları yapar; Hermes process working directory'sine aktarır; kullanıcı workspace path çıktısı üretir; hata halinde işi başarısız döndürür.

## Klasör yapısı
`data/users/<opaque-workspace-id>/workspace`, `memory`, `automations`, `artifacts`, `temp`, `metadata`.

## Güven kontrolleri
Path traversal, rooted path ve boş path reddedilir. Resolve edilen workspace kökünün kullanıcı root'u içinde kalması doğrulanır. Symlink/junction kaçışları için daha sert fiziksel path doğrulaması sonraki dilimde eklenmelidir.

## Hata durumları
WorkspaceId geçersizse worker işi çalıştırmaz. Storage root yoksa readiness storage unhealthy bildirir.

## Test
Kod seviyesinde workspace id'nin e-posta içermediği integration testle doğrulanır. Symlink/junction testleri henüz yoktur.

## Bilinen eksikler
OS-level sandbox, per-job process isolation ve izinli workspace dışı erişim policy motoru bu dilimde yoktur.