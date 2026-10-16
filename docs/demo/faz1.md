# Faz 1 demo senaryosu — Platform çekirdeği

Bu senaryo Faz 1'de (PLAN 0.x ve P1–P13) yapılan Platform özelliklerini baştan sona gösterir. Her adımın sonunda **Beklenen** satırı, demoda neyin görülmesi gerektiğini yazar. Senaryo yaklaşık 45 dakika sürer; bölümler birbirinden bağımsız da gösterilebilir.

İK modülleri (personel, izin, işe alım) Faz 2 ve sonrasındadır; bu senaryoda yoktur.

## 1. Hazırlık

1. Altyapıyı başlatın (Docker gerekir): `docker compose up -d`. Postgres, Valkey, RabbitMQ, Keycloak, Mailpit, Gotenberg ve Aspire panosu açılır.
2. Veritabanlarını oluşturun: `DOTNET_ENVIRONMENT=Development dotnet run --project src/Host/Merp.Migrator`.
3. API'yi başlatın: `dotnet run --project src/Host/Merp.Api` (dokümantasyon: `https://localhost:5001/scalar`).
4. Web'i başlatın: `cd web && npm install && npm run dev`, sonra `http://localhost:5173`.
5. E-postalar için Mailpit'i açın: `http://localhost:8025`.
6. Geliştirme kullanıcılarıyla **birer kez** girip çıkın: `admin / admin`, `ik / ik`, `yonetici / yonetici`, `calisan / calisan`. Kullanıcı kaydı ilk girişte oluşur; onay akışında ve rol atamada seçilebilmeleri için bu gereklidir.

| Kullanıcı | Rolü (ilk girişte, D26) | Menüde gördükleri |
|---|---|---|
| `admin` | Sistem Yöneticisi (tüm şirketler) | Tüm Platform ekranları |
| `ik` | İK Yöneticisi | Ana sayfa, Onaylar; yapay zeka açıksa İşe alım → CV analizi |
| `yonetici` | Yönetici | Ana sayfa, Onaylar |
| `calisan` | Çalışan | Ana sayfa, Onaylar |

## 2. Kabuk ve ana sayfa

1. `admin` ile girin.
2. Sol menüdeki grupları gösterin: Onaylar, Organizasyon, Erişim yönetimi, **Tanımlar** (ayarlar, numaralandırma, özel alanlar, diller, takvimler, onay akışları, şablonlar) ve **Sistem** (sistem bilgisi, arka plan işleri, denetim, yapay zeka kullanımı). Yalnızca açık sayfanın grubu açık başlar.
3. **Ctrl+K** ile ekran arayın: "şube" yazıp Şubeler'e gidin. Türkçe büyük/küçük harf (İ/ı) doğru eşleşir.
4. Üst bardan temayı Açık / Koyu / Sistem arasında değiştirin.
5. Ana sayfaya dönün.

**Beklenen:** Ana sayfada bekleyen onay ve okunmamış bildirim sayıları, son 5 bildirim ve kullanıcının yetkisine göre hızlı bağlantılar (en sonda Profilim) görünür.

## 3. Tanımlar: özel alan ve numaralandırma

1. Tanımlar → **Özel alanlar** → "Yeni alan": Şirket için "Sektör" adında, türü **Tek seçim** olan bir alan; seçenekler Gıda, Tekstil, Lojistik.
2. Tanımlar → **Numaralandırma**: hazır numara serilerini ve biçimlerini gösterin.

**Beklenen:** Yeni özel alan bir sonraki adımda şirket formunda görünür.

## 4. Organizasyon: şirket ve şube

1. Organizasyon → **Şirketler** → "Yeni şirket". Önce vergi numarasına `1234567891` yazıp kaydedin.
   **Beklenen:** Vergi numarası alanının altında Türkçe hata mesajı çıkar (kontrol hanesi tutmaz).
