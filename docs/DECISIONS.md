# mERP — Mimari Kararlar

Her madde kısa bir karar kaydıdır (ADR). Değişiklik gerekirse maddeyi silme. Yeni madde ekle ve eskisini "yerini aldı: Dxx" diye işaretle.

---

## D1 — Modüler monolit

**Karar:** Tek deploy edilen .NET uygulaması, içinde sınırları kesin modüller.

**Gerekçe:**
- ERP işlemleri modüller arasında sıkı bağlı.
- Mikroservis baştan dağıtık transaction ve operasyon yükü getirir.

**Mikroservise geçiş yolu:** Bir modül ayrı host'a taşınır.
- `Contracts` içindeki senkron sorgu arayüzlerinin implementasyonu HTTP/gRPC istemcisine döner.
- Outbox transport'u in-process'ten RabbitMQ'ya alınır.
- Modülün şeması ayrı veritabanına taşınır.
- İş kodu değişmez. Bu ancak CLAUDE.md'deki mimari kurallara uyulursa mümkündür, bu yüzden kurallar testle denetlenir.

## D2 — İsimlendirme

- Ürün adı **mERP**. C# kök namespace `Merp` (`Merp.Modules.HR.Domain`).
- İki harfli kısaltmalar büyük yazılır (`HR`). Daha uzunlar Pascal case yazılır (`Api`, `Pdf`).
- Modüller ve şemaları:
  - `Platform` → `platform`
  - `HR` → `hr`
  - `Recruitment` → `recruitment`
- Katalog için ayrı veritabanı kullanılır: `merp_catalog`.

## D3 — Çoklu kiracı (multi-tenancy)

**Karar:** Müşteri (tenant) başına ayrı veritabanı. Holdingin şirketleri aynı veritabanında `company_id` ile ayrılır.

- **Katalog DB** (`merp_catalog`): `tenants` (id, code, name, connection_string (şifreli), status, default_culture), Hangfire tabloları.
- **Tenant çözümleme:**
  - Kimliği doğrulanmış isteklerde JWT'deki `tenant` claim'inden (Keycloak kullanıcı attribute mapper).
  - Anonim isteklerde (kariyer sayfası) URL'deki `{tenantCode}`'dan.
- `ITenantContext` istek boyunca taşınır. DbContext'ler connection string'i buradan alır.
- Arka plan işleri ve outbox dağıtıcısı tenant'ları tek tek dolaşır. Her iş tenant id parametresi taşır.
- On-prem kurulum tek tenant'lı aynı koddur.
- Keycloak'ta tek realm (`merp`) kullanılır. Müşteri kendi AD/Entra ID'sini bağlamak isterse realm-per-tenant'a geçilebilir; sadece tenant çözümleme değişir.

## D4 — Şirket bağlamı

- `ICompanyScoped` arayüzünü uygulayan her kayıt `CompanyId` taşır.
- Arayüzde bir **aktif şirket** seçicisi bulunur ve `X-Company-Id` başlığıyla gönderilir. Bu başlık listeleri daraltır ve yeni kayıt için varsayılan şirketi belirler. Yetkiyi genişletmez.
- Kullanıcının görebileceği şirketler rol atamalarının kapsamından gelir (D5, `docs/domain/yetki-ve-onay.md`).

## D5 — Kimlik doğrulama ve yetkilendirme

**Karar:** Kimlik doğrulama Keycloak'ta (OIDC, PKCE), yetkilendirme uygulamanın içinde yapılır.

**Gerekçe:**
- ERP yetkileri ince taneli ve veri kapsamlı. Örnek: "İstanbul şubesindeki personelin izinlerini okuyabilir".
- Keycloak rolleri bunu taşıyamaz.

**Model** (detay `docs/domain/yetki-ve-onay.md`):
- Yetki (permission) kodda tanımlanır: `{modül}.{kaynak}.{eylem}`.
- Rol tenant'a özel bir yetki kümesidir.
- Rol ataması = kullanıcı + rol + kapsam (Tenant / Company / Branch / OrgUnit(+alt birimler) / ManagedTeam / Self) + geçerlilik tarihleri.
- Onay görevi atanan kişi, ilgili belgeyi modül yetkisi olmasa da görebilir (görev bazlı erişim).

