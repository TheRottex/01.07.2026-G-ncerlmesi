# Automations

## Amaç
Kalıcı otomasyonların sonraki aşamalarda worker workspace'inde, metadata'nın server'da takip edilmesi için sınırı tanımlar.

## Mevcut durum
`AgentScheduledTasks` tablosu mevcut otomasyon metadata'sını saklar. İlk dilimde kapsam dışı gelişmiş otomasyon türleri eklenmedi.

## Dosya hedef cümlesi
`Vortex.Server/Program.cs` mevcut task endpointleri şu hedef için korunur; doğrulanmış JWT kullanıcısı ve task isteği girdisini alır; plan aktif görev limitini doğrular; `AgentScheduledTasks` tablosuna aktarır; `AgentTaskDto` çıktısı üretir; hata halinde 401/429/400 döner.

## Güven sınırı
Her otomasyon çalıştırmasında UserId/HermesProfileId sahipliği yeniden doğrulanmalıdır. Dış etki oluşturan otomasyonlarda kullanıcı onayı gerekir.

## Worker offline davranışı
Bu dilimde yalnızca genel iş kuyruğu offline kalıcılığı uygulandı. Otomasyon missed-run davranışı henüz seçilmedi.

## Bilinen eksikler
`AutomationId`, `RequiredPermissions`, `NextRunAt`, telafi politikası ve worker-side scheduler sonraki dilimde uygulanmalıdır.