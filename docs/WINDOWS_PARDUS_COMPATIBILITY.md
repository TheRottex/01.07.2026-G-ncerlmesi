# Windows and Pardus Compatibility

## Amaç
Vortex.Desktop'ın Windows ve Pardus/Linux üzerinde yalnızca public server'a HTTPS ile bağlanması ilkesini korur.

## Client sınırı
Bu dilimde Desktop client worker'a doğrudan bağlanmaz. Worker secret'ı veya Tailscale adresi Desktop/Web'e gönderilmez.

## Worker path davranışı
`Vortex.HermesWorker` data root için environment/arg alır; varsayılan Windows LocalApplicationData kullanır. Pardus/Linux için servis dosyasında `VORTEX_WORKER_DATA` açıkça verilmelidir.

## Dosya hedef cümlesi
`Vortex.HermesWorker/Program.cs` şu hedef için oluşturuldu; OS bağımsız env/arg girdisini alır; `Path` API ve `OperatingSystem.IsWindows()` case-sensitivity doğrulamaları yapar; Hermes process'e aktarır; stdout sonucunu üretir; hata halinde platforma özel API kullanmadan failure döner.

## Bilinen eksikler
Pardus token saklama servisi, systemd unit hardening ve Windows Service kurulumu henüz belgelenen operasyonel taslak düzeyindedir.