## D6 — Kullanıcı ve personel ayrıdır

**Karar:** `User` (Platform) ile `Employee` (HR) farklı kavramlardır. İsteğe bağlı 0..1 ilişkiyle bağlanırlar.

- **`User`:** Sisteme giriş yapan kimlik. Keycloak `sub` ile eşlenir. Rolleri ve kapsamları vardır.
  - Personel olmayan kullanıcılar olabilir: danışman, denetçi, servis hesabı, dış işe alım uzmanı.
- **`Employee`:** İK kaydıdır. Sisteme hiç girmeyen personel olabilir, örneğin sahadaki işçi.
- **Bağlantı:** `User.EmployeeId` ve `Employee.UserId`. Tenant içinde iki yönde de tekil.
- **Personelden kullanıcı oluşturma:**
  - İK "Sistem kullanıcısı oluştur" der.
  - Platform Keycloak'ta kullanıcı açar ve davet e-postası gönderir.
  - Kullanıcıya "Çalışan" rolü Self kapsamıyla atanır.
- **İşten çıkış:** `EmployeeTerminated` olayı gelir. Kullanıcı çıkış tarihinde pasifleşir. Ayar: hemen veya çıkış tarihinde.
- **Personel modeli:**
  - `Employee`: kişi, tenant genelinde tekil, TCKN ile.
  - Altında `Employment`: şirket bazında istihdam dönemi. Holding içi nakilde eski istihdam kapanır, yenisi açılır, kıdem taşınabilir.
  - Onun altında `JobAssignment`: birim, pozisyon, yönetici, şube; tarihçeli.

## D7 — Onay motoru

**Karar:** Platform modülünde kendi onay motorumuz. Elsa vb. genel iş akışı motoru kullanmıyoruz.

**Gerekçe:**
- İhtiyaç onay zincirleri; genel BPM gerekmiyor.
- Elsa Studio Blazor tabanlı, React arayüzle uyumsuz.
- Kendi motorumuzla vekalet, kapsam ve Türkçe süreçler üzerinde tam kontrol sağlıyoruz.

**Model:** Kurallar `docs/domain/yetki-ve-onay.md`'de.
- Belge türü bazında ve şirket bazında ezilebilir akış tanımları.
- Koşullu adımlar.
- Onaylayıcı çözücüler: kullanıcı, rol, hat yöneticisi (n. seviye), birim yöneticisi, pozisyon, olgudan kullanıcı.
- Any/All modu, süre aşımı ve eskalasyon, vekalet, iade.

**Modül entegrasyonu olaylarla yapılır:**
- Modül `ApprovalRequested` olayını yayınlar.
- Platform `ApprovalRequestCompleted` ile geri döner.

## D8 — Modüller arası iletişim

- **Entegrasyon olayları:** `*.Contracts` içinde `record` olarak tanımlanır. `EventId`, `OccurredAt`, `TenantId`, `CompanyId?` taşır.
- **Outbox:** Her modülün şemasında `outbox_messages` tablosu bulunur. Olay, iş verisiyle aynı transaction'da yazılır.
- **Dağıtıcı:** Arka plan servisidir. `FOR UPDATE SKIP LOCKED` ile toplu okur, artan beklemeyle tekrar dener, N denemeden sonra dead-letter'a alır.
- **Inbox:** Her modülde `inbox_messages` tablosu bulunur. `(message_id, handler)` tekil olduğundan her handler idempotent çalışır.
- **Transport:** `IMessageTransport` arayüzünün iki uygulaması var:
  - `InProcess` (varsayılan, monolit)
  - `RabbitMQ` (topic exchange `merp.events`, modül başına kuyruk `merp.{module}`)
  - Seçim konfigürasyondan yapılır.
- **Senkron okuma:** Sadece `Contracts` içindeki salt-okunur arayüzlerle yapılır.