2. Geçerli bir VKN girin (örn. `1234567808`), il ve ilçeyi seçin, "Sektör" alanını doldurup kaydedin.
3. Şirketi açıp **logo** yükleyin ve **Ekler** bölümüne bir PDF ekleyin; eki indirin.
4. Şirket listesinde **Excel'e aktar** ile listeyi indirin.
   **Beklenen:** Excel'de görünen sütunlar ve "Sektör" sütunu var; sayılar ve tarihler tipli.
5. Organizasyon → **Şubeler** → "Yeni şube": yeni şirkete bir fabrika ekleyin; "Çalışma takvimi" alanını boş bırakın.
6. **Excel'den içe aktar** → "Şablonu indir". Şablona bir doğru, bir hatalı (bilinmeyen şirket kodu) satır girip yükleyin.
   **Beklenen:** Hiçbir şey yazılmadan hata satırı ve sütunuyla listelenir; hata raporu indirilebilir. Hatalı satırı düzeltip yeniden yükleyince "İçe aktar" ile şube oluşur.

## 5. Erişim: kullanıcı, rol ve veri kapsamı

1. Erişim yönetimi → **Kullanıcılar** → "Yeni kullanıcı" (gerçek olmayan bir e-posta ile).
   **Beklenen:** Mailpit'e Keycloak davet e-postası düşer.
2. Kullanıcıyı açıp **Rol ata**: İK Yöneticisi, kapsam "Şirket" → 4. adımda oluşturulan şirket.
3. Erişim yönetimi → **Roller ve yetkiler**: bir sistem rolünü açın, yetki matrisini gösterin; yeni bir rol (ör. "Denetçi", yalnızca görüntüleme yetkileri) oluşturun.
4. Çıkış yapıp `calisan` ile girin; adres çubuğuna `/system/settings` yazın.
   **Beklenen:** Menüde yalnızca Ana sayfa ve Onaylar var; adres "Erişim yetkiniz yok" sayfasını gösterir.

## 6. Denetim (KVKK)

1. `admin` ile şirketin telefonunu değiştirin.
2. Şirketler listesinde şirketin satırındaki **Geçmiş** simgesine basın.
3. Sistem → **Denetim kayıtları**: varlık türüne (Şirket) göre süzün.

**Beklenen:** Kim, ne zaman, hangi alanı eski ve yeni değeriyle değiştirdi görünür; dışa aktarmalar da "Dışa aktarma" işlemi olarak kayıtlıdır.

## 7. Diller ve çeviriler

1. Tanımlar → **Diller ve çeviriler**: bir ekran metnini (ör. "Ana sayfa") değiştirip kaydedin.

**Beklenen:** Kaydedince menüdeki metin hemen değişir; sayfayı yenilemek gerekmez. Çevirmen için **Excel'e aktar** ile metinler dışarı alınabilir.

## 8. Takvimler

1. Tanımlar → **Resmi tatiller**: 2026'yı gösterin; 28 Ekim yarım gün (13:00'ten itibaren), 29 Ekim tam gündür. 2028'e geçin.
   **Beklenen:** Dini bayramlar girilmemişse uyarı çıkar; "2027 sabit tatillerini kopyala" sabit tatilleri getirir.
2. Tanımlar → **Çalışma takvimleri**: yeni bir takvim (Cumartesi yarım gün) oluşturun.
3. 4. adımdaki şubeyi açıp bu takvimi seçin.

**Beklenen:** Haftalık saat toplamı anında hesaplanır; şube kendi takvimini kullanır, seçilmezse şirketin varsayılanı geçerlidir.

## 9. Bildirimler

1. Sağ üstten **Profilim** → **Bildirimler** kartı → **Test bildirimi gönder**.
   **Beklenen:** Birkaç saniye içinde zilde sayı artar; Mailpit'e e-posta düşer.
2. Tanımlar → **Bildirim şablonları** → "Yeni şablon": tür "Test bildirimi", konu `Demo: {{ title }}`; içerikte yerleşik düzeni değiştirip kaydedin.
3. Test bildirimini yeniden gönderin.

**Beklenen:** Yeni e-posta, değiştirilmiş şablonla gelir.

## 10. Onay motoru

