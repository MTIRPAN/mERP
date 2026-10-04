# İzin Kuralları — Mevzuat Varsayılanları ve Politika Modeli

> Bu belge hukuki görüş değildir. Seed değerleri yayına almadan önce bir mali müşavir veya iş hukukçusu teyit etmeli. "Teyit" işaretli maddeler özellikle kontrol edilmeli.

## 1. Tasarım ilkesi

Hiçbir izin kuralı kodda sabit değildir. Kurallar üç katmanda tanımlanır:

1. **İzin türü** (`LeaveType`): izin türünün kendi davranışı (ücretli mi, bakiyeden düşer mi, belge ister mi...).
2. **İzin politikası** (`LeavePolicy`): yıllık izin hak edişi, birikim, avans, devir. Şirket ve personel grubu bazında tanımlanır. Tarihçeli sürümler halinde tutulur (`ValidFrom`).
3. **Politika atama kuralı**: hangi personele hangi politikanın uygulanacağı.
   - Kriterler: şirket, şube, istihdam tipi, birim, unvan seviyesi, özel alan.
   - Öncelik sırasına göre ilk eşleşen kural uygulanır. Personel bazında elle ezme yapılabilir.

Seed, Türkiye özel sektör (4857 sayılı İş Kanunu) varsayılanlarını kurar. Farklı kuralları olan şirket kendi politikasını tanımlar. Kanuni alt sınırın altına düşen ayarlar kaydedilebilir ama arayüzde uyarı gösterilir (`LegalMinimumViolation`).

## 2. Yıllık izin — kanuni varsayılanlar (4857 md. 53–56, 59)