## D9 — Kimlikler ve veri

- Birincil anahtarlar `Guid` v7, strongly typed ID'ler.
- PostgreSQL adlandırması snake_case (EFCore.NamingConventions).
- Optimistic concurrency için `xmin` kullanılır.
- Soft delete sadece ana veride (`is_deleted` + global filter) kullanılır. Belgeler silinmez, iptal edilir.
- Özel alanlar `custom_fields jsonb` kolonunda tutulur (D12).

## D10 — Zaman

- Takvim tarihi `DateOnly`, an `DateTimeOffset` UTC.
- Şirketin saat dilimi `Company.TimeZoneId` (varsayılan `Europe/Istanbul`).
- `TimeProvider` enjekte edilir. Testlerde `FakeTimeProvider` kullanılır.

## D11 — Çoklu dil

**Varsayılan:** Sadece `tr-TR` aktif. Müşteri yeni dil ekleyebilir.

- **Anahtarlar:** `{modül}.{alan}.{anahtar}`. Örnekler: `hr.leave.errors.insufficient_balance`, web metinleri için `web.hr.leave.new_request`.
- **Kaynaklar:**
  - Varsayılan metinler her modülde gömülü `Localization/tr-TR.json` dosyalarında durur.
  - Veritabanındaki `localization_texts(culture, key, value)` tablosu bunları ezer veya yeni dil ekler.
  - `cultures` tablosu: kod, ad, aktif, varsayılan, fallback.
- **Admin arayüzü:**
  - Dil ekleme.
  - Anahtarları Excel olarak dışa aktarıp çeviri sonrası içe aktarma.
  - Tek tek düzenleme.
- **Çalışma zamanı:**
  - Backend `IStringLocalizer` uygular ve HybridCache ile önbellekler.
  - Web, i18next ile `/api/platform/localization/bundles/{culture}` adresinden yükler.
- **Ana veri metinleri:** İzin türü adı gibi metinler `LocalizedString` (jsonb `{"tr-TR": "...", "en-US": "..."}`) olarak tutulur. Değer yoksa varsayılan dile düşer.
- **Kültür seçimi:** Kullanıcının tercih ettiği kültür önce gelir, sonra `Accept-Language`, sonra tenant varsayılanı.

## D12 — Özel alanlar

- `CustomFieldDefinition`:
  - entity_type, code, label (`LocalizedString`)
  - data_type: text, number, date, bool, select, multiselect, user, employee
  - required, options, validation, group, order, company_id (null ise tüm şirketler)
- Değerler entity'nin `custom_fields jsonb` kolonunda tutulur. Doğrulama Application katmanında `ICustomFieldValidator` ile yapılır.
- Filtrelenebilir alanlar için GIN index kullanılır.
- Web'de tek bir `DynamicFields` bileşeni tüm formlarda çalışır.

## D13 — Audit ve KVKK

- **Audit kaydı:** SaveChanges interceptor'ı `[Audited]` entity'lerdeki alan değişikliklerini modülün `audit_entries` tablosuna yazar: kim, ne zaman, entity, id, alan, eski değer, yeni değer, korelasyon id.
- **Audit okuma:** Platform tüm modüllerin audit kayıtlarını `IAuditReader` arayüzüyle okur. Her modül bu arayüzü kaydeder.
- **Hassas kişisel veri** (sağlık, engellilik, adli sicil, kan grubu, din, sendika):
  - Ayrı tabloda tutulur.
  - Alan düzeyinde şifrelenir (ASP.NET Data Protection).
  - Ayrı yetki gerekir.
  - Okunması da audit'e yazılır.
- **Aday verisi:** Açık rıza kaydı (metin sürümü + tarih) tutulur. Saklama süresi dolunca anonimleştirme işi çalışır.

## D14 — Belge şablonları ve PDF

**Karar:** Liquid şablonları (Fluid) → HTML → PDF (Gotenberg konteyneri).

