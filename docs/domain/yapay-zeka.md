# Yapay zeka özellikleri

Mimari kararlar: `docs/DECISIONS.md` D24 (öneri, onay bekliyor). Görevler: `docs/PLAN.md` "Yapay zeka" bölümü.

## 1. İlkeler

1. **Öneri, karar değil.** Yapay zeka taslak hazırlar, özetler, eşleştirir. Kararı her zaman bir kullanıcı verir. Hiçbir iş akışı yapay zeka sonucuyla otomatik ilerlemez: aday elenmez, talep onaylanmaz, kayıt silinmez.
2. **Önce maskele, sonra gönder.** Sağlayıcıya sadece işin gerektirdiği metin gider. Kimlik ve iletişim bilgileri ile özel nitelikli veriler gönderilmeden önce çıkarılır (§4).
3. **Tipli çıktı.** Her özelliğin sonucu bir C# tipidir. Sağlayıcı yanıtı bu tipten üretilen JSON şemasına uymak zorundadır. Serbest metin ayrıştırılmaz.
4. **Varsayılan kapalı, izlenebilir.** Özellikler kiracı ve özellik bazında açılır. Her çağrı kullanım kaydına yazılır (kim, hangi özellik, hangi talimat sürümü, kaç token, tahmini maliyet). İçerik saklanmaz.
5. **Adil değerlendirme.** Talimatlar sadece işle ilgili niteliklerin değerlendirilmesini ister. Yaş, cinsiyet, medeni durum, uyruk, din, sağlık, engellilik, askerlik durumu gibi özellikler dikkate alınmaz ve çıkarım yapılmaz.

## 2. Akış

```
Modül handler'ı
  → IAiService.IsEnabled(özellik)        kiracı açık mı, sağlayıcı yapılandırılmış mı
  → metni hazırla ve maskele             IDocumentTextExtractor, PersonalDataRedactor
  → IAiService.GenerateAsync<TSonuç>()
       bütçe kontrolü                    platform.ai_usage, bu ayın toplamı
       sağlayıcı çağrısı                 talimatlar (önbellekli) + girdi + TSonuç şeması
       reddetme / kesilme / bozuk çıktı  hata kodu, kullanım yine kaydedilir
       kullanım kaydı                    platform.ai_usage + Merp.AI metrikleri
  → sonucu normalize et (aralıklar), kullanıcıya "öneri" olarak göster
```

## 3. Özellik kataloğu

| # | Özellik | Modül | Fayda | Durum |
|---|---|---|---|---|
| 3.1 | **CV analizi:** özet, deneyim, eğitim, yetkinlik, dil, sertifika; gereksinim bazında eşleşme ve kanıt; mülakat soruları | İşe alım | Uzmanın ön elemede harcadığı süreyi azaltır, değerlendirmeyi gereksinimlere bağlar | ✅ Durumsuz sürüm hazır (A2) |
| 3.2 | **Aday kartında CV analizi:** CV yüklenince analiz başvuruya kaydedilir; aday formu CV'den doldurulur | İşe alım | Elle veri girişini kaldırır | ✅ Hazır (A3) |
| 3.3 | **Toplu ön tarama:** bir ilanın tüm başvurularını gece toplu analiz | İşe alım | Yüksek hacimli ilanlarda hız; Batch API ile yarı maliyet | R4 sonrası (A6) |
| 3.4 | **İlan metni taslağı ve kapsayıcı dil kontrolü:** personel talebinden ilan taslağı; ayrımcı veya dışlayıcı ifadeler için uyarı | İşe alım | Daha hızlı ve tutarlı ilan, ayrımcılık riskini azaltır | ✅ Hazır (A4) |
| 3.5 | **Mülakat soruları ve geri bildirim özeti:** pozisyona göre soru seti; mülakatçı notlarından ortak özet (kör geri bildirim kuralına uyarak) | İşe alım | Yapılandırılmış mülakat, hızlı karar toplantısı | ✅ Hazır (A5) |
| 3.6 | **Aday e-posta taslakları:** şablonu adaya göre kişiselleştirir (ret, davet) | İşe alım | Aday deneyimi | ✅ Hazır (A5) |
| 3.7 | **Çeviri taslakları:** yeni dil eklenince eksik metinlerin taslağı | Platform | Çoklu dil maliyetini düşürür | P4 ile (A7) |
| 3.8 | **Onay özeti:** onaycıya talebin bir paragraflık özeti ve dikkat noktaları | Platform | Onay süresini kısaltır | P10 ile (A8) |
| 3.9 | **İçe aktarma kolon eşleştirme:** Excel başlıklarını alanlara eşleştirme önerisi | Platform | İlk kurulumu hızlandırır | P11 ile (A9) |
| 3.10 | **Belge şablonu taslağı:** açıklamadan Fluid şablonu taslağı | Platform | Şablon hazırlama süresi | P12 ile (A10) |
| 3.11 | **Doğal dille izin talebi:** "Gelecek cuma ve pazartesi yıllık izin" → form taslağı | İzin | Self serviste kolaylık | L4 ile (A11) |
| 3.12 | **Asistan (Ctrl+K):** doğal dille arama ve gezinme; yalnızca kullanıcının yetki ve veri kapsamındaki salt okunur sorgular | Platform | Tek giriş noktası | X fazında (A12) |

