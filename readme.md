# LanStreamControl-WinForms-60FPS

**Yerel ağda (LAN) ekran paylaşımı ve uzaktan kontrol uygulaması**  
C# .NET 4.x WinForms tabanlı, yüksek performanslı istemci-sunucu mimarisiyle düşük gecikmeli görüntü aktarımı sağlar.

---

## 🚀 Özellikler
- **60 FPS JPEG Delta yayını:** Server ekranını 60 FPS hızında sıkıştırılmış karelerle gönderir.
- **Düşük gecikme (Low-Latency):** `TcpClient.NoDelay` ve optimize edilmiş buffer yönetimi.
- **Responsive Client UI:** Dinamik panel boyutları, DPI uyumlu ve çift tamponlu (DoubleBuffer) arayüz.
- **Canlı modern arayüz:** Renkli kontrol paneli, canlı durum metrikleri (FPS / KB/s), TopMost ve görüntü modu geçişi.
- **Delta Frame sistemi:** Sunucu sadece değişen ekran bloklarını yollar.
- **Snapshot özelliği:** Client üzerinden anlık görüntüyü masaüstüne PNG olarak kaydetme.
- **Tam kontrol:** Mouse ve klavye olaylarını istemciden sunucuya gerçek zamanlı gönderir.
- **Sunucu ekran imleci ayarı:** İmleç çizimini server tarafında aç/kapat.
- **LAN taraması (/24):** Ağınızdaki aktif server cihazlarını otomatik olarak listeler.
- **OOM (Out Of Memory) koruması:** Bellek taşmalarına karşı güvenli görüntü oluşturma.
- **Hata günlüğü:** Tüm hatalar `%TEMP%/Client_err.txt` dosyasında tutulur.
- **Basit dağıtım:** .NET 4.0+ dışında ek bağımlılık yok.

---

## 🧩 Teknik Mimari

### Server
- `Server.cs` sunucu ekranını yakalar (`GDI BitBlt` ile) ve JPEG olarak sıkıştırır.
- Delta sistemiyle yalnızca değişen alanlar gönderilir.
- 5000 portunda yayın yapar, +1 portunda kontrol olaylarını dinler.

### Client
- `Client.cs` görüntü akışını alır, `PictureBox` üzerinde sürekli yeniler.
- Kullanıcı girdi olaylarını (fare, klavye) sunucuya yollar.
- Responsive UI ve LAN tarayıcı içerir.

---

## ⚙️ Derleme

```bat
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /t:winexe /out:Server.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Server.cs
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /t:winexe /out:Client.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Client.cs
```

### Gereksinimler
- .NET Framework **4.0 veya üzeri**
- Windows 7 / 8 / 10 / 11 (x64 önerilir)

---

## 📡 Kullanım
1. `Server.exe` uygulamasını paylaşılacak bilgisayarda başlat.
2. `Client.exe`’yi aynı ağda çalışan diğer cihazda aç.
3. “Scan /24” tuşu ile ağdaki aktif sunucuları bul.
4. IP ve port (varsayılan **5000**) girerek **Connect** yap.
5. Görüntü geldiğinde **Control: ON** butonuna basarak tam kontrol sağla.

---

## 💾 Günlük (Log) Dosyası
Tüm hatalar aşağıdaki dosyada tutulur:
```
C:\Users\<kullanıcı>\AppData\Local\Temp\Client_err.txt
```
Sorun yaşarsanız bu dosyayı kontrol edin.

---

## 🧠 Geliştirici Notları
- SplitContainer başlangıç hataları tamamen çözüldü (dinamik ölçüm).
- OOM ve GDI bellek sızıntılarına karşı koruma eklendi.
- Görüntü yenileme 60 FPS'ye optimize edildi.
- `ThreadException` ve `UnhandledException` log mekanizması entegre.

---

## 📜 Lisans
Bu proje MIT lisansı altındadır.  
Serbestçe değiştirilebilir, dağıtılabilir ve ticari olarak kullanılabilir.

---

**Yazar:** ali  
**Proje:** LanStreamControl-WinForms-60FPS  
**Sürüm:** v1.0.0