**Gerekçe:**
- Şablonlar müşteri tarafından düzenlenebilir: teklif mektubu, izin formu, yıllık izin kayıt belgesi.
- Tamamen ücretsiz.
- QuestPDF'in lisansı son müşterinin cirosuna bağlı; holdinglerde ücretli lisans gerekir (D19).

## D15 — Raporlama

- Liste ekranlarında kolon seçimi, filtre ve sunucu tarafı Excel dışa aktarma bulunur (ClosedXML).
- Baskı formatındaki çıktılar D14'teki şablonlarla üretilir.
- Ücretsiz ve kaliteli bir web rapor tasarımcısı yok. v1'de olmayacak.
- İleride gömülü BI için Apache Superset (Apache-2.0) değerlendirilecek.

## D16 — Dosya depolama

- `IFileStorage` arayüzünün iki uygulaması var:
  - `FileSystem` (varsayılan, on-prem için en basit)
  - `S3` (AWSSDK.S3, her S3 uyumlu depoyla çalışır)
- Metadata Platform'da `stored_files` tablosunda tutulur. İndirme yetki kontrollü endpoint üzerinden yapılır.
- MinIO kullanılmaz. Topluluk sürümü 2025'te bakım moduna alındı ve hazır imaj dağıtımı durdu. S3 gerekirse SeaweedFS veya Garage değerlendirilir.

## D17 — Gözlemlenebilirlik

- Serilog ile yapılandırılmış loglama, OpenTelemetry ile trace/metric/log (OTLP).
- Geliştirmede .NET Aspire Dashboard konteyneri kullanılır.
- Üretimde müşterinin tercih ettiği OTLP alıcısı kullanılır (Grafana stack vb.).
- Seq kullanılmaz; çok kullanıcılı üretim kullanımı ücretli.

## D18 — Frontend

