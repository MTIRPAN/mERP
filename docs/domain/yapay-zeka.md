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
| 3.2 | **Aday kartında CV analizi:** CV yüklenince analiz başvuruya kaydedilir; aday formu CV'den doldurulur | İşe alım | Elle veri girişini kaldırır | R4 ile (A3) |
| 3.3 | **Toplu ön tarama:** bir ilanın tüm başvurularını gece toplu analiz | İşe alım | Yüksek hacimli ilanlarda hız; Batch API ile yarı maliyet | R4 sonrası (A6) |
| 3.4 | **İlan metni taslağı ve kapsayıcı dil kontrolü:** personel talebinden ilan taslağı; ayrımcı veya dışlayıcı ifadeler için uyarı | İşe alım | Daha hızlı ve tutarlı ilan, ayrımcılık riskini azaltır | R3 ile (A4) |
| 3.5 | **Mülakat soruları ve geri bildirim özeti:** pozisyona göre soru seti; mülakatçı notlarından ortak özet (kör geri bildirim kuralına uyarak) | İşe alım | Yapılandırılmış mülakat, hızlı karar toplantısı | R5 ile (A5) |
| 3.6 | **Aday e-posta taslakları:** şablonu adaya göre kişiselleştirir (ret, davet) | İşe alım | Aday deneyimi | R4/R5 ile (A5) |
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
- **Çıktı:** `CvAnalysisResponse` (analiz, maskeleme sayıları, belgenin kesilip kesilmediği, model, talimat sürümü). Dosya ve analiz saklanmaz. R4 ile analiz başvuruya kaydedilecek.
- **Ekran:** İşe alım → CV analizi. Sonuç "Yapay zeka önerisidir" uyarısıyla gösterilir. Ekran görüntüleri: `docs/screenshots/ai/`.

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
  "Model": "claude-opus-5-5",
  "MaxOutputTokens": 16000,
  "RequestTimeout": "00:05:00",
  "MaxRetries": 2,
  "Anthropic": {
    "ApiKey": "",                     // user-secrets veya Ai__Anthropic__ApiKey; kaynak koda yazılmaz
    "BaseUrl": "https://api.anthropic.com",
    "InferenceGeo": "",               // "", "global" veya "us"
    "ServerSideFallback": true
  },
  "Pricing": { "InputPerMillionTokens": 4, "OutputPerMillionTokens": 20, "CacheReadPerMillionTokens": 0.2, "CacheWritePerMillionTokens": 5 },
  "Defaults": { "Enabled": false, "Features": [ "*" ], "MonthlyBudgetUsd": 0 },   // 0: sınırsız
  "Tenants": { "demo": { "Enabled": true, "MonthlyBudgetUsd": 20 } }
}
```

`Features` desenleri: `*` (hepsi), `recruitment.*` (modülün hepsi), `recruitment.cv_analysis` (tek özellik).

## 6. Maliyet tahmini

Liste fiyatı (Claude Opus 5.5): girdi 4 USD, çıktı 20 USD / milyon token; önbellekten okunan girdi 0,20 USD / milyon token.

| İş | Girdi | Çıktı (düşünme dahil) | Tahmini maliyet |
|---|---|---|---|
| Bir CV analizi | ~4.000–6.000 token | ~2.000–4.000 token | ~0,05–0,10 USD |
| Ayda 1.000 CV | | | ~50–100 USD |
| Toplu tarama (Batch API) | | | yaklaşık yarısı |

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