**Yapay zekaya bırakılmayacaklar:** otomatik ret veya sıralama, ücret belirleme, performans puanı, izin onayı, işten çıkarma kararı, sağlık raporu veya engellilik belgesi gibi özel nitelikli belgelerin işlenmesi.

### 3.1 CV analizi (yapıldı)

- **Endpoint:** `POST /api/recruitment/cv-analyses` (multipart). Yetki `recruitment.cv.analyze`, özellik `recruitment.cv_analysis`, hız sınırı `ai`.
- **Girdi:** dosya (PDF veya DOCX, en fazla 5 MB), isteğe bağlı pozisyon adı, gereksinimler (en fazla 8000 karakter), aday adı (maskeleme için).
- **İşleyiş:**
  1. Kiracıda özellik kapalıysa dosya okunmadan 403 `common.errors.ai_disabled` döner.
  2. Biçim dosya imzasından anlaşılır; metin PdfPig veya Open XML SDK ile çıkarılır (en fazla 30 sayfa, 100.000 karakter). DOCX üst ve alt bilgisi okunmaz.
  3. Aday adı verilmemişse ilk üç satırdaki ad bulunmaya çalışılır; başlık ve unvan kelimeleri ad sayılmaz.
  4. Metin maskelenir (§4). Kaç değerin çıkarıldığı kullanıcıya gösterilir.
  5. Talimatlar sabittir ve önbelleğe alınır (`CvAnalysisPrompt.Version`). Girdi: çıktı dili, `<job>` ve `<cv>` etiketleri.
  6. Sonuç normalize edilir: puan 0–100, deneyim 0–60 yıl; gereksinim verilmemişse eşleşme boş bırakılır.
- **Çıktı:** `CvAnalysisResponse` (analiz, maskeleme sayıları, belgenin kesilip kesilmediği, model, talimat sürümü). Dosya ve analiz saklanmaz. Başvuruya kaydedilen analiz için bkz. §3.2.
- **Ekran:** İşe alım → CV analizi. Sonuç "Yapay zeka önerisidir" uyarısıyla gösterilir. Ekran görüntüleri: `docs/screenshots/ai/`.

### 3.2 Aday kartında CV analizi (yapıldı)

- **Arka plan:** Kiracıda özellik açıksa, süreçteki ve son 30 günde gelen CV'li başvurular 2 dakikada bir, kiracı başına en fazla 5 tane analiz edilir. Eşleşme ilanın başlığı ve aranan nitelikleriyle yapılır. Sonuç başvuruya kaydedilir (model, talimat sürümü, tarih); başarısızlık hata koduyla kaydedilir.
- **Yeniden analiz:** `POST /api/recruitment/applications/{id}/cv-analysis`. Yetki `recruitment.cv.analyze` ve başvuruyu okuma kapsamı, hız sınırı `ai`. Başarısız yeniden analiz öncekini silmez.
- **Aday formu:** `POST /api/recruitment/candidates/cv-prefill`. Ad ve iletişim bilgileri yerelde düzenli ifadelerle okunur, sağlayıcıya gitmez. Unvan, şirket, deneyim ve eğitim analizden gelir.
- **Görünürlük:** Başvuruyu gören herkes (işe alım ekibi dahil) analizi "yapay zeka önerisidir" uyarısıyla görür.
- Kararlar: `docs/DECISIONS.md` D51.

