# Publishing v0.1.0

1. GitHub'da boş ve **Public** bir depo oluşturun. README, lisans veya `.gitignore` ekletmeyin; bu dosyalar projede hazırdır.
2. Bu klasörde aşağıdaki komutları çalıştırın:

```powershell
git init
git branch -M main
git add .
git commit -m "Release W1SLEY Edition v0.1.0"
git remote add origin https://github.com/OWNER/REPOSITORY.git
git push -u origin main
```

3. İlk GitHub Release'i otomatik üretmek için etiketi gönderin:

```powershell
git tag -a v0.1.0 -m "W1SLEY Edition v0.1.0"
git push origin v0.1.0
```

`.github/workflows/release.yml`, Windows üzerinde self-contained ZIP ve Inno Setup kurucusu üretip GitHub Release'e ekler. `SHA256SUMS.txt` aynı Release'e yüklenir.

## Yayından önce

- `README.md`, `SECURITY.md` ve `LICENSE` metinlerini son kez okuyun.
- Release dosyalarının SHA-256 değerlerinin `SHA256SUMS.txt` ile eşleştiğini doğrulayın.
- Kurucunun henüz kod imzası olmadığını Release notunda açıkça belirtin. İmzasız sürümler SmartScreen uyarısı gösterebilir.
- Agresif Performans'ı farklı donanımlarda otomatik test etmeyin; açık kullanıcı onayını koruyun.
