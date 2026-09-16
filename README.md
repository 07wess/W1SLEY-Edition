# W1SLEY Edition

W1SLEY Edition, Windows 11 için açık kaynaklı bir performans ve oyun merkezi uygulamasıdır. Fişe takılı kullanımda işlemci boost davranışını yönetir, oyun profilleri oluşturur, sistem sağlığını izler ve desteklenen değişiklikleri geri alma günlüğünde saklar. Pil güç değerlerine dokunmaz.

> Bu proje donanım hız aşırtması yapmaz. Windows güç planındaki işlemci boost davranışını değiştirir. Sonuçlar işlemci, BIOS/EC, soğutma ve üretici yazılımına göre değişebilir.

## Güç modları

| Mod | Windows boost değeri | Kullanım amacı |
| --- | --- | --- |
| **Agresif Performans** | Aggressive | En hızlı boost tepkisi. Güç tüketimi, sıcaklık ve fan sesi artabilir; bazı sistemlerde kararsızlık oluşturabilir. Açık onay ister ve sonraki oturum için Kararlı Performans kurtarması kurar. |
| **Tam Performans** | Enabled | Boost açık, frekans yönetimi Windows'ta. Oyun ve ağır işler için dengeli yüksek performans. |
| **Kararlı Performans** | Efficient Enabled | Verimlilik, sıcaklık ve performans arasında önerilen varsayılan. |

İlk açılış sihirbazı Windows sürümü, yönetici yetkisi, CPU, RAM, GPU, güç altyapısı, gerçek zamanlı koruma ve temel aygıt/disk olaylarını **salt okunur** tarar. Kullanıcı daha sonra başlangıç modunu seçer.

## Başlıca özellikler

- Canlı CPU, RAM, GPU, frekans ve desteklenen sıcaklık/sensör değerleri
- Üç doğrulanan işlemci boost modu
- Oyun keşfi, oyun profilleri ve oyun başlatıcı
- Oyun kapanınca güç modu, DNS ve kapatılan uygulamaları geri yükleme
- Güvenli veya açık uyarılı zorla RAM bakımı
- Başlangıç yöneticisi ve kontrollü Windows uygulama temizleyicisi
- Hızlı/tam güvenlik taraması başlatma ve sistem sağlığı kontrolü
- Kernel-Power, BugCheck, WHEA ve ekran sürücüsü olayları için çökme dedektörü
- İşlem günlüğü ve desteklenen ayarlar için geri alma merkezi
- Tepsi menüsü ve özelleştirilebilir klavye kısayolları

## Gereksinimler

- Windows 11 x64, derleme 22000 veya üzeri
- Yönetici yetkisi
- Kaynaktan derleme için .NET 10 SDK
- Kurucu üretmek için isteğe bağlı Inno Setup 6

Kurucu ve yayınlanan taşınabilir paket .NET çalışma zamanını içerir.

## Kurulum

GitHub Releases sayfasındaki `W1SLEY-Edition-Setup-x64.exe` dosyasını indirin. Windows SmartScreen, imzasız topluluk yapılarında uyarı gösterebilir. Dosyanın yayınlanan SHA-256 değeriyle eşleştiğini doğrulayın.

İlk açılışta sistem analizi çalışır. Önerilen başlangıç seçeneği **Kararlı Performans**'tır.

## Kaynaktan derleme

```powershell
dotnet build .\W1sleyEdition.csproj -c Release
```

Self-contained yayın ve ZIP üretmek için:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Version 0.1.2
```

Inno Setup kurucusunu da üretmek için:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Version 0.1.2 -BuildInstaller
```

## Güvenlik ve gizlilik

- İlk kurulum analizi ve sistem sağlığı kontrolleri salt okunurdur.
- Güç, DNS, başlangıç ve uygulama kaldırma işlemleri yalnızca kullanıcı eylemiyle çalışır.
- Desteklenen değişiklikler günlükte tutulur ve mümkün olduğunda geri alınabilir.
- Uygulama telemetri göndermez.
- Oyun içine kod enjekte etmez; anti-cheat süreçlerine dokunmaz.
- Üçüncü taraf tünelleme veya harici sürücü aracı barındırmaz; tamamen saf Windows performans ve sistem yönetimine odaklanır.

Güvenlik açığı bildirmek için [SECURITY.md](SECURITY.md) dosyasını okuyun.

## Lisans

MIT — ayrıntılar için [LICENSE](LICENSE).