### 3.4 İlan metni taslağı ve kapsayıcı dil kontrolü (yapıldı)

- **Taslak:** `POST /api/recruitment/openings/text-draft`. Yetki `recruitment.openings.manage`, özellik `recruitment.opening_text`, hız sınırı `ai`. Girdi: başlık, çalışma modeli, istihdam türü, personel talebinin pozisyonu, seviyesi, birimi, kadro sayısı ve nedeni, maskelenmiş kısa açıklama. Talebin gerekçesi ve ücret aralığı gönderilmez. Sonuç saklanmaz, ilan formunun editörlerini doldurur.
- **Gözden geçirme:** Taslaktan gelen ilan, kullanıcı "gözden geçirdim" onayıyla kaydedilene kadar yayınlanamaz (409 `recruitment.openings.errors.ai_draft_not_reviewed`).
- **Dil kontrolü:** Yerel kural listesi (yaş, cinsiyet, medeni durum, askerlik, uyruk, din, sağlık, dış görünüş; Türkçe ve İngilizce) ilan her okunduğunda çalışır. `POST /api/recruitment/openings/language-check` kural listesini, `withAi` ile de maskelenmiş düz metin üzerinde yapay zeka incelemesini çalıştırır. Uyarılar yayını engellemez.
- Kararlar: `docs/DECISIONS.md` D57.

### 3.5 Mülakat soruları ve geri bildirim özeti (yapıldı)

- **Soru önerileri:** `POST /api/recruitment/interviews/{id}/question-suggestions`. Yetki `recruitment.interviews.read`, özellik `recruitment.interview_questions`, hız sınırı `ai`. Girdi: ilanın başlığı, açıklaması ve nitelikleri (maskelenmiş düz metin), mülakat türü, formun kriterleri ve puan açıklamaları. Aday bilgisi gönderilmez. Kriter başına soru, neye dikkat edileceği ve ek sorular döner; saklanmaz.
- **Geri bildirim özeti:** `POST /api/recruitment/applications/{id}/feedback-summary` (İK), `/me/hiring/applications/{id}/feedback-summary` (işe alım ekibi) ve `/interviews/{id}/feedback-summary` (mülakatçılar). Özellik `recruitment.feedback_summary`.
  - Kör kural: başvurunun iptal edilmemiş, formlu mülakatlarındaki herkes formunu gönderene kadar 409 `recruitment.interviews.errors.feedback_pending` döner.
  - Mülakatçılar `I1`, `I2`… olarak gider. Aday ve mülakatçı adları maskelenir.
  - Özet karar önermez; güçlü yönler, endişeler, görüş ayrılıkları ve açık sorular döner.
- Kararlar: `docs/DECISIONS.md` D58.

### 3.6 Aday e-posta taslakları (yapıldı)

- **Taslak:** `POST /api/recruitment/applications/{id}/rejection-email-draft` ve `.../invitation-email-draft`. Özellik `recruitment.candidate_email`.
  - Bildirim türünün metni adayın dilinde ve yer tutucularıyla (`{candidate}`, `{position}`…) gider; pozisyon, aşama ve kullanıcının maskelenmiş talimatı da eklenir.
  - Adayın adı ve iletişim bilgileri gönderilmez.
- **Gönderim her zaman kullanıcı onayıyla:** taslak bir şey göndermez. Düzenlenen metin ret ya da mülakat planlama komutuyla gider, yer tutucuları Platform doldurur. Ertelenmiş ret e-postasının metni gidene kadar başvuruda saklanır.
- Kararlar: `docs/DECISIONS.md` D58.

## 4. Kişisel veri maskeleme

`Merp.SharedKernel.Privacy.PersonalDataRedactor` her özellikte sağlayıcıya gitmeden önce çalışır.