| Konu | Seçim |
|---|---|
| Proje | Vite, React, TypeScript (strict) |
| UI | Ant Design (`tr_TR` locale, dayjs `tr`) |
| Tablolar | antd Table (sunucu tarafı sayfalama, sıralama, filtre) |
| Kanban | @dnd-kit |
| Takvim | FullCalendar (sadece MIT core paketleri) |
| Zengin metin | TipTap (sadece MIT paketleri) |
| Organizasyon şeması | d3-org-chart |
| Grafikler | Apache ECharts (echarts-for-react) |
| Kod editörü (şablonlar) | @monaco-editor/react |
| Routing ve veri | React Router, TanStack Query |
| API istemcisi | orval (OpenAPI'den TanStack Query hook'ları) |
| Kimlik | oidc-client-ts + react-oidc-context (Keycloak, PKCE, sessiz yenileme) |
| Dil | i18next + react-i18next (backend'den yükleme) |
| Gerçek zamanlı | @microsoft/signalr |
| Test | Vitest, Testing Library, Playwright |

## D19 — Kütüphaneler ve lisans politikası

**İzinli lisanslar:** MIT, Apache-2.0, BSD-2/3, MPL-2.0, PostgreSQL, ISC. LGPL sadece dinamik bağlantıyla kullanılır (Hangfire).

### Backend — kullanılacaklar

| Amaç | Paket | Lisans |
|---|---|---|
| Web | ASP.NET Core 10 Minimal API, Microsoft.AspNetCore.OpenApi, Scalar.AspNetCore | MIT |
| ORM | EF Core 10, Npgsql.EntityFrameworkCore.PostgreSQL, EFCore.NamingConventions | MIT / PostgreSQL / Apache-2.0 |
| Ham SQL / rapor | Dapper | Apache-2.0 |
| Mediator | **Kendi implementasyonumuz** (`Merp.Infrastructure/Messaging`), DI dekorasyonu için Scrutor | MIT |
| Mapping | Elle `Select` projeksiyonu, gerekirse Mapperly | Apache-2.0 |
| Validasyon | FluentValidation | Apache-2.0 |
| Mesajlaşma | RabbitMQ.Client + kendi outbox/inbox | Apache-2.0 / MPL-2.0 |
| Arka plan işleri | Hangfire.Core + Hangfire.PostgreSql | LGPL-3.0 |
| Cache | Microsoft.Extensions.Caching.Hybrid + StackExchange.Redis (Valkey'e bağlanır) | MIT |
| Kimlik | Microsoft.AspNetCore.Authentication.JwtBearer, Keycloak.AuthServices.Sdk (admin API) | MIT |
| Loglama / izleme | Serilog, OpenTelemetry | Apache-2.0 |
| Excel | ClosedXML | MIT |
| Şablon | Fluid | MIT |
| PDF | Gotenberg (konteyner, HTTP ile) | MIT |
| E-posta | MailKit | MIT |
| Takvim daveti | Ical.Net | MIT |
| CV metni çıkarma | PdfPig, DocumentFormat.OpenXml | Apache-2.0 / MIT |
| Yapay zeka (D24) | Anthropic (resmi C# SDK; bağımlılıkları Microsoft.Extensions.AI.Abstractions, System.Net.ServerSentEvents) | MIT |
| Health check | AspNetCore.HealthChecks.* | Apache-2.0 |
| Test | xunit.v3, Shouldly, NSubstitute, Testcontainers, Respawn, Bogus, NetArchTest.Rules, coverlet | Apache-2.0 / BSD / MIT |

### Altyapı (docker-compose)

PostgreSQL 18, Valkey 8, RabbitMQ 4, Keycloak 26, Mailpit, Gotenberg 8, Aspire Dashboard. Hepsi ücretsiz.

### Kullanılmayacaklar

| Paket | Neden | Yerine |
|---|---|---|
| MediatR, AutoMapper (yeni sürümler) | 2025'te ticari lisansa geçti. Ücretsiz katman 5M$ altı ciro için, müşteriye göre risk. | Kendi mediator, Mapperly |
| MassTransit v9+ | Ticari sürüm | RabbitMQ.Client + kendi outbox |
| FluentAssertions v8+ | Ticari lisans | Shouldly |
| QuestPDF | Lisans ürünü kullanan son müşterinin cirosuna bağlı, 1M$ üstü ücretli | Fluid + Gotenberg |
| EPPlus 5+ | Ticari kullanım ücretli | ClosedXML |
| Duende IdentityServer | Ücretli | Keycloak |
| Redis 8 | AGPL/SSPL/RSAL lisansları | Valkey |
| MinIO | Bakım modunda, imaj yayını durdu | FileSystem / S3 uyumlu alternatif |
| Seq (üretim) | Çok kullanıcı ücretli | OTLP + Aspire Dashboard / Grafana |
| AG Grid Enterprise, Stimulsoft, DevExpress, Telerik, Syncfusion | Ücretli | antd Table + sunucu tarafı Excel |
| FullCalendar Premium (resource timeline), TipTap Pro | Ücretli | MIT paketleri |
| Moq | Tercih edilmez (2023 SponsorLink tartışması) | NSubstitute |

## D20 — Anonim endpoint listesi

Bu liste dışında `AllowAnonymous` kullanılamaz (mimari test denetler).

- `/health/live`, `/health/ready`
- `/api/public/{tenantCode}/careers/**`: kariyer sayfası ve başvuru (rate limit + honeypot)
- `/api/public/{tenantCode}/consents/{token}` ve `/api/public/{tenantCode}/offers/{token}`: aday rıza yenileme ve teklif yanıtı (imzalı, süreli token)
- `/api/platform/localization/bundles/{culture}` (sadece `web.*` anahtarları, giriş ekranı için)

## D21 — Kural 7'nin çalışma zamanında denetimi

**Karar:** CLAUDE.md kural 7 ("bir komut tek aggregate değiştirir") derleme anında denetlenemez. Bu yüzden çalışma zamanında denetlenir:
- `ModuleUnitOfWork` commit öncesinde ChangeTracker'da eklenen, değişen veya silinen aggregate root sayısına bakar. Birden fazlaysa exception fırlatır.
- Domain event handler'larının diğer aggregate'lerde yaptığı değişiklikler bu kontrolden sonra çalıştığı için sayılmaz.
- Ayar: `Persistence:EnforceSingleAggregatePerCommand`. API'de Development ve Testing ortamlarında açık, diğer ortamlarda kapalıdır. Testler ihlali yakalar; üretimde kullanıcı hata görmez.

**Gerekçe:** NetArchTest bağımlılıkları denetler, bir handler'ın kaç aggregate değiştirdiğini göremez. Kural integration testlerinde gerçek komutlarla doğrulanır.

## D22 — Host altyapı endpoint'leri

**Durum:** Kabul edildi (kullanıcı, 2026-10-05).

**Sorun:** D20 dışında `AllowAnonymous` yasak ve her endpoint bir yetki istiyor (kural 6). Ancak bazı host endpoint'leri modül endpoint'i değildir ve bu modele uymaz.

**Karar:** Aşağıdaki endpoint'ler mimari testte ayrı bir listeyle (D22) denetlenir:

| Endpoint | Koruma | Gerekçe |
|---|---|---|
| `/openapi/*`, `/scalar/*` | Sadece `ApiDocs:Enabled = true` iken açılır (varsayılan sadece Development) | Scalar sayfası girişten önce yüklenmeli; `npm run gen:api` dokümanı tokensız okur |
| `/admin/jobs/*` | Hangfire paneli kendi çerez yetkisini uygular: çerez `POST /api/platform/jobs/dashboard-session` (`platform.jobs.view`) ile 15 dakikalık alınır; sadece `Jobs:DashboardTenants` listesindeki tenant'lar | Tarayıcı panele bearer token gönderemez |
| `/hubs/notifications` | Oturum zorunlu, yetki istenmez | Her kullanıcı kendi bildirimlerini alır |
| `/api/platform/me` | Oturum zorunlu, yetki istenmez (`.RequireAuthenticatedUser()`) | Kullanıcının kendi bilgileri, menüsü ve yetkileri; hiç rolü olmayan kullanıcı da "erişiminiz yok" ekranını görebilmeli |

**Karar — Hangfire paneli (kullanıcı, 2026-10-05):** Panel, şirketlerdeki yaygın uygulamaya uygun olarak sadece bilgi işlem / sistem yöneticilerine açıktır.
- `platform.jobs.view` yetkisi sadece "Sistem Yöneticisi" sistem rolüne seed edilir (P2). İK ve diğer iş kullanıcıları paneli görmez.
- Panel salt okunurdur: işler panelden silinemez, yeniden tetiklenemez.
- Panel bütün tenant'ların işlerini gösterdiği için ayrıca tenant listesiyle sınırlanır (`Jobs:DashboardTenants`, varsayılan boş):
  - Tek şirketli kurulumda (müşterinin kendi sunucusu) listeye o şirketin tenant'ı yazılır; şirketin sistem yöneticileri paneli görür.
  - Çok kiracılı kurulumda (SaaS) listeye sadece barındıran ekibin (operatör) tenant'ı yazılır; müşteri yöneticileri paneli görmez.

## D23 — Arayüz görsel dili ve tema

**Karar (kullanıcı isteği, 0.12):** Arayüz kurumsal ama modern yapay zeka ürünlerini andıran bir görünümde olur; açık ve koyu tema desteklenir.

- **Token'lar:** Ant Design tema token'ları `web/src/app/theme/tokens.ts` dosyasında tanımlanır. Bileşenlerde sabit renk yazılmaz; `theme.useToken()` kullanılır.
- **Kurumsal taban:** Nötr yüzeyler (açık: `#F5F6FA` zemin ve beyaz kartlar; koyu: `#0B0D14` zemin ve `#12141C` yüzeyler), yoğun tablolar, 8 px köşe yarıçapı, sade tipografi.
- **Yapay zeka vurgusu:** Birincil renk indigo (`#4F46E5`). İndigo → mor → camgöbeği degradesi sadece marka işaretinde, asistan giriş noktasında, aktif menü göstergesinde ve yapay zeka özelliklerinde (menü öğesi, "Yapay zeka" etiketi, analiz düğmesi, sonuç kartı) kullanılır. Yapay zeka sonuçları her zaman "öneri" uyarısıyla gösterilir (D24).
- **Tema seçimi:** Açık / Koyu / Sistem. Seçim tarayıcıda saklanır; P2 ile kullanıcı tercihine taşınır.
- **Hızlı geçiş:** Ctrl/⌘+K komut paleti menü sayfalarında arar. İleride asistanın doğal dil komutlarıyla genişleyeceği giriş noktasıdır. Ana sayfada "Asistan — Yakında" kartı bulunur.
- **Yazı tipi:** Sistem yazı tipi yığını kullanılır (yüklüyse Inter). Font dosyası paketlenmez; font lisansları (OFL) D19 listesinde yok.

**Not:** TypeScript 6.0 kullanılır. TypeScript 7 yayınlandı ama typescript-eslint henüz desteklemiyor (`<6.1`).

## D24 — Yapay zeka (öneri — onay bekliyor)

**Durum:** Öneri. Kullanıcının isteği üzerine ("yapay zekayı kullanılabilecek aşamalarda kullan, örneğin işe alımda CV analizi") altyapı ve ilk özellik (CV analizi) geliştirildi. Aşağıdaki kararlar ve açık sorular kullanıcı onayı bekliyor. Özellik kataloğu ve kurallar: `docs/domain/yapay-zeka.md`.

**Mimari:**
- Modüller yapay zekayı yalnızca `IAiService` (`Merp.Application.Abstractions.AI`) üzerinden kullanır; sağlayıcı SDK'sına referans vermez. Çıktı her zaman tiplidir: sonuç tipinden JSON şeması üretilir ve sağlayıcı yanıtın şemaya uymasını garanti eder (structured outputs). Serbest metin ayrıştırma yoktur.
- Sağlayıcı Anthropic'tir (resmi C# SDK, MIT). **Karar (kullanıcı, 2026-10-05):** varsayılan model Claude Sonnet 5.5'tir (`claude-sonnet-5-5`, `Ai:Model`). Maliyet model bazlı fiyatlarla hesaplanır (`Ai:Pricing:{model}`; yerleşik fiyatlar Sonnet 5.5 ve Opus 5.5 için). Fiyatı bilinmeyen model en pahalı yerleşik fiyattan hesaplanır, böylece bütçe hiçbir zaman eksik hesaplanmaz. Sabit talimatlar önbelleğe alınır (prompt caching), düşünme derinliği (`effort`) her özellikte açıkça verilir. Model politika gerekçesiyle reddederse sağlayıcı aynı çağrıda önerdiği yedek modeli dener (`fallbacks: "default"`, `Ai:Anthropic:ServerSideFallback`).
- API anahtarı sadece yapılandırmadan okunur (user-secrets veya `Ai__Anthropic__ApiKey`); SDK'nın ortam değişkeni varsayılanları kullanılmaz. Çıkarım bölgesi `Ai:Anthropic:InferenceGeo` (`us` / `global`) ile seçilebilir.
- Başka bir sağlayıcı veya kurum içi model `IAiModelClient` uygulamasıyla eklenir; modüller değişmez.
- Belgeler sağlayıcıya ham gönderilmez. PDF/DOCX metni sunucuda çıkarılır (`IDocumentTextExtractor`), maskelenir ve sadece metin gönderilir. Böylece fotoğraf, üst ve alt bilgi gibi alanlar hiç çıkmaz.

**Yurt dışına aktarım (KVKK md. 9) — karar (kullanıcı "gerekeni yap" dedi, 2026-10-05):**
- 7499 sayılı Kanunla değişen md. 9 ve 10.07.2024 tarihli Yönetmelik uyarınca, yeterlilik kararı yoksa düzenli aktarım uygun güvencelerle yapılır: Kurul'un standart sözleşmesi (imzadan itibaren 5 iş günü içinde Kuruma bildirilir), bağlayıcı şirket kuralları veya taahhütname. 1 Eylül 2024'ten sonra açık rıza sadece arızi (ara sıra) aktarımlarda geçerlidir. Yapay zeka özellikleri veriyi düzenli olarak aktardığı için açık rıza dayanak olarak sunulmaz.
- Ürün güvencesi: Kiracıda aktarım dayanağı (`Ai:Tenants:{kod}:TransferBasis`: `StandardContract`, `BindingCorporateRules` veya `AdequacyDecision`) ve kanıtı (`TransferReference`, örn. bildirim tarihi ve KEP numarası) kayıtlı değilse yapay zeka özellikleri çalışmaz ve menüde görünmez. Dayanak, kullanım ekranında gösterilir.
- Sözleşmenin imzalanması, Kuruma bildirim, aydınlatma metni ve VERBİS güncellemesi veri sorumlusunun (müşterinin) işidir; kontrol listesi `docs/domain/yapay-zeka.md` §10'da. Hukuk danışmanı onayı gerekir.

**Kiracı, bütçe ve kayıt:**
- Varsayılan olarak kapalıdır. Kiracı ve özellik bazında açılır (`Ai:Defaults`, `Ai:Tenants:{kod}`). PLAN P11 ile ayarlar ekranına taşınır.
- Aylık bütçe USD cinsindendir ve Europe/Istanbul takvim ayına göre sayılır. Bütçe dolunca çağrılar 403 `common.errors.ai_budget_exceeded` ile reddedilir.
- Her çağrı `platform.ai_usage` tablosuna yazılır: özellik, talimat sürümü, model, kullanıcı, token sayıları, tahmini maliyet, süre ve sonuç. Prompt ve çıktı saklanmaz, loglanmaz. Kayıt, çağıran komutun transaction'ından bağımsızdır (çağrı başarısız olsa da ücretlendirilir).
- Kullanıcı başına hız sınırı uygulanır (`RateLimiting:Ai`, varsayılan dakikada 10).

**KVKK ve adil değerlendirme:**
- Veri minimizasyonu (KVKK md. 4): `PersonalDataRedactor` e-posta, telefon, T.C. kimlik no, IBAN, profil bağlantıları, aday adı ve etiketli özel alanları (doğum tarihi, yaş, cinsiyet, medeni durum, uyruk, din, sağlık, engellilik, kan grubu, askerlik, adres...) metinden çıkarır. Özel nitelikli veriler (md. 6) dışarı gönderilmez.
- Talimatlar yalnızca işle ilgili niteliklerin değerlendirilmesini ister. Korunan özellikler dikkate alınmaz, kariyer araları cezalandırılmaz, belgenin içindeki talimatlar yok sayılır (prompt injection).
- Kararı insan verir. Sonuçlar "yapay zeka önerisi" uyarısıyla gösterilir. Otomatik eleme, sıralama veya ret yapılmaz (KVKK md. 11/1-g; AB Yapay Zeka Yasası işe alımı yüksek riskli sayar).
- Testlerde gerçek API çağrılmaz; sahte sağlayıcı (`FakeAiModelClient`) kullanılır.

**Açık sorular (kullanıcıya):**
1. ~~Sağlayıcı ve model~~ — karar verildi: Claude Sonnet 5.5.
2. ~~Yurt dışına aktarım (KVKK md. 9)~~ — karar verildi: standart sözleşme (veya bağlayıcı şirket kuralları / yeterlilik kararı); dayanak kayıtlı değilse yapay zeka kapalı. Aydınlatma metinlerine yapay zeka maddesi R1 ile eklenecek.
3. Kurum içi (on-prem) model gerekiyor mu? Örneğin verinin yurt dışına çıkmasını istemeyen müşteriler için. Gereksinimler ve seçenekler `docs/domain/yapay-zeka.md` §9'da. Öneri: talep eden ilk müşteriyle birlikte yapılsın (PLAN A15, A16).
4. Sıra: Yapay zeka altyapısı ve durumsuz CV analizi plan sırasından önce yapıldı. Diğer özellikler (`docs/PLAN.md` "Yapay zeka" bölümü) ait oldukları görevlerle birlikte mi yapılsın?