1. Tanımlar → **Onay akışları** → belge türü **Deneme talebi** → "Yeni akış". Tek adım: onaylayıcı türü "Belirli kullanıcı", kullanıcı `yonetici`. Kaydedin.
2. Aynı ekranda **Deneme talebi gönder**: özet "Laptop alımı", tutar 45000.
   **Beklenen:** "Deneme talebi gönderildi" mesajı.
3. Çıkış yapıp `yonetici` ile girin.
   **Beklenen:** Ana sayfada "Bekleyen onaylar" 1; zilde onay bildirimi.
4. Onaylar → **Onay Kutum** → talebi açıp onaylayın.
   **Beklenen:** Talep onaylanır; zaman çizelgesinde adım, karar veren ve zaman görünür.
5. (İsteğe bağlı) Onaylar → **Vekaletlerim**: `yonetici`, bir hafta için `ik` kullanıcısına vekalet verir; yeni deneme talebi `ik`'nin kutusuna da düşer.
6. `admin` ile Onaylar → **Onay talepleri**: talep "Onaylandı" durumunda listelenir. Akışı olmayan ya da onaylayıcısı bulunamayan talepler burada "İlgi bekliyor" durumunda görünür; yönetici onaylayıcı atayabilir, görevi başka kullanıcıya aktarabilir veya adımı onaylayabilir.

## 11. Ayarlar

1. Tanımlar → **Ayarlar** → Bildirimler: "Bildirimleri e-postayla da gönder" ayarını yeni şirket için kapatın.
2. Üst bardaki şirket seçiciden bu şirketi seçip test bildirimi gönderin.

**Beklenen:** Bildirim zile düşer, e-posta gitmez. Şirket görünümünde kiracı genelinden gelen değer de gösterilir.

## 12. Belge şablonları ve PDF

1. Tanımlar → **Belge şablonları** → "Yeni şablon": tür "Antetli genel yazı", şirket olarak 4. adımdaki şirket, dil Türkçe → Editörü aç.
   **Beklenen:** Sağdaki önizlemede şirketin logosu, unvanı, adresi ve vergi bilgileriyle antetli örnek yazı görünür.
2. Gövdede `<p>{{ letter.subject }}</p>` satırını `<h2>{{ letter.subject }}</h2>` yapın.
   **Beklenen:** Önizleme yazmayı bıraktıktan kısa süre sonra güncellenir.
3. Bilerek `{% if letter %}` yazıp kapatmayın.
   **Beklenen:** Satır editörde kırmızı işaretlenir, altında satır numaralı hata mesajı çıkar. Hatayı düzeltin.
4. **PDF önizle**.
   **Beklenen:** PDF yeni sekmede açılır; Türkçe harfler (ğ, ü, ş, ı, ö, ç, İ) ve logo doğru basılır.
5. Kaydedin; şablon listesinde revizyon 1 olarak görünür. İçeriği değiştirip yeniden kaydedince revizyon 2 olur.

## 13. Sistem

1. Sistem → **Sistem bilgisi**: sürüm, ortam, kiracı ve kullanıcı.
2. Aynı sayfadan **Arka plan işleri panelini aç**.
   **Beklenen:** Hangfire paneli yeni sekmede açılır; yinelenen işler listelenir: `notifications.email` (e-posta yeniden deneme), `approvals.escalation` (onay süre aşımı), `calendars.holiday-reminder` (eksik tatil hatırlatma).
3. Sistem → **Yapay zeka kullanımı**: aylık kullanım ve bütçe. Anthropic API anahtarı tanımlı değilse "yapılandırılmamış" uyarısı görünür; bu beklenen durumdur (README → Yapay zeka).

## Bilinen sınırlar

- Yapay zeka özellikleri (CV analizi) Anthropic API anahtarı olmadan kapalıdır.
- "Yönetici" rolünün "yönettiği ekip" kapsamı İK organizasyon yapısıyla (H1) gelir; bu fazda yönetici rolüne kapsamlı veri yetkisi verilmez.
- Kayıt düzenlemeleri, ekranı açık başka bir kullanıcının değişikliğiyle çakışırsa 409 hatasıyla reddedilir; sayfa yenilenip yeniden denenir.