| Tür | Örnek | Yerine |
|---|---|---|
| E-posta | `ayse@example.com` | `[EMAIL]` |
| Telefon | `0532 123 45 67`, `+90 (212) 123 45 67`, `415-555-0132` | `[PHONE]` |
| T.C. kimlik no (sağlama algoritmasıyla) | `10000000146` | `[NATIONAL_ID]` |
| IBAN (mod-97 ile) | `TR33 0006 1005 1978 6457 8413 26` | `[IBAN]` |
| Profil ve web bağlantıları | `linkedin.com/in/…`, `https://…` | `[URL]` |
| Ad (verilen veya bulunan) | `Ayşe Yılmaz` | `[NAME]` |
| Etiketli özel alanlar | `Doğum Tarihi: …`, `Medeni Durum: …`, `Askerlik Durumu: …`, `Sağlık: …`, `Kan Grubu: …`, `Dini: …`, `Adres: …`, `Date of birth: …`, `Gender: …` | `Etiket: [REDACTED]` |

- Türkçe ve İngilizce etiketler büyük/küçük harf ve Türkçe karakter farkı gözetmeden tanınır (İ/ı/I/i aynı sayılır).
- Tarih aralıkları, tutarlar ve yıllar telefon sanılmaz.
- **Sınır:** Etiketsiz serbest metindeki bilgiyi (ör. "3 çocuk annesiyim") tanımaz. Bu yüzden talimatlar ayrıca korunan özelliklerin dikkate alınmamasını ister. Kesin anonimlik gerekiyorsa özellik kapatılmalı veya kurum içi model kullanılmalıdır.

## 5. Yapılandırma

```jsonc
"Ai": {
  "Provider": "Anthropic",            // "None": tüm özellikler kapalı
  "Model": "claude-sonnet-5-5",       // varsayılan; zor işler için "claude-opus-5-5"
  "MaxOutputTokens": 16000,
  "RequestTimeout": "00:05:00",
  "MaxRetries": 2,
  "Anthropic": {
    "ApiKey": "",                     // user-secrets veya Ai__Anthropic__ApiKey; kaynak koda yazılmaz
    "BaseUrl": "https://api.anthropic.com",
    "InferenceGeo": "",               // "", "global" veya "us"
    "ServerSideFallback": true
  },
  // İsteğe bağlı: model bazlı liste fiyatı (USD / milyon token). Yazılmazsa yerleşik fiyatlar kullanılır.
  "Pricing": { "claude-sonnet-5-5": { "InputPerMillionTokens": 2, "OutputPerMillionTokens": 10, "CacheReadPerMillionTokens": 0.2, "CacheWritePerMillionTokens": 2.5 } },
  "Defaults": { "Enabled": false, "Features": [ "*" ], "MonthlyBudgetUsd": 0 },   // 0: sınırsız
  "Tenants": { "demo": { "Enabled": true, "MonthlyBudgetUsd": 20 } }
}
```

`Features` desenleri: `*` (hepsi), `recruitment.*` (modülün hepsi), `recruitment.cv_analysis` (tek özellik).

## 6. Maliyet tahmini

Varsayılan model Claude Sonnet 5.5'in liste fiyatı: girdi 2 USD, çıktı 10 USD / milyon token; önbellekten okunan girdi 0,20 USD / milyon token. Claude Opus 5.5 bunun iki katıdır (4 / 20 USD).

| İş | Girdi | Çıktı (düşünme dahil) | Sonnet 5.5 (varsayılan) | Opus 5.5 |
|---|---|---|---|---|
| Bir CV analizi | ~4.000–6.000 token | ~2.000–4.000 token | ~0,03–0,05 USD | ~0,05–0,10 USD |
| Ayda 1.000 CV | | | ~30–50 USD | ~50–100 USD |
| Toplu tarama (Batch API) | | | yaklaşık yarısı | yaklaşık yarısı |

Gerçek değerler kullanım ekranında (Sistem → Yapay zeka kullanımı) izlenir. Bütçe kiracı bazında sınırlanır.

## 7. Hata kodları

| Kod | HTTP | Ne zaman |
|---|---|---|
| `common.errors.ai_disabled` | 403 | Sağlayıcı yapılandırılmamış veya özellik kiracıda kapalı |
| `common.errors.ai_budget_exceeded` | 403 | Aylık bütçe doldu |
| `common.errors.ai_refused` | 400 | Model içeriği işlemeyi reddetti (yedek model de reddetti) |
| `common.errors.ai_unavailable` | 503 | Sağlayıcıya ulaşılamadı, zaman aşımı veya sağlayıcı hatası |
| `common.errors.ai_invalid_output` | 503 | Yanıt kesildi veya okunamadı |
| `common.errors.document_unsupported_format` | 400 | PDF veya DOCX değil |
| `common.errors.document_unreadable` | 400 | Bozuk veya parola korumalı dosya |
| `common.errors.document_no_text` | 400 | Metin katmanı olmayan (taranmış) PDF |