| Kural | Varsayılan değer | Politika parametresi |
|---|---|---|
| Hak kazanma için en az hizmet | 1 yıl (deneme süresi dahil) | `minServiceYearsForEntitlement = 1` |
| 1 yıl ≤ kıdem < 5 yıl | 14 gün | `tiers[]` |
| 5 yıl ≤ kıdem < 15 yıl | 20 gün | `tiers[]` |
| kıdem ≥ 15 yıl | 26 gün | `tiers[]` |
| 18 yaş ve altı veya 50 yaş ve üstü | En az 20 gün | `ageMinimums[] = [{maxAge:18, minDays:20}, {minAge:50, minDays:20}]` |
| Yer altı maden işleri | +4 gün (yaş alt sınırı uygulandıktan sonra eklenir) | `extraDayRules[]` (kriter: özel alan `underground = true`) |
| Hak ediş zamanı | Her kıdem yıldönümünde, o güne kadar tamamlanan yıla ve o günkü yaşa göre toptan | `accrualMethod = AnniversaryLumpSum` |
| Süre hesabı | İş günü. Hafta tatili, ulusal bayram ve genel tatil günleri sayılmaz | `LeaveType.durationBasis = WorkingDays` |
| Bölme | Tarafların anlaşmasıyla bölünebilir, bir bölümü en az 10 gün olmalı (6704 s. K. ile 2016'dan beri) | `tenDayBlockRule = Warn` (`Off` / `Warn` / `Enforce`) |
| Yol izni | Başka yerde kullanılırsa talep ve belgeyle 4 güne kadar ücretsiz yol izni | `allowTravelDays = true`, `maxTravelDays = 4` |
| Devir | Kullanılmayan izin yanmaz, devreder | `carryOverLimitDays = null` (sınırsız). Sınır konursa uyarı gösterilir |
| İşten çıkış | Kalan bakiye ücret olarak ödenir | `EmployeeTerminated` ile kalan gün bilgisi yayınlanır (bordro sonra) |
| Kayıt | İşveren yıllık izin kayıt belgesi tutar | Rapor + PDF şablonu |
| Kıdem başlangıcı | İstihdamın `SeniorityBasisDate` alanı. Önceki hizmetler girilerek geriye alınabilir, gerekçe zorunlu | — |
| Grup kıdemi | Holding içi önceki istihdamlar sayılsın mı | `useGroupSeniority = false` |
| Ücretsiz izin hak edişi kaydırır mı | Yoruma açık, **teyit** | `unpaidLeaveShiftsAccrualDate = false` |

### Şirket politikasıyla eklenebilecek, kanunda olmayan seçenekler

- **Birikim yöntemi:** `MonthlyAccrual` (yıllık hakkın 1/12'si her ay) veya `CalendarYearLumpSum` (her 1 Ocak'ta). İlk yıl kısmi olabilir. Kanuni alttan az vermediği doğrulanır.
- **Avans izin (negatif bakiye):** `advanceLimitDays` (varsayılan 0).
- **İlk yıl dolmadan izin kullandırma:** `allowBeforeFirstEntitlement` (avans gibi çalışır).
- **Ek gün kuralları:** Örnek: "unvan seviyesi ≥ M1 ise +2 gün". `extraDayRules[]`
- **Yarım gün ve saatlik izin:** `allowHalfDay = true`, `allowHourly = false`, `hoursPerDay` çalışma takviminden gelir.
- **Yuvarlama:** `rounding = None | HalfDayDown | HalfDayNearest`. Ledger her zaman 2 ondalık saklar.
- **Holding içi nakilde bakiye:** `transferBalanceOnGroupTransfer = true`. Bakiye taşınır; `false` ise çıkışta ödenir.
- **Talep kuralları:** en az bildirim süresi (`minNoticeDays`), talep başına en fazla gün, geçmiş tarihe talep (sadece İK).

## 3. İzin türleri — seed

| Kod | Ad | Kategori | Ücret | Bakiyeden düşer | Süre | Süre tabanı | Belge | Dayanak |
|---|---|---|---|---|---|---|---|---|
| `YILLIK` | Yıllık ücretli izin | Annual | Ücretli | Evet | Politikaya göre | İş günü | Hayır | md. 53–56 |
| `EVLILIK` | Evlilik izni | Excuse | Ücretli | Hayır | 3 gün / olay | Takvim günü **(teyit)** | Evet | Ek md. 2 |
| `EVLAT_EDINME` | Evlat edinme (mazeret) | Excuse | Ücretli | Hayır | 3 gün / olay | Takvim günü **(teyit)** | Evet | Ek md. 2 |
| `VEFAT` | Vefat izni (ana, baba, eş, kardeş, çocuk) | Excuse | Ücretli | Hayır | 3 gün / olay | Takvim günü **(teyit)** | Evet | Ek md. 2 |
| `BABALIK` | Babalık izni | Excuse | Ücretli | Hayır | 10 gün / olay (01.05.2026 öncesi doğumlar: 5 gün) | Takvim günü **(teyit)** | Evet | Ek md. 2 (01.05.2026 değişikliği) |
| `ENGELLI_COCUK` | Engelli / süreğen hastalığı olan çocuğun tedavisi | Excuse | Ücretli | Hayır | Yılda 10 güne kadar | İş günü **(teyit)** | Evet | Ek md. 2 |
| `DOGUM` | Doğum izni | Maternity | SGK ödeneği | Hayır | 24 hafta: doğumdan önce 8, sonra 16. Çoğul gebelikte doğum öncesine +2 hafta. | Takvim günü | Evet | md. 74 (01.05.2026 değişikliği) |
| `DOGUM_SONRASI_UCRETSIZ` | Doğum sonrası ücretsiz izin | Unpaid | Ücretsiz | Hayır | Talep halinde 6 aya kadar | Takvim günü | Hayır | md. 74 |
| `SUT` | Süt izni | Arrangement | Ücretli | Hayır | 1 yaşından küçük çocuk için günde 1,5 saat | Saat | Hayır | md. 74 |
| `RAPOR` | İstirahat raporu | Sick | SGK | Hayır | Rapora göre | Takvim günü | Evet (rapor no) | 5510 |
| `UCRETSIZ` | Ücretsiz izin | Unpaid | Ücretsiz | Hayır | Şirket kuralı | İş günü | Hayır | Şirket |
| `IDARI` | İdari izin | Administrative | Ücretli | Hayır | Şirket kuralı | İş günü | Hayır | Şirket |
| `YOL` | Yol izni | Travel | Ücretsiz | Hayır | En fazla 4 gün, `YILLIK` talebine ek | Takvim günü | Evet | md. 56 |
| `SAATLIK` | Saatlik mazeret | Excuse | Ücretli | Politikaya göre (yıllıktan düşebilir) | Saat | Saat | Hayır | Şirket |

**Notlar:**
- **Doğum ve babalık kurallarının tarihçesi:** Kurallar `ValidFrom` ile sürümlenir. Hangi sürümün uygulanacağına doğum tarihi karar verir.
  - Seed iki sürüm içerir: 01.05.2026 öncesi ve sonrası.
  - 01.05.2026'da izni devam edenlere ek süre verilip verilmediği **teyit** edilmeli.
- **`SUT` türü:** v1'de günlük talep akışı yok. Sadece "düzenleme kaydı" (başlangıç ve bitiş tarihi, saat aralığı) olarak tutulur.
- **`DOGUM` öncesi süre:** Doğum öncesi kullanılmayan süre doğum sonrasına eklenir. Örnek: hekim onayıyla doğumdan 3 hafta öncesine kadar çalışma; erken doğum. Modelde doğum öncesi ve sonrası iki parça olarak tutulur, gerçekleşen doğum tarihiyle yeniden hesaplanır.

**Her `LeaveType` alanı:**
- `code`, `name` (`LocalizedString`), `category`, `isPaid`, `payer` (Employer / SGK / None)
- `deductsFrom` (null veya bakiye türü), `unit` (Day / HalfDay / Hour)
- `durationBasis` (WorkingDays / CalendarDays)
- `fixedDurationPerEvent?`, `maxPerYear?`, `maxPerRequest?`, `minNoticeDays?`
- `requiresDocument`, `requiresApproval`, `genderRestriction?`
- `allowPastDates` (İK için), `legalBasis` (metin), `isActive`, `companyId?`

## 4. Süre hesaplama

`ILeaveDurationCalculator` (HR) aşağıdaki girdilerden süre hesaplar:
- Personelin o tarihteki çalışma takvimi (`IWorkingCalendar`, Platform.Contracts)
- Resmi tatil takvimi
- İzin türünün süre tabanı

**İş günü tabanı:**

1. Aralıktaki her gün için çalışma takvimine bakılır. Haftalık desen 5 veya 6 gün olabilir, cumartesi ayarlanabilir.
2. Çalışma günü değilse 0 sayılır.
3. Tam gün resmi tatilse 0 sayılır.
4. Yarım gün tatilse (arife ve 28 Ekim, 13:00'ten itibaren) 0,5 sayılır.
5. Talebin başlangıç veya bitişi yarım günse (Sabah / Öğleden sonra) o gün 0,5 sayılır.
6. Yarım gün talep ile yarım gün tatil aynı yarıya denk gelirse o gün 0 sayılır.
7. Sonuç 0 ise talep reddedilir (`hr.leave.errors.zero_duration`).

**Diğer tabanlar:**
- **Takvim günü:** Aralıktaki tüm günler sayılır.
- **Saat:** Başlangıç ve bitiş saati, çalışma takvimindeki mesai saatleriyle kesiştirilir. Bakiyeden düşecekse `saat / hoursPerDay` ile güne çevrilir.

## 5. Bakiye modeli (ledger)

Bakiye hiçbir yerde sayı olarak saklanmaz. Hareketlerin toplamıdır.

`LeaveLedgerEntry` alanları: `EmploymentId`, `LeaveTypeCode`, `Date`, `Quantity` (+/−, 2 ondalık), `Source`, `SourceId`, `PeriodKey`, `Note`, `CreatedBy`.

**`Source` değerleri:**

| Değer | Anlamı |
|---|---|
| `Accrual` | Hak ediş |
| `Request` | Onaylanan talep (−) |
| `RequestCancel` | Onaylı talebin iptali (+) |
| `Adjustment` | İK düzeltmesi, gerekçe zorunlu |
| `OpeningBalance` | Eski sistemden açılış bakiyesi |
| `Transfer` | Holding içi nakil |
| `Termination` | İşten çıkışta kapanış |

**Kurallar:**
- **İdempotent hak ediş:** `(EmploymentId, LeaveTypeCode, Source=Accrual, PeriodKey)` tekildir. `PeriodKey` örnekleri: `2026-03-15` veya `2026-07`.
- **Bekleyen talepler:** Onay bekleyen talepler "rezerve" olarak hesaplanır, ledger'a yazılmaz. Kullanılabilir bakiye = ledger toplamı − bekleyen talepler.
- **Hak ediş işi:**
  - Hangfire'da her gece 01:00'de, tenant ve şirket saat dilimine göre çalışır.
  - O gün yıldönümü olan veya aylık birikim günü gelen istihdamlara hareket yazar.
  - Kaçırılan günleri telafi eder (son çalışma tarihinden bugüne).
- **İzin yılı:** Bir sonraki hak edişe kadarki dönem. Raporlar bu döneme göre gruplanır.

## 6. Talep doğrulama kuralları

Sırasıyla kontrol edilir; ilk hata döner, uyarılar toplanır.

1. Personelin talep tarihinde aktif istihdamı var.
2. İzin türü aktif ve personele uygun (cinsiyet kısıtı, politika).
3. Tarihler geçerli. Geçmiş tarih sadece `allowPastDates` ve İK yetkisiyle girilebilir.
4. Süre 0'dan büyük, `maxPerRequest` ve `maxPerYear` aşılmıyor.
5. Aynı personelin onaylı veya bekleyen başka izniyle çakışma yok.
6. `minNoticeDays` sağlanıyor. Sağlanmıyorsa İK için uyarı, çalışan için hata.
7. Bakiyeden düşen türlerde: `kullanılabilir bakiye − süre ≥ −advanceLimitDays`.
8. Belge gerekiyorsa en az bir ek var. Bazı türlerde onaydan önce eklenebilir: `documentDueBeforeApproval`.
9. Uyarı: 10 gün kuralı (`tenDayBlockRule = Warn`), kanuni alt sınır uyarıları.

**Onay sonrası iptal:**
- İzin başlamadıysa çalışan iptal talebi açar. Akış: `CancellationRequested` → onay → `RequestCancel` ledger hareketi.
- İzin başladıysa sadece İK "kısmi iptal / erken dönüş" yapabilir. Kalan günler iade edilir.

## 7. Resmi tatil seed'i (Türkiye)

Kaynak: 2429 s. Kanun (sabit tatiller) ve Diyanet takvimi (dini bayramlar). Dini bayram tarihleri her yıl yeni yıl için admin ekranından eklenir. Eklenmezse arka plan işi sistem yöneticisine 1 Ekim'de hatırlatır.

Arifeler ve 28 Ekim yarım gündür, 13:00'ten itibaren tatildir.

**2026:**

| Tarih | Ad | Tür |
|---|---|---|
| 01.01 | Yılbaşı | Tam |
| 19.03 | Ramazan Bayramı arifesi | Yarım |
| 20–22.03 | Ramazan Bayramı | Tam |
| 23.04 | Ulusal Egemenlik ve Çocuk Bayramı | Tam |
| 01.05 | Emek ve Dayanışma Günü | Tam |
| 19.05 | Atatürk'ü Anma, Gençlik ve Spor Bayramı | Tam |
| 26.05 | Kurban Bayramı arifesi | Yarım |
| 27–30.05 | Kurban Bayramı | Tam |
| 15.07 | Demokrasi ve Milli Birlik Günü | Tam |
| 30.08 | Zafer Bayramı | Tam |
| 28.10 | Cumhuriyet Bayramı arifesi | Yarım |
| 29.10 | Cumhuriyet Bayramı | Tam |

**2027:**

| Tarih | Ad | Tür |
|---|---|---|
| 01.01 | Yılbaşı | Tam |
| 08.03 | Ramazan Bayramı arifesi | Yarım |
| 09–11.03 | Ramazan Bayramı | Tam |
| 23.04 | Ulusal Egemenlik ve Çocuk Bayramı | Tam |
| 01.05 | Emek ve Dayanışma Günü | Tam |
| 15.05 | Kurban Bayramı arifesi | Yarım |
| 16–19.05 | Kurban Bayramı (19.05 aynı zamanda Gençlik ve Spor Bayramı) | Tam |
| 15.07 | Demokrasi ve Milli Birlik Günü | Tam |
| 30.08 | Zafer Bayramı | Tam |
| 28.10 | Cumhuriyet Bayramı arifesi | Yarım |
| 29.10 | Cumhuriyet Bayramı | Tam |

## 8. Test senaryoları (unit test olarak birebir yazılmalı)

### 8.1 Süre hesabı

Aksi belirtilmedikçe 5 günlük çalışma haftası (Pzt–Cum) varsayılır.

| # | Talep | Beklenen | Neden |
|---|---|---|---|
| S1 | 26.10.2026 Pzt – 30.10.2026 Cum | 3,5 | 28.10 arife 0,5; 29.10 tatil 0 |
| S2 | 25.05.2026 Pzt – 29.05.2026 Cum | 1,5 | 26.05 arife 0,5; 27–29.05 bayram 0 |
| S3 | 08.03.2027 Pzt – 13.03.2027 Cmt, 6 günlük hafta | 2,5 | 08.03 arife 0,5; 09–11.03 bayram 0; Cuma 1; Cumartesi 1 |
| S4 | 17.05.2027 Pzt – 21.05.2027 Cum | 2 | 17–19.05 bayram 0 |
| S5 | 31.12.2026 Per – 01.01.2027 Cum | 1 | Yılbaşı 0 |
| S6 | 28.10.2026, sadece öğleden sonra | Hata `zero_duration` | Yarım gün tatille aynı yarı |
| S7 | 30.10.2026 Cum – 02.11.2026 Pzt | 2 | Hafta sonu sayılmaz |
| S8 | `EVLILIK`, 30.10.2026 Cum'dan başlayan | 3 takvim günü, bitiş 01.11.2026 | Takvim günü tabanı |

### 8.2 Hak ediş

| # | Personel | Tarih | Beklenen |
|---|---|---|---|
| E1 | Kıdem başlangıcı 10.11.2025 | 05.10.2026'da yıllık izin talebi | Hata `insufficient_balance` (avans 0). 10.11.2026'da +14 |
| E2 | Kıdem 15.03.2021 | 15.03.2025 hak ediş | +14 (4 yıl) |
| E2b | Aynı personel | 15.03.2026 hak ediş | +20 (5 yıl) |
| E3 | Kıdem 01.07.2011 | 01.07.2026 | +26 (15 yıl) |
| E4 | Doğum 10.02.1976, kıdem 01.06.2023 | 01.06.2026 | +20 (3 yıl ama 50 yaş) |
| E5 | Doğum 01.08.2007, kıdem 01.07.2025 | 01.07.2026 | +20 (18 yaş) |
| E6 | Yer altı, kıdem 3 yıl, 50 yaş | Yıldönümü | +24 (20 + 4) |
| E7 | Politika: M1+ unvan +2 gün, kıdem 2 yıl | Yıldönümü | +16 |
| E8 | Hak ediş işi aynı gün iki kez çalışır | — | Tek hareket (idempotent) |
| E9 | Holding içi nakil, A'da bakiye 8, `transferBalanceOnGroupTransfer = true` | Nakil günü | A'da −8 Transfer, B'de +8 Transfer |
| E10 | Avans limiti 5, bakiye 3 | 7 günlük talep | Kabul (sonuç −4) |
| E10b | Aynı | 9 günlük talep | Hata `insufficient_balance` |
| E11 | `BABALIK`, doğum 20.04.2026 | — | 5 gün |
| E11b | `BABALIK`, doğum 10.05.2026 | — | 10 gün |
| E12 | Onaylı 5 günlük talep, başlamadan iptal | — | Ledger: −5 Request, +5 RequestCancel; bakiye eski haline döner |
| E13 | Çakışan iki talep | İkinci talep | Hata `overlapping_request` |