## 8. Test ve değerlendirme

- Birim testler: maskeleyici, şema üretimi (structured outputs kısıtları), sağlayıcı istek biçimi (sahte HTTP ile), bütçe ve hata akışları, talimat girdisi.
- Entegrasyon testleri: sahte sağlayıcıyla (`FakeAiModelClient`) uçtan uca; sağlayıcıya giden metinde kişisel verinin bulunmadığı doğrulanır.
- CI gerçek API'yi çağırmaz.
- **Açık iş (A13):** Talimat kalitesi için değerlendirme seti. Anonimleştirilmiş örnek CV'ler, beklenen alanlar, eşleşme puanı aralığı ve adalet kontrolleri (ör. aynı CV'de yalnızca ad veya cinsiyet değişince puan değişmemeli). Talimat veya model değişikliği bu setle ölçülür.

## 9. Kurum içi (on-prem) model seçeneği

Bazı müşteriler verinin şirket dışına hiç çıkmamasını isteyebilir. İki ayrı konu vardır:

- **mERP'in müşteri sunucusunda çalışması:** Zaten planda (Docker, PostgreSQL ve diğer bileşenler; PLAN X5). Ek bir iş gerektirmez.
- **Yapay zeka modelinin müşteri sunucusunda çalışması:** Aşağıdaki gereksinimler bunun içindir.

| Konu | Gereken |
|---|---|
| Donanım | GPU'lu sunucu. Küçük modeller (7–14 milyar parametre) için 24–48 GB belleğe sahip tek GPU yeterli olabilir, ancak Türkçe değerlendirme kalitesi belirgin şekilde düşer. Büyük modeller (~70 milyar parametre) için yaklaşık 2 adet 80 GB'lık veri merkezi GPU'su gerekir. Maliyet donanıma göre on binlerce dolara çıkabilir. |
| Model | Lisansı D19'a uyan açık ağırlıklı bir model (Apache-2.0 veya MIT). Bazı popüler modellerin özel lisansları vardır ve listede değildir. |
| Sunucu yazılımı | Modeli OpenAI uyumlu bir HTTP API ile sunan açık kaynak bir sunucu. Şemaya uygun çıktı için JSON şema desteği olmalıdır. |
| Bizim tarafta geliştirme | Yeni bir `IAiModelClient` uygulaması (A15) ve testleri, değerlendirme setiyle (A13) kalite ölçümü, kurulum rehberi: yaklaşık 1–2 hafta. Modüller değişmez. |
| Müşteri tarafında işletim | GPU kapasite planlaması (eş zamanlı kullanıcı), model güncellemeleri, izleme ve güvenlik yamaları müşterinin bilgi işlem ekibine düşer. |
| Kalite | Açık modeller Türkçe metinde ve gerekçeli değerlendirmede genellikle Claude'un gerisindedir. Fark değerlendirme setiyle ölçülmeden söz verilmemelidir. |

**Ara seçenekler:**
1. Yapay zekayı o müşteride kapalı tutmak. Ürünün geri kalanı yapay zekasız eksiksiz çalışır.
2. Claude'u müşterinin kendi bulut hesabı üzerinden kullanmak (Amazon Bedrock, Google Cloud veya Microsoft Foundry; A16). Veri müşterinin bulut sözleşmesi altında işlenir. Bölge seçilebilir, ancak Türkiye dışı olduğu için KVKK md. 9 yine geçerlidir. Bizim için küçük bir iş; sunucu tarafı yedek model ve Batch API bu platformlarda yoktur.

**Öneri:** Kurum içi modeli talep eden ilk müşteriyle birlikte yapmak. Altyapı buna hazır; o güne kadar 1. veya 2. seçenek kullanılır.

## 10. KVKK uyumu (yurt dışına aktarım)

Sağlayıcı (Anthropic) Türkiye dışındadır. Maskelenmiş özgeçmiş metni de kişisel veridir: kişi dolaylı olarak belirlenebilir. Bu yüzden her çağrı KVKK md. 9 kapsamında yurt dışına aktarımdır.

### 10.1 Hukuki çerçeve

7499 sayılı Kanunla değişen md. 9 ve 10.07.2024 tarihli "Kişisel Verilerin Yurt Dışına Aktarılmasına İlişkin Usul ve Esaslar Hakkında Yönetmelik" uyarınca:

- Yeterlilik kararı varsa aktarım yapılabilir. Bu belge yazılırken sağlayıcının ülkesi için Kurul kararı bilinmemektedir.
- Yeterlilik kararı yoksa uygun güvence gerekir. En pratik yol, Kurul'un ilan ettiği standart sözleşmedir. Sözleşme imzalandıktan sonra 5 iş günü içinde Kuruma bildirilir (fiziki, KEP veya Kurul'un bildirim modülü). Bağlayıcı şirket kuralları ve taahhütname de birer güvencedir.
- Açık rıza 1 Eylül 2024'ten sonra yalnızca arızi (ara sıra, tekrarlanmayan) aktarımlarda kullanılabilir. Yapay zeka özellikleri veriyi düzenli aktardığından açık rızaya dayanılmaz.

### 10.2 Üründe yapılanlar

- Kiracıda aktarım dayanağı kayıtlı değilse (`TransferBasis`) yapay zeka özellikleri çalışmaz, menüde görünmez ve API 403 `common.errors.ai_disabled` döner.
- Dayanak ve kanıtı (`TransferReference`: örn. "Standart sözleşme 12.10.2026, Kurul bildirimi KEP 2026-…") Sistem → Yapay zeka kullanımı ekranında görünür.
- Veri minimizasyonu (§4): kimlik ve iletişim bilgileri ile özel nitelikli veriler gönderilmez.
- Otomatik karar yok: sonuçlar öneridir (md. 11/1-g'deki itiraz hakkı korunur).
- Prompt ve çıktı saklanmaz. Kullanım kaydında sadece kim, ne zaman, hangi özellik ve maliyet bilgisi tutulur.

### 10.3 Veri sorumlusunun (müşterinin) yapacakları

Bunlar yazılımla çözülemez; müşterinin hukuk birimi veya danışmanı yürütür:

1. Sağlayıcıyla Kurul'un standart sözleşmesini imzalamak; uygun modülü seçmek (veri sorumlusundan veri işleyene). SaaS kurulumunda ürünü işleten firma veri işleyendir; o durumda veri işleyenden veri işleyene modülü de gerekir.
2. Sözleşmeyi imzadan itibaren 5 iş günü içinde Kuruma bildirmek ve bildirim tarihini, referansını `TransferReference` olarak kaydetmek.
3. Sağlayıcının veri işleme sözleşmesini (DPA) imzalamak. Verinin model eğitiminde kullanılmadığını ve saklama süresini sözleşmede teyit etmek.
4. Aydınlatma metinlerini güncellemek (md. 10). Aday metnine eklenecek örnek madde (hukuk danışmanınca gözden geçirilmelidir):

   > Başvurunuz, işe alım uzmanlarımıza yardımcı olmak amacıyla yapay zeka destekli bir araçla ön değerlendirmeden geçirilebilir. Bu değerlendirmede ad, iletişim bilgileri ve özel nitelikli kişisel verileriniz çıkarılır; yalnızca eğitim, deneyim ve yetkinliklerinize ilişkin bilgiler, KVKK md. 9 uyarınca imzalanan standart sözleşme kapsamında yurt dışında bulunan hizmet sağlayıcıya aktarılır. Değerlendirme sonucu yalnızca öneri niteliğindedir; işe alım kararını yetkili çalışanlarımız verir. Münhasıran otomatik sistemlerle analiz edilmesi sonucu aleyhinize bir sonuç doğduğunu düşünüyorsanız itiraz edebilirsiniz.

5. VERBİS kaydını güncellemek (yükümlüyse): alıcı grubu ve yurt dışına aktarılan veri kategorileri.
6. Gerekirse çalışanlar için de aynı adımları uygulamak. İK özellikleri (A7–A11) çalışan verisi işler.

Kurum içi model (§9) kullanılırsa veri yurt dışına çıkmaz ve bu bölüm uygulanmaz.
