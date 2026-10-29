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
- Uygulama ayrıntıları D31'dedir. Metadata tablosu kural 2 gereği Platform'da değil, her modülün kendi şemasındadır.

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
| Organizasyon şeması | Kendi bileşenimiz: yerleşim d3-hierarchy (ISC), kartlar React ile. d3-org-chart, WTFPL lisanslı d3-flextree bağımlılığı yüzünden kullanılmaz (kullanıcı kararı, H1b) |
| Grafikler | Apache ECharts (echarts-for-react) |
| Kod editörü (şablonlar) | @monaco-editor/react + monaco-editor (MIT), uygulamanın kendi paketinden yüklenir (D36); dolaylı bağımlılıklar `marked` (MIT) ve `dompurify` (MPL-2.0 / Apache-2.0) |
| Routing ve veri | React Router, TanStack Query |
| API istemcisi | orval (OpenAPI'den TanStack Query hook'ları) |
| Kimlik | oidc-client-ts + react-oidc-context (Keycloak, PKCE, sessiz yenileme) |
| Dil | i18next + react-i18next (backend'den yükleme) |
| Gerçek zamanlı | @microsoft/signalr |
| Test | Vitest, Testing Library, Playwright |

## D19 — Kütüphaneler ve lisans politikası

**İzinli lisanslar:** MIT, Apache-2.0, BSD-2/3, MPL-2.0, PostgreSQL, ISC, Unlicense (kamu malı; kullanıcı onayı, P8b: `@microsoft/signalr` bağımlılığı `fetch-cookie`), 0BSD (kamu malına yakın; kullanıcı onayı, R4a: `@dnd-kit/core` bağımlılığı `tslib`). LGPL sadece dinamik bağlantıyla kullanılır (Hangfire).

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
| HTML temizleme (zengin metin, D49) | HtmlSanitizer (Ganss.Xss; bağımlılıkları AngleSharp, AngleSharp.Css) | MIT |
| PDF | Gotenberg (konteyner, HTTP ile) | MIT |
| E-posta | MailKit | MIT |
| Takvim daveti | Ical.Net | MIT |
| CV metni çıkarma | PdfPig, DocumentFormat.OpenXml | Apache-2.0 / MIT |
| Dosya depolama (D16, D31) | AWSSDK.S3 (S3 uyumlu depolar için; testlerde SeaweedFS konteyneri) | Apache-2.0 |
| Yapay zeka (D24) | Anthropic (resmi C# SDK; bağımlılıkları Microsoft.Extensions.AI.Abstractions, System.Net.ServerSentEvents) | MIT |
| Health check | AspNetCore.HealthChecks.* | Apache-2.0 |
| Test | xunit.v3, Shouldly, Microsoft.AspNetCore.SignalR.Client, NSubstitute, Testcontainers, Respawn, Bogus, NetArchTest.Rules, coverlet | Apache-2.0 / BSD / MIT |

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
- `/api/platform/localization/bundles/{culture}` (sadece `web.*` anahtarları, giriş ekranı için; token ile kiracının çevirilerini de verir, D28)

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
| `/api/platform/locations/provinces`, `/api/platform/locations/provinces/{provinceCode}/districts` | Oturum zorunlu, yetki istenmez (`.RequireAuthenticatedUser()`) | Kamuya açık referans veri (il/ilçe, `docs/reference-data.md`); her modülün adres formunda kullanılır. Kullanıcı onayıyla eklendi (P1). |
| `/api/platform/me/preferences` | Oturum zorunlu, yetki istenmez (`.RequireAuthenticatedUser()`) | Kullanıcının kendi tercihleri (dil); hiç rolü olmayan kullanıcı da değiştirebilmeli (P2). |
| `/api/platform/custom-fields/form` | Oturum zorunlu, yetki istenmez (`.RequireAuthenticatedUser()`) | Bir formun göstereceği özel alan tanımları (etiket, tip, seçenek). Hassas değil; özel alanı olan her formda gerekir (P6, D30). |
| `/api/platform/attachments`, `/api/platform/attachments/{id}`, `/api/platform/files/{id}` | Oturum zorunlu; yetkiyi dosyanın sahibi olan kaydın `IFileAccessPolicy`'si verir | Dosyaların kendi yetkisi yoktur, ait oldukları kaydın yetkisi ve veri kapsamı geçerlidir (şirket için `platform.companies.read/manage`). Kaydı göremeyen 404, salt okuyabilen yükleme ve silmede 403 alır (P7, D31). |
| `/api/platform/notifications`, `/api/platform/notifications/unread-count`, `/api/platform/notifications/{id}/read`, `/api/platform/notifications/read-all`, `/api/platform/notifications/preferences`, `/api/platform/notifications/test` | Oturum zorunlu, yetki istenmez (`.RequireAuthenticatedUser()`) | Kullanıcının kendi bildirimleri ve tercihleri; başka kullanıcının bildirimi 404 (P8, D32). |
| `/api/platform/approvals/my-tasks`, `/api/platform/approvals/my-tasks/count`, `/api/platform/approvals/tasks/{id}/decision`, `/api/platform/approvals/delegations`, `/api/platform/approvals/delegations/{id}/revoke`, `/api/platform/approvals/preview` | Oturum zorunlu, yetki istenmez (`.RequireAuthenticatedUser()`) | Kullanıcının kendi onay görevleri, kararları, vekaletleri ve kendi belgesinin önizlemesi. Başka kullanıcının görevi ve vekaleti 404 (P10b, D34). |
| `/api/platform/approvals/requests/{id}`, `/api/platform/approvals/timeline` | Oturum zorunlu; talep eden, onaylayıcılar ve vekilleri, `platform.approvals.admin` (Tenant) ya da belgenin modülündeki `IApprovalDocumentAccessPolicy` erişim verir | Onay geçmişi belgenin kendisini görenlere açıktır; diğerleri 404 alır (P10b, D34). |
| `/api/platform/approvals/document-types`, `/api/platform/approvals/users` | Oturum zorunlu, yetki istenmez (`.RequireAuthenticatedUser()`) | Vekalet formunda belge türü adları ve vekil seçimi için aktif kullanıcıların adı (en fazla 20). Hassas alan (e-posta, telefon) dönmez (P10b, D34). |
| `/api/platform/imports`, `/api/platform/imports/{key}`, `/api/platform/imports/{key}/template`, `/api/platform/imports/jobs`, `/api/platform/imports/jobs/{id}`, `/api/platform/imports/jobs/{id}/report`, `/api/platform/imports/jobs/{id}/commit` | Oturum zorunlu; şablon ve yükleme içe aktarma tanımının yetkisini ister (şubeler için `platform.branches.manage`), liste yalnızca yetkili tanımları döner | İçe aktarma tüm modüllerin ortak altyapısıdır, yetkiyi tanım verir. Kullanıcı yalnızca kendi işlerini görür; başkasının işi 404 (P11b, D35). |

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

## D24 — Yapay zeka

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-11). Kullanıcının isteği üzerine ("yapay zekayı kullanılabilecek aşamalarda kullan, örneğin işe alımda CV analizi") altyapı ve ilk özellik (CV analizi) geliştirildi. Özellik kataloğu ve kurallar: `docs/domain/yapay-zeka.md`.

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
3. ~~Kurum içi (on-prem) model~~ — karar verildi (2026-10-11): önerildiği gibi talep eden ilk müşteriyle birlikte yapılır. Kurum içi (on-prem) model gerekiyor mu? Örneğin verinin yurt dışına çıkmasını istemeyen müşteriler için. Gereksinimler ve seçenekler `docs/domain/yapay-zeka.md` §9'da. Öneri: talep eden ilk müşteriyle birlikte yapılsın (PLAN A15, A16).
4. ~~Sıra~~ — karar verildi (2026-10-11): diğer özellikler ait oldukları görevlerle birlikte yapılır. Yapay zeka altyapısı ve durumsuz CV analizi plan sırasından önce yapıldı. Diğer özellikler (`docs/PLAN.md` "Yapay zeka" bölümü) ait oldukları görevlerle birlikte mi yapılsın?

## D25 — Uygulama katmanında veri erişimi

**Durum:** Kabul edildi (P1 sonrası kullanıcı onayı). P1 (ilk gerçek aggregate'ler) ile ortaya çıktı.

**Sorun:** Komut ve sorgu handler'ları Application katmanında durur ama DbContext Infrastructure'dadır (kural 5). Sayfalama, sıralama, arama ve eşzamanlılık her modülde tekrar yazılmamalı.

**Karar:**
- Her modülün Application katmanı kendi veri arayüzünü tanımlar (`IPlatformDbContext`: `DbSet<Company>`...). Modülün DbContext'i bunu uygular, handler'lar arayüzü kullanır. Komutlar sadece ekler veya değiştirir; kaydetmeyi unit of work yapar. Application EF Core'a referans verebilir (kural 5 yalnızca Infrastructure ve sağlayıcı paketlerini yasaklar); Npgsql'e referans vermez.
- Ortak yardımcılar yeni yapı taşı **`Merp.Application.Persistence`** projesindedir (EF Core'a bağlıdır):
  - `ToPagedResultAsync`: sayfa okuma (`PageRequest` → `PagedResult<T>`; `PagedResult`, `PageRequest` ve `SortMap` EF'siz olarak `Merp.Application.Abstractions.Paging` içinde).
  - `SortMap<T>`: sadece tanımlı alanlara göre sıralama, kimlikle kararlı sayfa; bilinmeyen alan 400 `common.errors.invalid_sort`.
  - `TextSearch.Matches`: büyük/küçük harf duyarsız arama; `ModuleDbContext` bunu PostgreSQL `ILIKE`'ın fonksiyonu `texticlike`'a çevirir. Application sağlayıcıya özgü fonksiyon kullanmaz.
  - `ConcurrencyVersion`: `xmin` sürümü DTO'larda `version` olarak döner; güncelleme komutu bu sürümü bekler (`ExpectVersion`), aradaki değişiklik 409 olur.
- `Merp.Application.Abstractions` EF Core'a bağımlı olmaz. Böylece `*.Contracts` projeleri (D1, ayrı servise taşınabilir sözleşmeler) EF Core taşımaz.
- SharedKernel değer nesneleri (ör. `Address`) modül tablolarına owned type olarak eşlenebilir (sütunlar `address_city` gibi). Audit, owned değerlerin değişikliklerini sahibinin alanı olarak yazar (`Address.City`).

**Kapsam sağlayıcısı:** `IAccessScopeProvider.GetCurrentUserScopesAsync` oturumdaki kullanıcının kapsamlarını verir. P1'deki geçici sağlayıcıların yerini P2'de rol atamaları aldı (D26). Testler kapsamı `X-Test-Scopes` başlığıyla verebilir.

## D26 — Kullanıcı, rol ve yetki kontrolü

**Durum:** Kabul edildi (P2 incelemesinden sonra kullanıcı onayı). PLAN P2a ile uygulandı.

**Kimlik zinciri:**
- Her istekte JWT'deki `sub`, kiracının Platform kullanıcısına eşlenir. İlk istekte kullanıcı rolsüz oluşturulur (JIT). Ad, soyad ve e-posta token'dan kopyalanır; eşleme önbelleği saatte bir yenilenir.
- `CurrentUser.UserId` bu kullanıcıdır. Audit kayıtlarına kimin yaptığı artık yazılır.
- Pasif kullanıcı, geçerli token'la gelse de 403 `common.errors.user_inactive` alır.
- Kullanıcının dil tercihi `Accept-Language`'dan önce gelir (D11).

**Yetki hesabı:**
- Yetki, aktif kullanıcının bugün geçerli rol atamalarının rollerinde varsa vardır. Kapsamlar bu atamaların kapsamlarının birleşimidir.
- "Bugün" Europe/Istanbul takvimine göre hesaplanır; atamalar kiracı geneli olduğu için şirket saat dilimi kullanılmaz. Başlangıç ve bitiş günleri dahildir.
- Sonuç HybridCache'te en fazla 10 dakika, gün sonunu geçmeyecek şekilde tutulur. Etiketler kullanıcı (`{kiracı}:user:{id}:perm`) ve kiracı (`{kiracı}:perm`) düzeyindedir. Rol değişince kiracı etiketi, atama/aktiflik/tercih değişince kullanıcı etiketi temizlenir.
- Önbellek, komut commit edildikten sonra temizlenir (`IAfterCommit`). Daha önce temizlenirse eşzamanlı bir istek eski veriyi yeniden önbelleğe alabilir.

**Yetki kataloğu ve sistem rolleri:**
- Modüllerin yetki tanımları her migration'dan sonra `platform.permissions` tablosuna senkronlanır (`ITenantDataSeeder`). Koddan kalkan yetki katalogdan ve tüm rollerden düşer.
- Sistem rolleri (yetki-ve-onay §1.4) seed edilir. Varsayılan yetkileri desenle tanımlanır (`platform.*`, `!hr.employees.sensitive.*`). İlk kez görülen bir yetki, deseni uyan sistem rollerine otomatik eklenir; kullanıcının sistem rollerinde yaptığı düzenlemeler korunur.
- Yetki tanımları ve etiketleri modülün Application/Infrastructure katmanında kaydedilir. Migrator Api modüllerini yüklemediği için seed bunlara ihtiyaç duyar.

**Yönetim kuralları:**
- Kullanıcı ve rol yönetimi kiracı düzeyindedir. `platform.users.*` ve `platform.roles.*` yalnızca Tenant kapsamıyla geçerlidir; şirket kapsamlı yönetici 403 alır.
- Sistemi yöneticisiz bırakacak işlem 409 ile reddedilir: Tenant kapsamlı son Sistem Yöneticisi atamasını kaldırmak, bitirmek ya da sahibini pasifleştirmek. Kullanıcı kendini pasifleştiremez.
- Kullanıcı oluşturma önce Keycloak'ta kimliği açar (`tenant` özniteliğiyle), sonra Platform kaydını yazar. Davet e-postası commit'ten sonra gönderilir. Pasifleştirme Keycloak'ta da hesabı kapatır.

**Keycloak yönetim API'si:**
- Realm'deki `merp-api` istemcisinin servis hesabı kullanılır (`manage-users`). İstemci `HttpClient` ile yazıldı; D19'daki Keycloak.AuthServices.Sdk eklenmedi, çünkü dört çağrı için paket gerekmedi.
- Ayarlar `Keycloak:Admin` bölümündedir: Url, Realm, ClientId, ClientSecret, WebUrl. Sır user-secrets veya ortam değişkeniyle verilir; yoksa kullanıcı yönetimi 503 döner.

**İlk yönetici:**
- Development: `Platform:Bootstrap:Users` realm'deki test kullanıcılarına ilk girişte rol verir (`admin` → Sistem Yöneticisi). P1'deki `Development:Permissions` kaldırıldı.
- Üretim: `Merp.Migrator create-admin --tenant … --username … --email … --first-name … --last-name …` Keycloak kullanıcısını ve Tenant kapsamlı Sistem Yöneticisi atamasını oluşturur, davet e-postasını gönderir.

## D27 — Audit okuma

**Durum:** Kabul edildi. Kapsam kuralını kullanıcı P3 başında seçti.

**Okuma yolu:**
- `IAuditReader` (Application.Abstractions) bir modülün `audit_entries` tablosunu okur. `AddModuleDbContext` bunu her modül için kendiliğinden kaydeder. Platform, modüllere referans vermeden hepsinin kayıtlarını `IEnumerable<IAuditReader>` ile okur (D13).
- Modüllerin kayıtları bellekte birleştirilip en yeniden eskiye sıralanır. Bu yüzden sayfalama en fazla 10 000 kayıt derine iner. Daha eskisi için tarih aralığı veya kayıt filtresi gerekir (`platform.audit.errors.too_deep`).
- Her modülde `audit_entries.occurred_at` üzerine indeks eklendi.

**Yetki ve kapsam:**
- `platform.audit.read` yalnızca Tenant kapsamıyla geçerlidir. Audit kayıtları şirket veya şube taşımaz, bu yüzden şirket kapsamlı kullanıcı 403 alır (`platform.audit.errors.tenant_scope_required`).
- Kayıt geçmişi çekmecesi de aynı yetkiye bağlıdır. Geçmişi kaydın kendi kapsamına göre gösterme sonraya bırakıldı; gerekirse her modül bir entity → kapsam çözümleyicisi ekler.

**Hassas alan okumaları:**
- `ISensitiveReadLog`, `[Sensitive]` alanların değeri maskesiz gösterildiğinde alan başına bir `Read` kaydı yazar. Kayıt, entity'yi eşleyen modülün tablosuna gider.
- Kayıt, isteğin unit of work'ü dışında, ayrı bir DbContext ile hemen yazılır. Veri görüldüyse istek sonradan başarısız olsa da kayıt kalır.
- Hassas alanların değerleri audit'e hiç yazılmaz; değişiklikte yalnızca değiştiği görünür (`***`).

**Etiketler:** Entity ve alan adları `{modül}.entities.{Tür}` ve `{modül}.entities.{Tür}.{Alan}` anahtarlarıyla yerelleştirilir (ör. `platform.entities.Company.Address.City`). Metni olmayan alan için teknik ad gösterilir.

## D28 — Çoklu dilin uygulanması

**Durum:** Kabul edildi (P4 incelemesinden sonra kullanıcı onayı). PLAN P4 ile uygulandı. D11'i ayrıntılandırır.

**Diller ve çeviriler kiracıya aittir:**
- `platform.cultures` (kod, ad, aktif, varsayılan, yedek dil) ve `platform.localization_texts` (dil, anahtar, metin) tabloları kiracı veritabanındadır.
- Çeviriler `Culture` aggregate'inin parçasıdır. Excel içe aktarma tek aggregate'i değiştirir (kural 7).
- Seed yalnızca `tr-TR` ekler (aktif, varsayılan). Varsayılan dil kapatılamaz, silinemez. Varsayılanı değiştirmek sonraki bir göreve bırakıldı.
- `Localization:SupportedCultures` ayarı kaldırıldı.

**Metin arama sırası:** dil → yedek dilleri → kiracının varsayılan dili → `tr-TR`.
- Her dilde önce kiracının çevirisine, sonra modüllerin gömülü JSON metnine bakılır. Yedek dil zincirinde döngü kurulamaz.
- Kiracının dilleri ve çevirileri tek bir `LocalizationSnapshot` olarak HybridCache'te tutulur (`{kiracı}:localization` etiketi). Değişiklikten sonra önbellek commit'ten sonra temizlenir.

**Kültür seçimi:** `RequestContextMiddleware`, kiracı belli olunca dil listesini yükler ve kültürü şu sırayla seçer:
1. Kullanıcının tercihi (aktif bir dilse).
2. `Accept-Language` içinde aktif dillerin en iyi eşleşmesi (`en` → `en-US`).
3. Kiracının varsayılan dili.

Seçilen kültür `Content-Language` başlığında döner.

**Yerelleştirici:** `ITextLocalizer` artık istek başına (scoped) çalışır ve yukarıdaki zinciri izler. ASP.NET `IStringLocalizer` uygulanmadı; hiçbir bileşen ona ihtiyaç duymuyor.

**Web metin paketi (D20):**
- Token olmadan (giriş ekranı) yalnızca gömülü metinleri verir. Geçerli her dil kodu kabul edilir ve Türkçe'ye düşer; önbelleğe alınabilir (`public`).
- Token ile kiracının çevirilerini de içerir ve yalnızca aktif dilleri kabul eder (`private, no-cache`).
- Web, oturum açıldıktan sonra paketi yeniden yükler. Üst bardaki dil seçici tercihi sunucuya da kaydeder.

**Yönetim:**
- `platform.localization.read` ve `platform.localization.manage` yalnızca Tenant kapsamıyla geçerlidir (D26 gibi).
- Excel biçimi: A sütunu anahtar, B sütunu varsayılan dildeki metin, C sütunu çeviri. C sütununun başlığı dil kodudur; başka dilin dosyası reddedilir. Boş hücreler atlanır, katalogda olmayan anahtarlar raporlanır.
- Excel için D19'daki ClosedXML (MIT) eklendi. Alt bağımlılıkları MIT ve Apache-2.0 (SixLabors.Fonts 1.0.0).

**Ana veri metinleri (`LocalizedString`):** Girişte varsayılan dilde metin zorunludur ve yalnızca aktif diller kullanılabilir (`MustCoverDefaultCulture`; ilk kullanan: rol adları).

## D29 — Belge numaralandırma

**Durum:** Kabul edildi (P5 incelemesinden sonra kullanıcı onayı). PLAN P5 ile uygulandı.

**Sorun:** Numara, çağıran komutun transaction'ında satır kilidiyle üretilmeli (PLAN P5). Komut başka modülde olabilir (HR, Recruitment). Kural 2 başka şemaya yazmayı, kural 3 modüller arası senkron yazmayı yasaklar.

**Karar — desen Platform'da, sayaç modülde:**
- **Desen ve sıfırlama:** `platform.number_sequences` tablosunda. Belge türü başına bir varsayılan (şirketsiz) ve isteğe bağlı şirkete özel desenler. Modüller belge türlerini `INumberSequenceDefinitionProvider` ile bildirir (izin kataloğu gibi). Platform eksik varsayılanları seed eder, düzenlenmiş desenlere dokunmaz.
- **Sayaç:** Her modül şemasındaki `number_counters` tablosu (`audit_entries` gibi `ModuleDbContext`'ten gelir). Belge türünün ilk parçası modüldür (`recruitment.offer` → `recruitment` şeması).
- **Üretim:** `INumberGenerator.NextAsync(companyId, documentType, date)`. Deseni Platform'dan okur; `INumberSequenceSettingsSource` HybridCache'te tutulur ve değişiklikten sonra temizlenir. Ardından modülün açık transaction'ında tek bir `INSERT … ON CONFLICT DO UPDATE SET value = value + 1 RETURNING value` çalıştırır.
  - Satır kilidi commit'e kadar sürer: eşzamanlı komutlar sırayla numara alır, aynı numara iki kez verilmez.
  - Geri alınan komut numarayı geri bırakır; boşluk oluşmaz.
  - Transaction dışında çağrılırsa hata verir.
- **Sayaç kapsamı:** Belge türü × şirket × dönem (`-`, `2026` veya `2026-10`). Her şirket kendi sayacıyla sayar. Belge tarihi dönemi belirler; eski yıla ait geç bir belge o yılın sayacından devam eder.

**Desen kuralları:**
- `{yyyy}`, `{yy}`, `{MM}` ve tam olarak bir `{seq}` veya `{seq:N}` (N 1–12) kullanılabilir.
- Yıllık sıfırlamada yıl, aylık sıfırlamada yıl ve ay desende olmalıdır; yoksa numaralar tekrar eder.
- Desen değişince sayaç kaldığı yerden devam eder. Yılı desenden çıkarmak eski numaraları tekrarlayabilir; arayüz bunu uyarır.

**Yönetim:** `platform.numbering.read/manage` yalnızca Tenant kapsamıyla geçerlidir (D26 gibi). Varsayılan desen silinemez; şirkete özel desen silinince varsayılan devreye girer.

**Seed:** Şimdilik `recruitment.requisition` (`PT-{yyyy}-{seq:5}`) ve `recruitment.offer` (`TKL-{yyyy}-{seq:5}`), yıllık. `hr.employee` ve `hr.leave-request` HR modülü (H1/H2) eklenince onun tanımlarıyla gelir.

## D30 — Özel alanların uygulanması

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-11). PLAN P6 ile uygulandı. D12'yi ayrıntılandırır.

**Tanımlar Platform'da, değerler kaydın kendisinde:**
- Tanımlar `platform.custom_field_definitions` tablosundadır: kayıt türü, kod, veri tipi, şirket, çok dilli ad, zorunluluk, seçenekler, sınırlar, grup, sıra, aktiflik.
- Modüller özel alan alabilen kayıt türlerini `ICustomFieldEntityProvider` ile bildirir (`platform.company`). Türün ilk parçası modüldür.
- Değerler kaydın `custom_fields` jsonb kolonunda, alan kodu → değer olarak tutulur (D12).

**Değişmezler:** Kod, veri tipi ve şirket kayıttan sonra değişmez, çünkü kayıtlardaki değerler bunlara bağlıdır. Kod, kayıt türü başına benzersizdir (şirketten bağımsız). Tanım silinmez, pasifleştirilir: pasif alan formda görünmez, kayıtlardaki değeri korunur.

**Doğrulama:**
- Kaydın sahibi olan modül, komutunda `ICustomFieldValidator` çağırır. Platform bunu önbelleğe alınmış tanımlarla uygular (HybridCache, değişince temizlenir).
- Yalnızca aktif ve o şirkete uyan alanlar kabul edilir; bilinmeyen kod hatadır. Hatalar `customFields.{kod}` alanına yazılır.
- Pasif alanların mevcut değerleri korunur.
- Değerler normalleştirilir: kırpılmış metin, sayı, `yyyy-MM-dd`, mantıksal değer, seçenek kodu veya kodları, GUID.
- Kullanıcı tipindeki alanın kullanıcısı Platform'da aranır. Personel tipindeki alan HR modülü gelene kadar yalnızca biçimce denetlenir.

**Liste filtresi:**
- `customFields` sorgu parametresi bir JSON nesnesidir; kayıt bu değerleri içermelidir (jsonb `@>`). Bu karşılaştırmayı `custom_fields` üzerindeki GIN (`jsonb_path_ops`) indeksi karşılar.
- Uygulama katmanı veritabanı sağlayıcısına bağlanmasın diye sorgularda yer tutucu bir metot (`CustomFieldFiltering.Contain`) kullanılır. Infrastructure bunu sorgu derlenmeden önce Npgsql'in `JsonContains` çağrısına çevirir (`IQueryExpressionInterceptor`).

**Form endpoint'i:** `/api/platform/custom-fields/form` oturum ister, yetki istemez (D22). Hassas değildir ve özel alanı olan her formda gerekir.

**Yönetim:** `platform.custom_fields.read/manage` yalnızca Tenant kapsamıyla geçerlidir.

**Web:**
- `DynamicFields` tüm veri tiplerini gösterir (`shared/customFields`).
- Listelerde özel alanlar isteğe bağlı kolon ve tek alanlık filtre olarak kullanılır.
- İlk kullanan: Şirketler.

## D31 — Dosyaların uygulanması

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-11). PLAN P7 ile uygulandı. D16'yı ayrıntılandırır.

**Sorun:** D16 metadata'yı Platform'daki `stored_files` tablosunda tutar. Ama dosyayı yükleyen komut başka modülde olabilir (HR, Recruitment). Kural 2 başka şemaya yazmayı, kural 3 modüller arası senkron yazmayı yasaklar (D29'daki numaralandırma sorunuyla aynı).

**Karar — metadata modülde, depolama ortak:**
- `stored_files` her modülün şemasındadır (`ModuleDbContext`, `audit_entries` ve `number_counters` gibi). Varlık `Merp.Application.Persistence`'tadır, komutlar onu kendi DbContext'leriyle ekler. Böylece metadata iş verisiyle aynı transaction'da kaydedilir veya geri alınır.
- Kayıt türü (sahip) `{modül}.{tür}` biçimindedir (`platform.company`). İlk parça metadata'nın hangi şemada olduğunu belirler.
- Ekler için ayrı `*_attachments` tabloları yoktur: dosya, sahibi ve amacıyla (`attachment`, `logo`) `stored_files`'ta durur. PLAN P7'deki "her modül kendi ekler tablosunu tutar" maddesi bu tabloyla karşılanır.
- Bir kaydın tek resmi (şirket logosu) kaydın alanı değil, amacı `logo` olan dosyasıdır. Böylece logo değişince kaydın sürümü değişmez; formu açık olan kullanıcı kaydederken 409 almaz.

**Depolama (`IFileStorage`):**
- `Files:Storage`: `FileSystem` (varsayılan, `Files:FileSystem:RootPath`) veya `S3` (`Files:S3:*`, AWSSDK.S3; kova önceden oluşturulur, kimlik bilgileri yalnızca yapılandırmadan okunur).
- Anahtar: `{tenantId}/{modül}/{yyyy}/{MM}/{dosyaId}`. Kullanıcının verdiği ad yolda kullanılmaz.

**Yükleme (`IFileStore.SaveAsync`):**
- Boyut sınırı ve uzantı beyaz listesi: ekler için `Files:MaxBytes` (varsayılan 20 MB) ve `Files:AllowedExtensions` (pdf, docx, xlsx, pptx, png, jpg, jpeg, webp, txt, csv). Logo için PNG, JPEG veya WebP, en fazla 1 MB. SVG kabul edilmez, çünkü betik taşıyabilir.
- İçerik denetimi: baştaki baytlar (magic bytes) uzantıyla uyuşmalıdır. Office belgelerinde paketin ana parçası aranır, VBA projesi olan (makrolu) paket reddedilir. Metin dosyası geçerli UTF-8 olmalı ve kontrol karakteri içermemelidir. Yalnızca içeriği denetlenebilen uzantılara izin verilebilir; yapılandırma bu kurala uymazsa uygulama başlamaz.
- SHA-256 saklanır. İndirmede gönderilen içerik türü sunucunun belirlediği türdür, istemcinin bildirdiği değil.
- Hatalar `file` alanına yazılan doğrulama hatalarıdır (`common.files.errors.*`).

**Erişim (`IFileAccessPolicy`):**
- Dosyaların kendi yetkileri yoktur. Kaydın sahibi olan modül, sahip türü için politika kaydeder ve kaydın kendi yetkisi ile veri kapsamını uygular (şirket: `platform.companies.read` okur, `platform.companies.manage` yükler ve siler).
- Kaydı göremeyen kullanıcı için dosya yoktur (404). Salt okuyabilen kullanıcı yükleme ve silmede 403 alır. Uç noktalar bu yüzden yalnızca oturum ister (D22).
- İndirme: `X-Content-Type-Options: nosniff`, `Content-Security-Policy: sandbox`, `Cache-Control: private, no-store`. Resimler sayfada gösterilir, diğer dosyalar indirilir.

**Silme ve temizlik:**
- Silme yumuşak silmedir: satır audit için kalır, içerik commit'ten hemen sonra depodan silinir.
- Gece işi `files.sweep` (02:00 UTC), her kiracı ve modül için iki iş yapar:
  - Silinmiş dosyaların depoda kalan içeriğini temizler ve satıra `purged_at` yazar.
  - Geri alınan komutlardan kalan, metadata'sı olmayan içeriği `Files:OrphanGracePeriod` (1 gün) sonra siler.
- Tekrarlanan işler `RecurringJobDefinition` ile kaydedilir. İş sunucusu açık olan host (`Jobs:EnableServer`) bunları açılışta Hangfire'a yazar.

**Web:** `<Attachments ownerType ownerId />` (`shared/files`) sürükle-bırak, liste, indirme ve silme sunar. Liste yanıtı kabul edilen uzantıları ve boyut sınırını da döndürür. Başka bir modül ek kullanmaya başladığında, uç noktalarını bu bileşenin modül listesine ekler. İlk kullanan: şirket formu (logo ve ekler).

## D32 — Bildirimlerin uygulanması

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-11). PLAN P8a ile uygulandı.

**Akış:**
- Bildirim isteyen modül `NotificationRequested` olayını (Platform.Contracts) kendi outbox'ıyla yayınlar. Olay türü, alıcı kullanıcılar, argümanlar ve uygulama içi bir bağlantı taşır; `CompanyId` e-posta şablonunu seçer.
- Platform olayı inbox'tan işler. Her etkin alıcı için tercihler izin veriyorsa zile bir bildirim ve kuyruğa bir e-posta yazar.
- Commit'ten sonra kullanıcının açık sekmelerine SignalR ile `notificationReceived` gönderilir ve e-postalar hemen gönderilir.
- Inbox işleyicileri de komutlar gibi `IAfterCommit` kuyruğunu kullanır: işler commit'ten sonra ve transaction dışında çalışır; hataları yalnızca loglanır.

**Bildirim türleri:**
- Modüller türlerini `INotificationTypeProvider` ile bildirir (yetkiler gibi): kod `{modül}.{ad}`, varsayılan kanallar ve kullanıcının kapatıp kapatamayacağı (`Mandatory`).
- Metinler modülün yerelleştirme anahtarlarıdır: `{modül}.notifications.{ad}.title`, `.body` (isteğe bağlı) ve tercih listesindeki ad `.label`. Bildirim metin değil tür ve argüman saklar; başlık ve gövde okunurken okuyanın dilinde üretilir.

**Kayıtlar aggregate değildir:** `notifications` ve `email_messages`, outbox ve audit kayıtları gibi teslim kayıtlarıdır. Böylece bir olay birçok alıcıya tek transaction'da yazılabilir; kural 7 aggregate'leri sayar ve bunları saymaz.

**Bağlantılar:** Yalnızca uygulama içi yollar kabul edilir (`/` ile başlar, `//` ve `\` içermez). Bildirim kullanıcıyı başka bir siteye götüremez. E-postada bağlantının önüne `Notifications:WebBaseUrl` eklenir.

**Tercihler:**
- Kullanıcı her tür için uygulama içi ve e-posta kanallarını açar veya kapatır.
- Yalnızca varsayılandan farklı seçimler saklanır (`users.notification_preferences`, jsonb). Böylece varsayılan sonradan değişirse seçim yapmamış kullanıcılara uygulanır.
- `Mandatory` türlerde seçim dikkate alınmaz.

**E-posta:**
- MailKit ile SMTP üzerinden gönderilir (`Email` bölümü; geliştirmede Mailpit).
- Kuyruk `email_messages` tablosudur. Gönderilemeyen ileti 1, 2, 4… dakika (en fazla 1 saat) sonra yeniden denenir; 8 denemeden sonra `Failed` olur. Yeniden denemeyi her dakika çalışan `notifications.email` işi yapar.
- Konu tek satırdır; şablon başlık ekleyemez.

**Şablonlar:**
- Liquid (Fluid) kullanılır. Modelde yalnızca değerler, sözlükler ve listeler vardır; şablondan hiçbir .NET üyesine erişilemez. HTML'de değerler kodlanır, Türkçe harfler korunur.
- Şablon tür veya tüm türler × şirket veya tüm şirketler × kültür için tanımlanır (`notification_templates`, null değerler dahil tekil).
- Seçim sırası: önce alıcının kültürü (okuyabilmesi için), sonra türün kendi şablonu, sonra şirketinki. Hiçbiri yoksa yerleşik düzen kullanılır.
- Kaydedilen şablon ayrıştırılır; hata satır numarasıyla döner. Çalışma anında yine de hata çıkarsa ileti yerleşik düzenle gider.
- Yönetim API'si `platform.notification_templates.read/manage` yetkisi ister ve yalnızca Tenant kapsamıyla geçerlidir. Düzenleme ekranı P12'deki şablon editörüyle gelir.

**Anlık kanal:**
- SignalR kullanıcıyı `{tenant}:{sub}` ile tanır (`IUserIdProvider`).
- Tek API örneği varsayılır. Birden çok örnekte Valkey backplane'i (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`, MIT) eklenmelidir (X5).

**Web (P8b):** Sunucu yalnızca "yeni bildirim var" der (`notificationReceived`: kimlik ve tür); istemci okunmamış sayısını ve listeleri yeniden okur. Metinler böylece her zaman okuyanın dilinde, tek yerden (API) gelir. Bağlantı kurulamazsa zil sayfa odaklanınca güncellenir.

**Uç noktalar:** Kullanıcının kendi bildirimleri ve tercihleri yalnızca oturum ister (D22). `POST /api/platform/notifications/test` kullanıcıya kendi kanallarından bir test bildirimi gönderir (`platform.test`).

## D33 — Çalışma takvimi ve resmi tatiller

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-11). PLAN P9a ile uygulandı.

**Resmi tatiller:**
- `HolidayCalendar` (ör. `TR`) tatilleri tutar: tarih, çok dilli ad, tam veya yarım gün, yarım günün başlangıç saati (varsayılan 13:00) ve hareketli (dini) mi.
- Bir tarihte bir tatil olur. İki bayram aynı güne denk gelirse tek kayıtta adlandırılır (2027'de 19 Mayıs ile Kurban Bayramı).
- Sabit tatiller "önceki yıldan kopyala" ile yeni yıla taşınır (29 Şubat atlanır). Dini bayramlar her yıl Diyanet takviminden elle girilir.
- Seed: `izin-kurallari.md` §7'deki 2026 ve 2027 tatilleri. Kiracının takvimi varsa seed dokunmaz.

**Çalışma takvimi (`WorkSchedule`):**
- Haftanın her günü (pazartesiden pazara) için çalışma günü mü, başlangıç, bitiş ve isteğe bağlı mola (başlangıç ve bitiş saati) tutulur. Mola mesai içinde kalır. Takvim bir resmi tatil takvimine bağlıdır.
- Takvim bir şirketin ya da (şirketi yoksa) bütün kiracınındır. Her şirketin ve kiracının bir varsayılanı olur. Yeni varsayılan, eskisini domain olayıyla (`WorkScheduleMadeDefault`) bırakır (kural 7). Tekil indeks kullanılmaz, çünkü iki güncellemenin sırası belli değildir. Varsayılan takvim pasifleştirilemez.
- Şube kendi takvimini seçebilir (`branches.work_schedule_id`). Seçilen takvim ya kiracının ortak takvimi ya da şubenin şirketinin takvimi olmalıdır.
- Seed: kiracı varsayılanı "Standart (Pzt–Cum)" 08:30–18:00, mola 12:30–13:30. Varsayılan olmayan "Haftada 6 gün (Pzt–Cmt)": cumartesi 08:30–13:30.

**`IWorkingCalendar` (Platform.Contracts, salt okunur):**
- `ResolveScheduleIdAsync(şirket, şube)` önce şubenin takvimini, yoksa şirketin varsayılanını, yoksa kiracının varsayılanını döndürür. İstihdam ezmesi HR'da (L2) bunun önüne eklenir.
- `GetDayAsync` ve `GetDaysAsync` (en fazla 400 gün) her gün için şunları döndürür: çalışma günü mü, tatil oranı (0, 0,5 veya 1), tatil adı ve yarım günün başlangıcı, mesai ve çalışma saati.
- `LeaveDayValue` iş günü tabanlı izin hesabının günlük değeridir (`izin-kurallari.md` §4): çalışma günüyse 1 eksi tatil oranı, değilse 0. Yarım gün tatilde çalışma saati tatil başlayınca biter; mola o saate kadar olan kısmıyla düşülür.
- Takvimler, yıllık tatiller ve çözümler HybridCache'te bir saat tutulur. Takvim, tatil veya şube değişince kiracının tüm takvim önbelleği temizlenir.

**Hatırlatma:** `calendars.holiday-reminder` işi her 1 Ekim 06:00 UTC'de (09:00 TSİ) çalışır. Ertesi yılın dini bayramları girilmemiş her takvim için, `platform.calendars.manage` yetkisini bugün Tenant kapsamıyla taşıyan kullanıcılara `platform.holidays_missing` bildirimi gönderir (D32).

**Yetkiler:** `platform.calendars.read/manage`. Değişiklikler yalnızca Tenant kapsamıyla yapılır, çünkü takvimler bütün şirketlerce paylaşılır.

## D34 — Onay motorunun uygulanması

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-12). PLAN P10a ile uygulandı; P10b ve P10c buna göre ilerler.

`docs/domain/yetki-ve-onay.md` §2 birebir uygulanır. Belgede açık bırakılan noktalar şöyle çözüldü:

**Talep ve tur:**
- Bir belgenin tek onay talebi olur (`approval_requests` üzerinde `(document_type, document_id)` tekil). İade edilen belge için `ApprovalRequested` yeniden yayınlanınca aynı talepte yeni bir tur başlar: tur numarası artar, akış 1. adımdan başlar, olgular yenilenir, adımlar ilk gönderimdeki snapshot'tan kullanılır. Eski kararlar geçmişte tur numarasıyla kalır.
- Akışta veya karardayken gelen tekrar `ApprovalRequested` görmezden gelinir (loglanır).
- İptal, talebi henüz yoksa inbox'ta yeniden denenir. Böylece talebinin önüne geçen iptal de sonunda uygulanır.
- `SkipIfAlreadyApproved` aynı turdaki onayları sayar. `Any` modunda onaylayıcılardan biri önceki adımda onay verdiyse adım atlanır. `All` modunda yalnızca onay vermemiş olanlara sorulur.
- Talep eden onaylayıcı listesinden çıkarılır. Geriye kimse kalmazsa adım "talep eden" gerekçesiyle atlanır. Çözücü kimseyi bulamazsa önce fallback denenir, sonra talep `NeedsAttention` olur.
- Tanım bulunmayan ve otomatik onaylanmayan talepte adım yoktur. Yönetici onaylayıcı atayamaz, talebi zorla onaylayabilir.

**Eşzamanlılık:** Her değişiklik talebin `last_activity_at` alanını günceller. Böylece aynı talebe aynı anda gelen iki karardan ikincisi talebin sürümünde (`xmin`) çakışır ve 409 alır, yalnızca görev değişse bile. Geçmiş satırlarının kendi kimliği vardır, bu yüzden çakışma birincil anahtarda değil sürümde yakalanır.

**Süre ve eskalasyon:**
- `DueHours` takvim saatidir; çalışma takvimi (D33) hesaba katılmaz. İş günü bazlı süre gerekirse ayrı bir seçenek olarak eklenir.
- `approvals.escalation` işi 15 dakikada bir çalışır. Süresi geçmiş ve henüz işlenmemiş görevi olan talepleri bulur ve her talep için ayrı komut gönderir (kural 7). Bir talepteki hata diğerlerini durdurmaz.
- `notify` (ve eskalasyonu olmayan süreli adım): hatırlatma bir kez gönderilir.
- `escalate-to-manager`: görev, onaylayıcının yöneticisine aynı karar süresiyle aktarılır. Yönetici HR'ın uygulayacağı `IApproverManagerLookup` ile bulunur. HR yoksa, yönetici yoksa, yönetici pasifse ya da zaten onaylayıcıysa hatırlatma gönderilir. Aktarılan görev de süresi geçince yeniden eskale olur.
- `auto-approve` / `auto-reject`: sistem adımı onaylar ya da talebi reddeder. Geçmişte kullanıcı olmadan "overdue" gerekçesiyle görünür.

**Vekalet:** Kullanıcı kendi vekaletini verir ve sonlandırır. Başka kullanıcının vekaleti 404 döner. Vekil, asılın bugün kapsanan belge türlerindeki görevlerine karar verebilir; kendi talebine karar veremez. Görev atandığında asıl ve vekilleri bildirim alır.

**Bildirimler (D32):** `platform.approval_assigned` (onaylayıcı ve vekilleri), `platform.approval_needs_attention` (`platform.approvals.admin` yetkisini Tenant kapsamıyla taşıyanlar), `platform.approval_overdue` (hatırlatma). Bağlantı: `/approvals/requests/{id}`.

**Yetkiler:**
- `platform.approvals.definitions.read/manage`: akış tanımları.
- `platform.approvals.admin`: yeniden atama, zorla onay, dikkat bekleyen talebe onaylayıcı atama.
- Bu işlemler yalnızca Tenant kapsamıyla yapılır.
- Görevlerim, karar ve vekaletlerim oturum açmış her kullanıcıya açıktır. P10b'de D22 listesine eklenir.

**Sözleşmeler:**
- Belge türü bildirimi (`IApprovalDocumentTypeProvider`, `ApprovalDocumentType`, `ApprovalFact`) `Merp.Application.Abstractions.Approvals` içindedir, bildirim türleri gibi. Contracts'ta yalnızca olaylar, sorgu arayüzleri ve çözücü sözleşmesi (`IApproverResolver`, `IApproverManagerLookup`) bulunur.
- Olgu türleri: `Text`, `Number`, `Boolean`, `Date` (`yyyy-MM-dd`), `Identifier`, `TextList`.
- `role` çözücüsü, rolü bugün geçerli bir atamayla taşıyan aktif kullanıcıları döndürür. Kapsam şöyle değerlendirilir: Tenant her belgeyi kapsar. Company belgenin şirketini kapsar. Branch ve OrgUnit, olgulardaki `branchId` / `orgUnitId` ile eşleşirse kapsar. Alt birimler izlenmez, çünkü birim ağacı HR'dadır.

**P10b ile eklenenler:**
- **Varsayılan akışlar (§2.8):** Belge türü, varsayılan akışını `ApprovalDocumentType.DefaultFlow` ile bildirir. `ApprovalDefinitionSeeder` her kiracıda, o türde hiç tanım yoksa (aktif ya da pasif) bunu genel tanım olarak ekler. Kiracının sonraki değişikliklerine dokunmaz. `role` adımı sistem rolünü koduyla anar (`roleCode`, ör. `hr-specialist`); seeder kiracının rol kimliğini yazar. Ad ve adım adları tr-TR metinlerinden alınır. §2.8'deki tanımlar, belge türleri HR ve Recruitment'ta tanımlanınca o görevlerle gelir (PLAN L ve R görevleri).
- **Zaman çizelgesi erişimi:** Talep eden, onaylayıcılar (geçmiş ya da bekleyen), açık görevin bugünkü vekili ve `platform.approvals.admin` (Tenant) talebi görür. Bunlara ek olarak belgenin modülü, `IApprovalDocumentAccessPolicy` (`Merp.Application.Abstractions.Approvals`) ile belgeyi kendi yetkisi ve veri kapsamıyla görenlere izin verir. Bu, dosyalardaki `IFileAccessPolicy` (D31) ile aynı kalıptır. Diğerleri 404 alır.
- **`IApprovalAccess.HasTaskAsync`** (Contracts): yetki modelindeki görev bazlı erişim (§1 madde 5) içindir. Görevin onaylayıcısı ya da kararı veren kişi (geçmiş görev dahil) ve açık görevin bugünkü vekili belgeyi görebilir.
- **`IApprovalPreview`** (Contracts) ve `POST /approvals/preview`: Seçilecek akışı ve adımların onaylayıcılarını bugünkü atamalarla döndürür, hiçbir şey kaydetmez. Önizlemede `Any` modunda kimin onaylayacağı bilinmez. Bu yüzden "önceden onay verdi" kuralı yalnızca tek onaylayıcılı adımlar ve `All` adımları için uygulanır.
- **Görevlerim:** Kullanıcının ve bugün vekili olduğu kişilerin açık görevleri listelenir, vekilin kendi talepleri hariç tutulur. Kişi başına açık görev az olduğu için vekalet kapsamı bellekte süzülür. Sıralama: süresi olanlar süreye göre, sonra en eskiler.
- **Kullanıcı seçimi:** `GET /approvals/users?search=` vekil ve onaylayıcı seçiminde kullanılır. Oturum açmış herkese açıktır ve aktif kullanıcıların yalnızca kimliğini ve adını döndürür (en fazla 20).

**P10c ile eklenenler:**
- **Deneme talebi:** `Platform:Approvals:EnableTestDocuments` açıkken (geliştirmede açık) `platform.approval-test` belge türü kaydedilir. Bu türün alanları: tutar, kategori, belgedeki onaylayıcı. Akış yöneticisi `POST /approvals/test-documents` (`platform.approvals.definitions.manage`) ile, akış ekranından deneme belgesi gönderir. Belge bir modülün belgesi gibi outbox üzerinden `ApprovalRequested` ile gider; PLAN P10'un uçtan uca kabul kriteri bununla sağlanır. Kapalıyken endpoint 404 döner.
- **Menü:** "Onaylar" grubu (Onay Kutum, Vekaletlerim) her oturum açmış kullanıcıya görünür. Onay akışları ve onay talepleri Sistem menüsündedir ve yetkiye bağlıdır.
- **Canlı güncelleme:** Bir `platform.approval_*` bildirimi geldiğinde Onay Kutum ve rozet yeniden yüklenir (D32 SignalR).
- **Sessiz sorgular:** Kendi boş/yok durumunu gösteren sorgular (`meta.silent`), örneğin hiç gönderilmemiş belgenin zaman çizelgesi, genel hata bildirimini göstermez.

## D35 — Ayarlar, içe ve dışa aktarma altyapısı

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-13). PLAN P11 ile uygulanır.

**Bölünme:** P11 üç PR'a ayrılır: P11a ayarlar, P11b içe aktarma, P11c dışa aktarma.

**Ayarlar (P11a):**
- **Tanımlar:** Modüller ayarlarını `ISettingDefinitionProvider` ile kodda tanımlar (`Merp.Application.Abstractions.Settings`), yetkiler gibi. Bir tanım şunları içerir:
  - anahtar (`{modül}.{grup}.{ad}`)
  - tip: `Boolean`, `Number` (tam sayı seçeneği, en az ve en çok), `Text` (en fazla uzunluk), `Choice` (seçenekler ve etiketleri)
  - varsayılan değer
  - ad ve açıklama anahtarları
  - şirket bazında değiştirilebilir mi
- **Değerler:** Değerler Platform'da `settings_profiles` tablosunda tutulur: kiracı için bir profil, şirket başına bir profil, değerler jsonb. Bir profil bir aggregate'tir. Böylece ekrandan birden çok ayarı kaydeden komut tek aggregate değiştirir (kural 7), ve değişiklik audit'e girer.
- **Okuma:** Modüller ayarı `ISettings.GetAsync<T>(anahtar, şirket)` ile okur. Sıra şöyledir: şirketin değeri, yoksa kiracının değeri, yoksa varsayılan.
  - Platform tüm profilleri kiracı başına bir önbellek kaydında tutar. Kayıttan sonra önbellek temizlenir.
  - Platform yoksa varsayılanlar döner (D29 ve D30'daki gibi).
- **Doğrulama:** Kaydederken tip, seçenek, sınırlar ve şirket bazında değiştirilebilirlik kontrol edilir. Bilinmeyen anahtar reddedilir. Boş gönderilen değer kaldırılır, ayar üst seviyeden gelir.
- **Yetki ve ekran:**
  - `platform.settings.read/manage`, yalnızca Tenant kapsamıyla (numaralandırma ve takvimlerdeki gibi).
  - Ekran Sistem → Ayarlar. Ayarlar modüllere göre gruplanır.
  - Kiracı geneli ya da şirket seçilir. Şirket görünümünde sadece şirket bazında değiştirilebilen ayarlar ve devralınan değer gösterilir.
- **İlk kullanım:** A14 ile yapay zeka özellikleri ve aylık bütçe ayarlara taşınır. Yurt dışına aktarım dayanağı ve kanıtı hukuki kayıt olduğu için yapılandırmada kalır (D24).

**İçe aktarma (P11b):**
- **Tanımlar:** Modüller içe aktarmalarını `IImportDefinition` ile tanımlar (`Merp.Application.Abstractions.Imports`). Bir tanım şunları içerir:
  - anahtar (`{modül}.{kayıt}`), ad anahtarı ve gereken yetki
  - sütunlar: anahtar, başlık ve açıklama anahtarı, tip (`Text`, `Number`, `WholeNumber`, `Date`, `Boolean`, `Choice`), zorunluluk, en fazla uzunluk, seçenekler
  - satırların iş kurallarıyla doğrulanması (veri kapsamı, tekrar, referanslar) ve bir parti için yazma komutu
- **Şablon:** Başlıklar kullanıcının dilindedir, zorunlu sütunlar `*` ile işaretlenir. Açıklamalar başlık notu, seçenekler ve Evet/Hayır açılır liste olarak gelir.
- **Akış:**
  1. Kullanıcı dolu şablonu yükler. Dosya `IFileStore` ile saklanır (D31) ve bir `import_jobs` kaydı açılır.
  2. Dosya önce doğrulanır, hiçbir şey yazılmaz. Hücre hataları (tip, zorunluluk, uzunluk, seçenek) ve tanımın hataları satır ve sütunuyla, kullanıcının dilinde kaydedilir.
  3. Hata varsa kullanıcı hata raporunu indirir: yüklediği satırlar, bir "Hatalar" sütunu ve hatalı hücrelerde kırmızı dolgu ile not.
  4. Hata yoksa kullanıcı "İçe aktar" der. Satırlar 100'lük partiler hâlinde yazılır. Her parti kendi işlemindedir; hatalı parti tek başına geri alınır, diğerleri yazılır ("kısmen tamamlandı").
- **Kural 7 istisnası:** Parti komutu (`IImportBatchCommand`, işaret arayüzü `IBulkCommand`) bir işlemde birden çok aggregate oluşturabilir. Unit of work bu komutlarda tek aggregate denetimini (D21) atlar. Başka hiçbir komut bu arayüzü kullanamaz.
- **Arka plan:** `Platform:Imports:BackgroundThreshold` (varsayılan 1000) satırdan büyük dosyalar Hangfire ile arka planda doğrulanır ve yazılır. İş, kayıt işlendikten sonra kuyruğa girer (`IBackgroundTasks`, tenant bilgisini taşır). Arka planda kullanıcının veri kapsamı, işi başlatan kullanıcının kimliğiyle denetlenir. Küçük dosyalar istek bitince aynı süreçte işlenir. Ekran işin durumunu sorgulayarak izler.
- **Sınırlar:** En fazla 10.000 satır (`MaxRows`) ve 10 MB. Bir işte en fazla 1000 hata saklanır, ekranda ilk 200'ü gösterilir.
- **Audit:** İçe aktarma işi audit'e dahil edilmez; işin kendisi kayıttır. Oluşan kayıtlar (örn. şubeler) kendi audit kayıtlarını yazar.
- **İlk tanım:** Şubeler (`platform.branches`, `platform.branches.manage`). Şirket kodu, il adı ya da plaka kodu, ilçe adı ile eşlenir.
- **Hatasız satırlar ve güncelleme modu:** D40 hatalı dosyanın hatasız satırlarını aktarma seçeneğini ve TCKN gibi bir anahtara göre güncelleme modunu ekler.

**Dışa aktarma (P11c):**
- **Uç noktalar:** Her liste kendi yanında `GET …/{liste}/export` sunar (örn. `/api/platform/branches/export`). Liste ile aynı yetkiyi ve aynı filtre parametrelerini alır, ek olarak `columns` (virgülle ayrılmış sütun anahtarları) ve `sort`.
- **Satırlar liste sorgusundan okunur:** `IListExporter` liste sorgusunu sayfa sayfa (200'er) çalıştırır. Böylece veri kapsamı, filtreler, aktif şirket ve `[Sensitive]` maskelemesi ekrandakiyle aynıdır; dışa aktarma ayrı bir sorgu yazmaz.
- **Sütunlar:** Modül, listenin sütunlarını `ExportColumn<T>` ile tanımlar (ekrandaki sütun anahtarı, başlık anahtarı, değer). Ekranda görünen sütunlar ekrandaki sırayla aktarılır; bilinmeyen anahtarlar (örn. işlem sütunu) atlanır, hiç sütun verilmezse hepsi aktarılır. Özel alanlar `customFields.{kod}` sütunlarıyla aktarılır.
- **Hücreler:** Sayılar, tarihler ve anlar tipli yazılır (Excel'de sıralanıp toplanabilir). Anlar `Exports:TimeZoneId` (varsayılan Europe/Istanbul) saatine çevrilir. Enum ve durum değerleri kullanıcının dilindeki etiketlerle, evet/hayır metinle yazılır. Başlık satırında filtre vardır ve sabittir.
- **Boyut:** Dosya istek içinde üretilir. `Exports:MaxRows` (varsayılan 10.000) satırı aşan liste reddedilir (400, `filter` alanı), kullanıcı filtreyi daraltır.
- **Denetim (KVKK):** Her dışa aktarma, satırların varlığının modülündeki audit tablosuna `Export` işlemi olarak yazılır: kim, ne zaman, kaç satır, hangi sütunlar, sıralama ve filtreler. Kayıt isteğin işleminden bağımsız, hemen yazılır.
- **İlk listeler:** şirketler (özel alanlar dahil), şubeler, kullanıcılar.

## D36 — Belge şablonlarının ve PDF'in uygulanması

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-15). PLAN P12 ile uygulanır. D14'ü ayrıntılandırır.

**Bölünme:** P12 iki PR'a ayrılır: P12a backend, P12b web.

**Belge türleri:**
- Modüller belgelerini `IDocumentTypeProvider` ile kodda tanımlar (`Merp.Application.Abstractions.Documents`), bildirim türleri gibi. Bir tanım şunları içerir:
  - anahtar (`{modül}.{belge}`) ve ad anahtarı
  - yerleşik şablon (HTML ve CSS): kiracı hiç şablon tanımlamasa da belge üretilir
  - örnek model: editördeki önizleme ve doğrulama bununla yapılır
  - alan listesi: şablonda kullanılabilecek değişkenler ve açıklama anahtarları (editörde yardım olarak gösterilir)
- Platform ilk tür olarak antetli genel yazıyı (`platform.letter`) tanımlar. Kabul testi ve demo bununla yapılır. İzin formu, teklif mektubu gibi türler kendi görevleriyle (L6, R6) gelir.

**Şablonlar:**
- `DocumentTemplate` (Platform, `document_templates`) bir aggregate'tir ve audit'e girer. Alanları:
  - tür, şirket (null: tüm şirketler), kültür
  - gövde (Liquid HTML, en fazla 200.000 karakter) ve CSS (en fazla 50.000 karakter)
  - revizyon (içerik her değiştiğinde 1 artar, belgede gösterilebilir) ve aktiflik
- Tür × şirket × kültür tekildir (null değerler dahil), bildirim şablonlarındaki gibi (D32).
- Seçim sırası: önce istenen kültür, sonra şirketin şablonu, sonra tüm şirketlerinki. Pasif şablonlar atlanır. Hiçbiri yoksa türün yerleşik şablonu kullanılır.

**Render:**
- Fluid, D32'deki sandbox kurallarıyla: modelde yalnızca değerler, sözlükler ve listeler. Ek olarak sonsuz döngüye karşı adım sınırı konur.
- Şablon belgenin kültürüyle çalışır, böylece `date` filtresi ay adlarını o dilde yazar.
- Platform her modele `company` nesnesini ekler: unvan, kısa ad, vergi dairesi ve numarası, MERSİS, adres, telefon, e-posta, logo. Bunun yanında `document` (tür, revizyon, üretim tarihi) eklenir.
- Hazır parçalar: `{% render 'logo' %}` ve `{% render 'letterhead' %}` (logo, unvan, adres ve vergi bilgileri). Parçalar bellekte tutulur. Şablon dosya sistemine ya da başka bir kaynağa erişemez.
- Logo HTML'e `data:` URI olarak gömülür. Böylece PDF dışarıdan hiçbir kaynak indirmez.
- Kaydedilen şablon önce ayrıştırılır, sonra türün örnek modeliyle render edilir. Hata satır numarasıyla `body` alanında döner. CSS Liquid değildir, olduğu gibi sayfaya eklenir.
- Belge metinleri ("Sayı", "Konu" gibi etiketler) koda yazılmaz: şablon bunları belgenin dilinde `texts.*` olarak okur. Kaynakları türün `{modül}.documents.{ad}.texts.*` anahtarları ve Platform'un ortak `platform.documents.texts.*` anahtarlarıdır.

**PDF:**
- `IPdfConverter` (Abstractions) HTML'i PDF'e çevirir. Altyapıdaki uygulaması Gotenberg 8'in Chromium HTML dönüşümünü HTTP ile çağırır.
  - Ayarlar `Pdf:GotenbergUrl` (geliştirmede `http://localhost:3000`) ve `Pdf:TimeoutSeconds` (varsayılan 30).
  - Sayfa A4'tür ve arka planlar basılır.
- Gotenberg'e ulaşılamazsa iş hatası döner (`common.errors.pdf_unavailable`, 503). Sağlık kontrolüne Gotenberg eklenir; durumu "degraded" olur, uygulamayı durdurmaz.
- Dış ağ erişimi kapalı tutulur: Gotenberg `--chromium-allow-list=^(file:///tmp/|data:)` ile çalışır, sayfa kendi dosyaları ve `data:` URI'ları dışında hiçbir adrese istek atamaz. Üretim kurulumunda da aynı ayar kullanılmalıdır.

**Modüllerin kullanımı:**
- Modüller belgeyi `IDocumentGenerator.GetPdfAsync(tür, şirket, kültür, model)` (Platform.Contracts) ile ister.
- Bu çağrı salt okunurdur, bir şey yazmaz. Bu yüzden kural 3'teki senkron sorgu arayüzlerine uyar.
- Üretilen PDF'i saklamak gerekiyorsa modül bunu kendi `IFileStore` kaydıyla yapar (D31).

**Yetki ve uç noktalar:**
- `platform.document_templates.read/manage`, yalnızca Tenant kapsamıyla (bildirim şablonlarındaki gibi).
- `/api/platform/document-templates`:
  - türler: örnek model, alanlar ve yerleşik şablonla
  - şablon listesi, ayrıntısı, oluşturma, güncelleme ve silme. Silmek güvenlidir, çünkü yerleşik şablona düşülür.
  - `preview` (HTML) ve `preview/pdf`: kaydetmeden, gönderilen gövde ve CSS ile örnek modelden üretir.

**Web (P12b):**
- Sistem → Belge şablonları: liste ve editör sayfası.
  - Editör: gövde ve CSS sekmeleri, alan listesi, örnek veri.
  - Canlı önizleme: yazmayı bıraktıktan kısa süre sonra `preview` çağrılır, sonuç sandbox'lı bir `iframe`'de gösterilir. "PDF önizle" düğmesi PDF'i yeni sekmede açar.
  - Hata satırı editörde işaretlenir.
- Aynı editör bileşeniyle Sistem → Bildirim şablonları ekranı da gelir (D32'de P12'ye bırakılmıştı).
- Editör `@monaco-editor/react` (MIT) ve `monaco-editor` (MIT) ile kurulur.
  - Monaco CDN'den değil uygulamanın kendi paketinden yüklenir: kurum ağlarında dış CDN kapalı olabilir.
  - Yalnızca editör sayfası açılınca yüklenir.

**Docker olmayan geliştirme ortamı:** Gotenberg istemcisi sahte HTTP yanıtlarıyla birim testinden geçer. Gerçek PDF üretimi entegrasyon testinde Gotenberg container'ıyla (Testcontainers) doğrulanır ve yalnızca CI'da koşar. PDF'in metni PdfPig ile okunur: Türkçe harfler ve logo kontrol edilir.

## D37 — İK modülü ve organizasyon yapısı

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-16). PLAN H1 ile uygulanır.

**Bölünme:** H1 iki PR'a ayrılır: H1a backend (İK modülünün kurulumu ve organizasyon modeli), H1b web.

**Modül:** `Merp.Modules.HR.{Domain,Application,Infrastructure,Api,Contracts}`, şema `hr`, `HrDbContext`, test projeleri `Merp.Modules.HR.UnitTests` ve `Merp.Modules.HR.IntegrationTests`. Modül API host'una ve migrator'a İşe alım modülündeki gibi eklenir.

**Model:**
- `OrgUnit` (aggregate): şirket, üst birim, kod (şirkette tekil), ad, tür (Direktörlük, Departman, Birim, Takım), yönetici pozisyonu, masraf merkezi kodu, şube, geçerlilik tarihleri ve `path`.
  - `path` PostgreSQL `ltree`'dir. Her etiket birimin kimliğidir (32 hane). Npgsql bunu ek paket olmadan destekler.
  - Alt ağaç sorgusu `path <@ birim.path` ile yapılır ve GiST indeksi kullanır.
  - Taşıma: komut yalnızca taşınan birimi değiştirir ve `OrgUnitMoved` olayını yayınlar. Alt birimlerin `path`'ini aynı işlemde bir domain event işleyicisi tek SQL ile günceller. D21'e göre bu kural 7'yi ihlal etmez. Bir birim kendi alt birimine taşınamaz.
- `Job` (aggregate, unvan kataloğu): kod (kiracıda tekil), ad, aile, seviye (M1, U2…).
- `Position` (aggregate, kadro): birim, unvan, kod (şirkette tekil), bağlı olduğu pozisyon, norm kadro (varsayılan 1), durum (Aktif, Dondurulmuş, Kapalı), yönetici pozisyonu mu.
  - Bağlı olduğu pozisyon boşsa yöneticisi birimin yönetici pozisyonudur. Pozisyon birimin yöneticisiyse bir üst birimin yönetici pozisyonuna bağlanır.

**Doluluk ve kişiler (plandaki sıralamanın çözümü):**
- Pozisyonlara kişi atanması (`JobAssignment`) H2'de, personel–kullanıcı bağı H3'te gelir. H1'de pozisyonların sahibi yoktur.
- İK içinde `IPositionHolders` arayüzü tanımlanır: bugün hangi personelin (ve kullanıcının) hangi pozisyonda olduğunu söyler. H1'deki uygulaması boş döner. H2 bunu görev atamalarından, H3 kullanıcı bağından doldurur.
- Doluluk ve boş kadro bu arayüzden hesaplanır: H1'de bütün kadrolar boş görünür.
- Çözücüler, `ManagedTeam` kapsamı ve `IOrgQuery`'nin kişiyle ilgili yöntemleri H1'de bu arayüzle yazılır.
- Kabul kriteri "`line-manager(2)` doğru kişiyi döner" H1'de birim testiyle, sahte bir `IPositionHolders` ile doğrulanır. Uçtan uca entegrasyon testi H3'e taşınır (kullanıcı bağı gelince).

**Raporlama hattı:**
- `reporting_lines` (yönetici pozisyon, ast pozisyon, derinlik) kapanış tablosudur, pozisyon hiyerarşisinden üretilir.
- Pozisyon veya birim değişince olay işleyicisi ilgili şirketin tablosunu aynı işlemde yeniden kurar. Her gece tüm kiracılarda tam yeniden kurulur (Hangfire, `hr.reporting-lines`).
- H2'de görev atamasındaki yönetici ezmesi bu tabloya eklenmez; kişinin atamasında tutulur (D38).

**Sözleşmeler ve Platform kayıtları:**
- `IOrgQuery` (HR.Contracts, salt okunur):
  - birimin alt ağacı
  - birimin yönetici pozisyonu ve sahibi
  - personelin yönetici zinciri
  - kullanıcının organizasyon bilgisi
- Çözücüler (`IApproverResolver`):
  - `line-manager` (seviye 1..n)
  - `org-unit-manager` (olgudaki birim, yoksa talep edenin birimi)
  - `position` (pozisyonun sahibi)
- `IApproverManagerLookup`: süre aşımında yöneticiye yükseltme için.
- Kapsam türleri:
  - `OrgUnit`: kimlik ister, alt birimleri kapsayabilir.
  - `ManagedTeam`: kullanıcının doğrudan ve dolaylı astları.
  - İK listeleri bu kapsamları kendi sorgularında predikata çevirir: `OrgUnit` için `ltree`, `ManagedTeam` için kapanış tablosu ve pozisyon sahipleri.

**Yetkiler:** `hr.org.read/manage`. İK Yöneticisi (`hr.*`) ikisine de, İK Uzmanı okumaya sahiptir. Birim ve pozisyon yönetimi şirket kapsamına uyar: kapsam dışındaki şirketin kaydı 404 döner, kapsam dışındaki şirkete kayıt açmak 403'tür. Unvan kataloğu kiracıda ortak olduğu için yalnızca Tenant kapsamıyla yönetilir; her kapsam okuyabilir.

**Web (H1b):**
- İK → Organizasyon: birim ağacı ve birim detayı, pozisyonlar (norm, dolu, boş), unvanlar.
- Organizasyon şeması kendi bileşenimizle çizilir (yerleşim `d3-hierarchy`, ISC; D18). `d3-org-chart` bağımlılığı `d3-flextree` WTFPL lisanslı olduğu için kullanılmadı. Fotoğraflar personel kartıyla (H2) gelir; o zamana kadar baş harfler gösterilir.

**Uygulama notları (H1a):**
- Şirket ve şube bilgisi Platform.Contracts'taki salt okunur `IOrganizationLookup` ile alınır (kural 3, `IWorkingCalendar` gibi): kayıt kontrolü, adlar ve şirketin saat dilimi. Birimin bugün geçerli olup olmadığı şirketin saat dilimine göre hesaplanır.
- Okuma kapsamı: Tenant her şey, Company şirketin kayıtları, Branch o şubeye bağlı birimler, OrgUnit birim (alt birimleriyle birlikte seçildiyse `ltree` alt ağacı), ManagedTeam kullanıcının pozisyonlarının astları (kapanış tablosu), Self kullanıcının kendi pozisyonları. Pozisyonlar birimlerini izler.
- Uygulama katmanı `OrgPaths.IsWithin` kullanır. Infrastructure'daki sorgu kesicisi bunu Npgsql'in `<@` operatörüne çevirir (D30'daki özel alan filtresi gibi), böylece Application Npgsql'e bağlanmaz.
- `reporting_lines` tablosunda yabancı anahtar yoktur. Tabloyu, yeni pozisyon kaydedilmeden önce çalışan domain event işleyicisi kurar. Pozisyonlar silinmez.
- Kapatılan pozisyonlar zincirde atlanır: astları kapatılan pozisyonun yöneticisine bağlanır. Bir birimin yöneticisi olan pozisyon kapatılamaz ve biriminden çıkarılamaz. Bir pozisyonu kendisine bağlayacak değişiklik (bağlı olunan pozisyon, birim yöneticisi, taşıma) 400 ile reddedilir.
- `line-manager(n)`: seviyeler kişiye göre sayılır, boş pozisyonlar atlanır. n. seviyedeki yöneticinin kullanıcısı yoksa sonuç boştur ve uyarı loglanır (yetki-ve-onay.md §2.4).
- `org-unit-manager`: yöneticisi olmayan birimde en yakın üst birimin yöneticisi alınır. Talep eden kendi biriminin yöneticisiyse bir üstteki yöneticiye gider.
- Oluşturma uç noktalarının ortak yanıtı `EntityCreatedResponse` Merp.Web'e taşındı; OpenAPI'de tek şema kalır.

## D38 — Personel kartı ve yaşam döngüsü

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-17). PLAN H2 ile uygulanır.

**Bölünme:** H2a domain, yaşam döngüsü ve kişi bilgisi komutları ile uç noktaları, hassas veri, migration; H2b sorgular (liste, kart), maskeleme ve okuma audit'i, veri kapsamı, belgeler ve hatırlatmalar, fotoğraf; H2c web.

**Aggregate'ler (D6):**
- `Employee`: kişi. TCKN kiracıda tekildir. Kimlik, iletişim (e-posta, telefon, adres, acil durum kişisi), aile, eğitim, sertifika ve ehliyet (geçerlilik tarihli), yabancı dil, askerlik durumu, IBAN, fotoğraf (D31, amaç `photo`), özel alanlar (D30, `hr.employee`) ve `UserId` (H3'te dolar).
  - `Employment` ve `JobAssignment` bu aggregate'in alt varlıklarıdır. İşe giriş, görev değişikliği, çıkış, tekrar işe giriş ve grup içi nakil tek aggregate'i değiştirir. Nakilde eski istihdamın kapanması ile yenisinin açılması tek işlemde olur (kural 7).
  - Eşzamanlılık her satırın kendi `xmin`'iyle kalır: kişi kartı ile görev geçmişini farklı kullanıcıların aynı anda düzenlemesi çakışmaz.
- `EmployeeSensitiveData` (ayrı aggregate ve tablo, personelle 1:1): engellilik derecesi, kan grubu, sağlık notları, adli sicil belgesi tarihi (D13).
- `EmployeeDocument` (ayrı aggregate): tür, dosya (D31), geçerlilik tarihleri.

**Hassas veri (D13):**
- Şifreleme `ISensitiveDataProtector` arayüzüyle yapılır (Merp.Application.Abstractions). Infrastructure'daki uygulaması ASP.NET Data Protection kullanır. Amaç `Merp.SensitiveData` ile kiracı kimliğidir; anahtarlar katalog veritabanındadır, üretimde sertifikayla şifrelenir (mevcut ayar). Uygulama katmanı ASP.NET'e bağlanmaz.
- Domain şifreli metni tutar. Handler yazarken şifreler. Okurken `hr.employees.sensitive.read` yetkisi varsa çözer ve okumayı `ISensitiveReadLog` ile audit'e yazar; yoksa alanlar boş ve "gizli" işaretli döner. Audit kayıtlarında bu alanların değeri yerine `***` yazılır.
- TCKN ve IBAN `[Sensitive]` işaretlidir: aynı yetki yoksa maskelenir (`123******01`, `TR** **** … 1234`), açık okunduğunda audit'e yazılır. TCKN tekillik kontrolü için düz tutulur.

**İstihdam:**
- `Employment`: şirket, sicil no (P5 `hr.employee`, şirket bazında `{seq:6}`, sıfırlanmaz), işe giriş tarihi, kıdem başlangıç tarihi ve değişiklik gerekçesi, istihdam tipi (Belirsiz, Belirli süreli + bitiş, Kısmi + haftalık saat, Stajyer, Mevsimlik), deneme süresi bitişi, çalışma takvimi ve izin politikası ezmeleri, çıkış bilgisi (tarih, SGK çıkış kodu, gerekçe, tekrar işe alınabilir mi, not).
- Durum tarihlerden hesaplanır: Başlayacak (giriş ileri tarihli), Aktif, Ayrıldı. Bir şirkette aynı anda tek açık istihdam olur. Farklı şirketlerde eşzamanlı istihdam olabilir; biri birincil işaretlenir ve "birincil pozisyon" ondan alınır.

**Görev ataması:**
- `JobAssignment`: pozisyon (birim pozisyondan gelir), şube, yönetici ezmesi (bir pozisyon), unvan, seviye, masraf merkezi, değişiklik nedeni (Giriş, Nakil, Terfi, Reorganizasyon, Düzeltme), başlangıç ve bitiş günü (ikisi dahil). Unvan, seviye ve masraf merkezi atama anındaki pozisyon ve birimden kopyalanır, değiştirilebilir; tarihçe böylece sabit kalır.
- Yeni atama öncekini bir gün önce kapatır. İleri tarihli atama kaydedilir; bugünkü görev her sorguda tarihlerden hesaplanır (kadro doluluğu, kapsam, listeler). Gece işi `hr.assignments-effective`, o gün başlayan atamalar için `EmployeeAssignmentChanged` yayınlar.
- `IPositionHolders` (D37) bugün geçerli atamalardan doldurulur: kadro doluluğu ve boş kadro gerçek değer gösterir.
- Yönetici ezmesi `reporting_lines`'a yazılmaz; o tablo pozisyonlar arasında kalır (norm kadrosu birden çok olan pozisyonda kişiye özel ezme pozisyon tablosuna sığmaz). `line-manager`, yönetici araması ve `ManagedTeam` önce kişinin ezmesine bakar, sonra pozisyon zincirine. D37'deki "ezme bu tabloya eklenir" ifadesi bununla değişir.

**Komutlar:**
- `Hire`: yeni kişi, ilk istihdam ve ilk atama. TCKN kayıtlıysa anlamlı hata döner ve mevcut personel gösterilir; tekrar işe giriş `Rehire` ile yapılır.
- `Rehire`: ayrılmış personele aynı ya da başka şirkette yeni istihdam.
- `ChangeAssignment`: ileri tarihli olabilir. Aynı günü değiştiren kayıt "Düzeltme" nedeniyle mevcut atamanın yerine geçer.
- `Terminate`: çıkış günü açık atamayı kapatır, sonraki ileri tarihli atamaları siler. SGK çıkış kodu zorunludur.
- `GroupTransfer`: kaynak istihdam nakil gününden bir gün önce kapanır, hedef şirkette yeni istihdam açılır. Kıdem başlangıcı varsayılan olarak korunur; izin bakiyesinin taşınması olayda bir seçenek olarak taşınır, izin modülü (L) uygular.
- Kişi bilgileri, iletişim, aile ve nitelikler için ayrı güncelleme komutları.

**Olaylar (HR.Contracts):** `EmployeeHired`, `EmployeeAssignmentChanged`, `EmployeeTerminated`, `EmployeeRehired`, `EmployeeGroupTransferred`. Personel, istihdam, şirket ve tarihleri taşır.

**Referans veriler:**
- Belge türleri kiracı tablosudur: seed'lenir (kimlik, diploma, sağlık raporu, sözleşme, SRC, pasaport…), sonradan eklenebilir.
- SGK çıkış kodları, eğitim seviyeleri, yakınlık dereceleri ve askerlik durumları kodda sabit listelerdir (yasal ya da değişmeyen değerler); adları `tr-TR.json`'dadır.

**Belge hatırlatma:** Gece işi `hr.document-reminders`, bitişine 30 ve 7 gün kalan belgeler için bildirim yollar. Alıcılar, belgenin şirketinde `hr.employees.documents.manage` yetkisine sahip kullanıcılardır. Bunun için Platform.Contracts'a salt okunur `IPermissionHolders` eklenir: bir yetkiye belirli şirket kapsamında sahip etkin kullanıcıları döner.

**Veri kapsamı (personel listeleri):** Tenant hepsi; Company istihdam şirketi; Branch bugünkü atamanın şubesi; OrgUnit bugünkü atamanın birimi (alt birimlerle, `ltree`); ManagedTeam raporlama hattında altımdaki pozisyonların sahipleri ve yönetici ezmesi benim pozisyonuma olanlar; Self kendi personel kaydı (H3).

**Yetkiler:** `hr.employees.read/create/update/terminate/export`, `hr.employees.sensitive.read/update`, `hr.employees.documents.read/manage`. Sistem rollerinin desenleri değişmez (İK Uzmanı hassas veri hariç tüm personel yetkilerine sahiptir).

**Listeler:** Varsayılan liste bugün aktif personeli gösterir. Durum filtresiyle Başlayacak ve Ayrılanlar görünür.

**Uygulama notları (H2a):**
- SGK çıkış kodları kodda sabit liste yerine kiracı tablosudur (`sgk_exit_codes`): yaygın kodlar seed'lenir, liste düzenlenebilir kalır. SGK listeyi zaman zaman değiştirdiği için yanlış ya da eksik bir kod müşteri tarafında düzeltilebilir. Eğitim seviyeleri, yakınlık dereceleri ve askerlik durumları kodda sabittir.
- Başlamış atamaların duyurusu gece yerine saatte bir çalışır (`hr.assignments-effective`, her saatin 5. dakikası): her saat dilimindeki şirket, günü başladıktan en geç bir saat sonra duyuru alır. Duyurulan atama işaretlenir; olay bir kez yayınlanır.
- Tekrar işe girişte yalnızca yeni şirketin kapsamda olması yeterlidir: kişi, çağıranın göremediği bir şirketten gelebilir.
- Görev değişikliğinin nedeni "Giriş" olamaz; giriş yalnızca işe giriş ve tekrar işe girişte kullanılır.
- Kişi kartı komutları (kişisel bilgiler, iletişim, aile, nitelikler, IBAN) kartın sürümünü ister; aynı anda yapılan değişiklik 409 döner. Hassas veri kendi satırındadır, sürüm istemez.
- jsonb sütun yardımcısı (`HasJson`) Platform'dan Merp.Infrastructure'a taşındı; modüller ortak kullanır.

**Uygulama notları (H2b1):**
- Personel listesinde her satır bir istihdamdır (kişi + şirket): sicil no şirket bazındadır ve şirket kapsamı istihdama uygulanır. Aynı anda iki şirkette çalışan kişi iki satırda görünür; tekrar işe girmiş kişinin eski istihdamı "Ayrılanlar" filtresinde kalır.
- "Bugün" her satırda istihdamın şirketinin saat dilimine göre hesaplanır. Bir anda şirketlerin günü UTC gününe eşit ya da bir gün geride veya ileride olduğundan, SQL günü bu üç değerden seçer (şirket listeleri parametredir).
- Satırın görevi bugünü kapsayan atamadır. Başlamamış istihdamda ilk atama, bitmiş istihdamda son atama kullanılır; böylece şube, birim ve yönetici kapsamı ayrılan ve başlayacak personeli de yerleştirir.
- ManagedTeam kapsamı: kişinin pozisyonlarının altındaki pozisyonların sahipleri ve yönetici ezmesi kişinin pozisyonlarından birini gösterenler. Self kapsamı kullanıcının kendi personel kaydıdır. İkisi de kullanıcı–personel bağına dayanır; uçtan uca test H3'te.
- TCKN listede ve Excel'de her zaman maskelidir. Açık TCKN ve IBAN yalnızca kartta, `hr.employees.sensitive.read` kapsamı kişinin görülen istihdamlarından birini kapsıyorsa gelir. Her açık okuma audit'e yazılır (alan bazında `Read` kaydı; özel nitelikli verilerde yalnızca dolu alanlar).
- Kart yalnızca çağıranın kapsamındaki istihdamları gösterir. Grup içi nakilde hedef şirketin İK'sı kaynak şirketteki istihdamı görmez; kıdem tarihi yeni istihdama kopyalandığı için korunur.
- Fotoğraf genel bir dosya uç noktası yerine kendi uç noktalarıyla sunulur (`GET/PUT/DELETE /api/hr/employees/{id}/photo`, okuma `hr.employees.read`, değiştirme `hr.employees.update`). Böylece D22 listesine yeni bir "yalnızca oturum" uç noktası eklenmez. PNG, JPEG veya WebP, en fazla 2 MB.
- Excel'e aktarma şimdilik özel alan kolonlarını içermez: özel alan tanımları Platform'dadır ve modüller arası okuma arayüzü henüz yoktur. Personel listesi ekranıyla (H2c) birlikte ele alınacak.

**Uygulama notları (H2b2):**
- Belgenin dosyası belgenin kendisine aittir (sahip türü `hr.employee_document`, amaç `attachment`). Fotoğrafta olduğu gibi kendi uç noktalarıyla sunulur (`/api/hr/employees/{id}/documents`, okuma `hr.employees.documents.read`, değiştirme `hr.employees.documents.manage`); D22 listesine ekleme yapılmaz. Dosya kuralları eklerinkiyle aynıdır (`Files:AllowedExtensions`, `Files:MaxBytes`).
- Belge silme yumuşak silmedir: kayıt audit için kalır, dosyanın içeriği commit'ten sonra silinir.
- Belge türleri tüm şirketlerde ortaktır; unvan kataloğu gibi yalnızca Tenant kapsamıyla eklenir ve değiştirilir. Kod değişmez, kullanılmayan tür pasifleştirilir; pasif tür yeni belgede seçilemez, mevcut belgede kalır.
- Hatırlatma işi `hr.document-reminders` her gün 05:00 UTC'de (İstanbul'da 08:00) çalışır. Kalan gün, belgenin sahibinin birincil şirketinin gününe göre sayılır. Belge 30 güne girince ilk, 7 güne girince ikinci hatırlatma gider; her biri geçerlilik tarihi başına bir kez. Geç girilen belge doğrudan 7 gün hatırlatmasını alır. Geçerlilik tarihi değişince hatırlatmalar baştan başlar.
- Alıcılar, kişinin çalıştığı ya da işe başlayacağı şirketlerde `hr.employees.documents.manage` yetkisine Tenant veya o şirketin Company kapsamıyla sahip etkin kullanıcılardır (`IPermissionHolders`). Şube ve birim kapsamlı yetki sahipleri şimdilik bildirim almaz. Ayrılmış personelin belgeleri için hatırlatma gitmez.
- Bildirimdeki belge türü adı kiracının varsayılan dilindedir (`tr-TR`): bildirim argümanları alıcıdan bağımsız metinlerdir.

**Uygulama notları (H2c1):**
- Personel listesinde arama 11 haneli geçerli bir TCKN ise yalnızca o TCKN'li kişiyi bulur. İşe giriş, TCKN kayıtlıysa reddederken bu bilgiyi zaten verdiği için arama yeni bir şey açığa çıkarmaz; sonuç yine veri kapsamından geçer. İşe giriş sihirbazı "TCKN kayıtlı" hatasında listeyi bu aramayla açan bağlantıyı gösterir: D38'deki "mevcut personel gösterilir" böyle karşılanır.
- Şube seçimi Platform'un şube listesini kullanır (`platform.branches.read`). Bu yetkisi olmayan kullanıcıya sihirbazda şube alanı gösterilmez; şube sonradan görev değişikliğiyle girilebilir.

**Uygulama notları (H2c2):**
- Modüllerin API'ye açılan public tiplerinin adları modüller arasında benzersizdir. OpenAPI şemaları tipin kısa adıyla adlandırılır; HR'ın `ChangeAssignmentCommand` ve `AddressInput` tipleri Platform'dakilerle aynı adı taşıdığı için şemalar birleşmişti. HR tipleri `ChangeEmployeeAssignmentCommand` ve `EmployeeAddressInput` oldu; mimari test (`ApiTypes_HaveUniqueNamesAcrossModules`) bunu denetler.
- IBAN ve özel nitelikli verilerin düzenlenmesi yalnızca değerleri açık görebilen kullanıcıya sunulur: hassas veri komutu tüm alanları birlikte değiştirir, maskeli değerlerle açılan form mevcut veriyi silerdi.
- Belge türleri ekranı menüde `hr.employees.documents.manage` ile görünür; ekleme ve değiştirme yine Tenant kapsamı ister.

## D39 — Personel–kullanıcı bağı ve self servis

**Durum:** Kabul edildi (kullanıcı onayı, 2026-10-21). PLAN H3 ile uygulanır. D6'yı ayrıntılandırır.

**Bölünme:**
- **H3a — bağ ve yaşam döngüsü:** kullanıcı oluşturma, mevcut kullanıcıya bağlama, bağı kaldırma, çıkışta pasifleştirme, `ICurrentUser.EmployeeId`, `line-manager(2)` uçtan uca testi.
- **H3b — self servis:** "Kartım", değişiklik talepleri, onay motoru bağlantısı, audit kaynağı.
- **H3c — web:** karttaki kullanıcı bölümü, "Kartım" ekranı, talep formları, İK kartında talepler sekmesi.

**Bağımlılık yönü:** Platform, HR'a bağımlı olmaz. HR'ın Platform'dan istekleri Platform.Contracts'taki olaylarla gider (`NotificationRequested` ve `ApprovalRequested` gibi); Platform HR.Contracts'a referans vermez.

**Bağın tek yazarı Platform'dur:**
- Bağ iki tarafta tutulur (D6):
  - `User.EmployeeId` (Platform): `ICurrentUser.EmployeeId` buradan gelir.
  - `Employee.UserId` (HR): pozisyon sahipliği, ManagedTeam ve Self kapsamı buradan okunur.
- Bağı yalnızca Platform yazar ve olay yayınlar; HR kendi alanını bu olaylarla günceller. Tekillik Platform'daki benzersiz indeksle (`employee_id`) güvence altındadır.
- Bağ değişince Platform kullanıcının erişim önbelleğini temizler; `ICurrentUser.EmployeeId` bir sonraki istekte dolar.

**Olaylar (Platform.Contracts):**
- HR → Platform:
  - `UserProvisioningRequested(EmployeeId, UserName, Email, FirstName, LastName, Culture, RequestedByUserId)`: Platform Keycloak kimliğini ve kullanıcıyı açar, davet e-postasını gönderir (P2'deki kullanıcı oluşturmayla aynı yol), Çalışan rolünü Self kapsamıyla atar, bağı yazar.
  - `UserLinkRequested(EmployeeId, UserId, RequestedByUserId)`: mevcut kullanıcı bağlanır; Çalışan rolü yoksa Self kapsamıyla eklenir.
  - `UserUnlinkRequested(EmployeeId, UserId)`: bağ kaldırılır; rollere dokunulmaz, Self kapsamı artık boş sonuç verir.
  - `UserDeactivationRequested(UserId, EmployeeId)`: kullanıcı ve Keycloak kimliği pasifleşir (mevcut etkin/pasif mantığı).
- Platform → HR:
  - `UserLinkedToEmployee(EmployeeId, UserId)`
  - `UserUnlinkedFromEmployee(EmployeeId, UserId)`
  - `UserProvisioningFailed(EmployeeId, ErrorCode)`: kullanıcı adı veya e-posta kullanımda, kullanıcı başka personele bağlı, kimlik sağlayıcı hatası.

**HR tarafı:**
- Yeni yetki `hr.employees.user` (sistem kullanıcısı oluşturma ve bağlama). İK Yöneticisi (`hr.*`) ve İK Uzmanı (`hr.employees.*`) desenleriyle gelir. `platform.users.manage` gerekmez: bu yol yalnızca Çalışan rolünü Self kapsamıyla verir.
- Komutlar: `CreateEmployeeUser`, `LinkEmployeeUser`, `UnlinkEmployeeUser`. E-postanın varsayılanı kişinin birincil e-postası, kullanıcı adının varsayılanı e-postadır.
- Kişinin bağ durumu kartta görünür: yok, bekliyor, bağlı, hata (hata koduyla). Bekleyen bir istek varken yenisi kabul edilmez.
- Bağlanacak kullanıcıyı seçmek için Platform.Contracts'a salt okunur `IUserDirectory` eklenir (kural 3):
  - ada veya e-postaya göre etkin kullanıcı arama,
  - kimliklerle kullanıcı özetleri: ad, e-posta, etkin mi, bağlı olduğu personel.
- Kart, bağlı kullanıcının etkin olup olmadığını `IUserDirectory`'den gösterir.

**Çıkışta pasifleştirme:**
- Ayar `hr.users.deactivation` (HR, şirket bazında ezilebilir, D35):
  - `on_termination_date` (varsayılan, D6): kullanıcı çıkış gününün sonunda pasifleşir;
  - `immediately`: çıkış kaydedilince pasifleşir.
- Karar HR'dadır: kişinin başka bir devam eden ya da başlayacak istihdamı varsa (grup içi nakil, eşzamanlı istihdam) kullanıcı pasifleşmez.
- `immediately`: istek çıkış komutuyla aynı işlemde outbox'a yazılır.
- `on_termination_date`: saatlik iş `hr.user-deactivations`, çıkış günü kendi şirketinin saat diliminde bitmiş kişiler için isteği yayınlar. İstihdam işaretlenir, istek bir kez gider.
- Tekrar işe girişte kullanıcı kendiliğinden etkinleşmez. Pasifliğin başka bir nedeni olabilir; otomatik etkinleştirme onu geri alırdı. Kart "kullanıcı pasif" uyarısını gösterir, etkinleştirme Kullanıcılar ekranından yapılır.

**Self servis:**
- Yetkiler `hr.self.*` altındadır (Çalışan rolünün mevcut deseni, yetki-ve-onay.md §1.4): `hr.self.profile.read` ve `hr.self.profile.request`.
- Uç noktalar:
  - `/api/hr/me`: kendi kartı;
  - `/api/hr/me/profile-changes`: kendi talepleri.
  - Kişi `ICurrentUser.EmployeeId` ile bulunur, yolda kimlik yoktur. Bağı olmayan kullanıcı 404 alır.
- Kendi kartında TCKN ve IBAN açık gösterilir, okunmaları audit'e yazılır (KVKK m.11: ilgili kişinin kendi verisine erişimi). Özel nitelikli veriler self serviste gösterilmez (D13); onları İK görür.
- `hr.employees.read`'i Self kapsamıyla veren roller mevcut kart uç noktasını kullanır: kendi kartı 200, başkasınınki 404 (H2b1'deki Self kapsamı). "Başkasının kartını göremez" kabul kriteri bununla test edilir.

**Değişiklik talebi (`hr.profile-change`):**
- Ayrı aggregate `ProfileChangeRequest` (kural 7):
  - numara: P5 `hr.profile-change`, `PDT-{yyyy}-{seq:5}`, yıllık sıfırlanır;
  - personel ve talep eden kullanıcı;
  - tür: İletişim (e-posta, telefon, adres, acil durum birlikte), Aile veya IBAN;
  - önerilen değerler: jsonb, güncelleme komutlarıyla aynı biçim. IBAN ayrı bir `[Sensitive]` alanda tutulur;
  - talep anındaki değerler (`Before`);
  - durum: Bekliyor, İade edildi, Onaylandı ve uygulandı, Uygulanamadı, Reddedildi, Geri çekildi.
- Bir kişinin aynı türde tek açık talebi olur.
- Gönderim `ApprovalRequested` ile yapılır:
  - olgular: `kind` (metin) ve şirket (kişinin birincil şirketi);
  - varsayılan akış (D34): tek adım, rol `hr-specialist`;
  - akış yoksa talep otomatik onaylanmaz, "ilgilenilmeli" olur.
- İade edilen talep düzeltilip yeniden gönderilir ya da geri çekilir.
- Onaylanınca HR, inbox'tan gelen `ApprovalRequestCompleted` ile talebi karta uygular. Kartın ilgili bölümü talep anından beri değiştiyse uygulamaz: talep "Uygulanamadı (kart değişti)" olur, İK'nın son düzenlemesi ezilmez.
- Talep eden uygulandı, reddedildi ve uygulanamadı durumlarında bildirim alır (`hr.profile_change_completed`).
- Kimler görür:
  - talep eden kendi taleplerini;
  - İK, kartta "Değişiklik talepleri" sekmesinde (`hr.employees.read` ve kapsamıyla);
  - onaylayıcılar, `IApprovalDocumentAccessPolicy` ile.

**Audit kaynağı (yapı taşı değişikliği):**
- Kabul kriteri: onaylanan değişiklik audit'te talep numarasıyla görünür.
- `audit_entries` tablosuna `source` sütunu eklenir (en fazla 100 karakter); her modülün audit tablosuna migration gider.
- Toplu ya da dolaylı değişiklik yapan komut kaynağını `IAuditSource` ile belirler (ör. `hr.profile-change:PDT-2026-00012`); kaynak aynı işlemdeki tüm audit kayıtlarına yazılır. Kullanıcının doğrudan düzenlemesinde boş kalır.
- Audit ekranı "Kaynak: Değişiklik talebi PDT-2026-00012" gösterir. İçe aktarma (H4) gibi işler de aynı alanı kullanabilir.
- Uygulamayı sistem yapar, kayıttaki kullanıcı boştur. Kimin istediği ve kimin onayladığı talepte ve onay geçmişindedir.

**`line-manager(2)` uçtan uca (D37'den taşınan kabul kriteri):** H3a'da bağlı kullanıcılarla iki seviyeli pozisyon zinciri kurulur; onay talebi ikinci seviye yöneticinin kullanıcısına düşer.

**Uygulama notları (H3a):**
- Kullanıcı istekleri (`POST /api/hr/employees/{id}/user`, `.../user/link`, `DELETE .../user`) 202 döner: istek Platform'a yola çıkmıştır, kart Platform'un cevabına kadar "bekliyor" gösterir. Reddedilen isteğin nedeni kartta kullanıcının dilinde döner.
- Bağlanacak kullanıcı adayları `GET /api/hr/employees/user-candidates` ile aranır; başka personele bağlı kullanıcılar listede yer almaz.
- `/api/platform/me` kullanıcının bağlı olduğu personeli de döner (`employeeId`); web "Kartım"ı (H3c) buna göre gösterir.
- Pasifleştirme işi `hr.user-deactivations` her saatin 15. dakikasında çalışır. Platform son sistem yöneticisini hiçbir durumda pasifleştirmez.

**Uygulama notları (H3b):**
- Self servis uçları `/api/hr/me` altındadır: `GET /api/hr/me` (Kartım), `GET /api/hr/me/profile-changes`, `POST .../contacts`, `.../family`, `.../iban` (201) ve `POST .../{id}/withdraw` (204). İK, kişinin taleplerini `GET /api/hr/employees/{id}/profile-changes` ile görür; önerilen IBAN `hr.employees.sensitive.read` yoksa maskelenir, açık okunması audit'e yazılır.
- Kartım, kartın kendisini döner: TCKN ve IBAN açık, okunmaları audit'e yazılır; özel nitelikli veriler (sağlık, engellilik, adli sicil) self serviste gösterilmez.
- Talep türleri: `Contacts` (e-posta, telefon, adres ve acil durum kişileri birlikte), `Family`, `Iban`. Her tür için aynı anda tek açık (bekleyen veya iade edilmiş) talep olabilir; ikincisi 409 döner.
- İade edilen talep `requestId` ile düzeltilip aynı numarayla yeniden gönderilir; Platform onayı baştan başlatır. Talep sahibi açık talebini geri çekebilir; Platform'a `ApprovalCancellationRequested` gider.
- Onaylanan talep aynı işlemde karta yazılır (`ProfileChangeApproved` domain olayı, kural 7). Kartın ilgili bölümü talepten sonra değiştiyse talep "Onaylandı ama işlenmedi" (`NotApplied`) olarak kapanır, İK'nın sonraki düzenlemesi korunur. Sonuç talep sahibine `hr.profile_change_completed` bildirimiyle gider.
- Audit kaydının `source` alanı `IAuditSource` ile komutun geri kalanı için ayarlanır; onaylanan talebin kart değişiklikleri `hr.profile-change:PDT-2026-00012` biçiminde kaynak taşır.

**Uygulama notları (H3c):**
- Personel kartında "Sistem kullanıcısı" bölümü bağlı kullanıcıyı ve aktif/pasif durumunu gösterir. `hr.employees.user` yetkisi olan kullanıcı oluşturur, mevcut kullanıcıya bağlar ve bağı kaldırır. İstek Platform'dayken kart birkaç saniyede bir kendini yeniler; reddedilen isteğin nedeni bölümde görünür. Yetkisi olmayan okuyucu bölümü yalnız bağlı kullanıcı varsa görür.
- "Kartım" (`/hr/me`, menüde `hr.self.profile.read` ile) kişinin kendi kartıdır: TCKN ve IBAN açık, özel nitelikli veri kartı yoktur. Fotoğraf İK uç noktasından geldiği için Kartım'da yer tutucu gösterilir. Hesabı personele bağlı olmayan kullanıcı açıklayıcı bir mesaj görür.
- Talep formları İK'nın iletişim ve aile formlarıyla aynıdır, açıklama alanı eklenir ve "Onaya gönder" ile talep oluşturur; IBAN için ayrı form vardır. Bir bölümde açık talep varken o bölümün "Değişiklik iste" düğmesi pasiftir. İade edilen talep "Düzelt ve gönder" ile önerdiği değerlerden açılır.
- Talep listesi (Kartım'da "Taleplerim", İK kartında "Değişiklik talepleri") satır açıldığında önerilen değerleri, açıklamayı, iade gerekçesini ve onay sürecini gösterir. Kartın sekmesi adreste tutulur (`?tab=requests`); bildirim ve onay görevi bağlantıları doğrudan talepler sekmesini açar.

## D40 — Organizasyon ve personel içe aktarma

**Bağlam:** H4, D35'teki içe aktarma altyapısına İK tanımlarını ekler. Kabul kriterleri altyapıda iki değişiklik gerektirir: hatalı satırlar varken diğer satırların aktarılabilmesi ve kayıtlı personelin TCKN'ye göre güncellenmesi. İkisi de kullanıcıyla kararlaştırıldı (2026-10-24).

**Bölünme:** H4 iki PR'dır: H4a backend (altyapı değişiklikleri, dört tanım, örnek dosyalar), H4b web.

**Hatasız satırları aktarma (D35'i genişletir):**
- Doğrulamada hata varsa iş yine "hatalı" olur ve hata raporu indirilir. Yalnız bazı satırlarda hata varsa kullanıcı "Hatasız satırları aktar" seçebilir (`POST …/jobs/{id}/commit?skipInvalidRows=true`). Dosyanın bütününe ait hata (eksik sütun, yetki) varsa bu seçenek yoktur.
- Commit sırasında satırlar hatalılar çıkarılarak yeniden doğrulanır ve hiçbir satır düşmeyene kadar tekrarlanır. Böylece hatalı bir satıra bağlı satırlar da atlanır (ör. üst birimi hatalı olan birim) ve doğrulamadan sonra değişen kayıtlar yakalanır.
- İş "kısmen tamamlandı" olarak kapanır; atlanan satırlar hatalarıyla raporda kalır.
- Seçenek bütün içe aktarmalar için geçerlidir, şubeler dahil. Kullanıcı seçmedikçe hatalı dosya aktarılmaz.

**Güncelleme modu:**
- Tanım `SupportsUpdate` ile güncellemeyi destekleyebilir; yükleme `mode=CreateOrUpdate` ile yapılır (varsayılan `Create`). Mod işte saklanır, doğrulama özeti kaç satırın güncelleme olduğunu gösterir (`updateRows`). Desteklemeyen tanımda bu mod 400 döner.
- Doğrulama bir denemedir (dry run): hiçbir şey yazılmaz, satırlar alanın kurallarıyla (ör. personelde `Employee.Hire`) sınanır, kaç kayıt ekleneceği ve güncelleneceği görülür. Ayrı bir deneme düğmesi yoktur.
- Şimdilik yalnız personel güncellemeyi destekler. Unvan, birim ve pozisyon dosyaları yalnız yeni kayıt ekler.

**Tanımlar (İK):** Dosyalar bu sırayla yüklenir; her biri öncekilerin kodlarına başvurur.
- `hr.jobs` (Unvanlar): kod, ad, meslek ailesi, kademe. Katalog kiracı genelinde olduğu için Tenant kapsamı gerekir (D37); yoksa dosyanın bütünü hatalıdır.
- `hr.org-units` (Birimler): şirket kodu, birim kodu, ad, tür, üst birim kodu, masraf merkezi, şube kodu, başlangıç tarihi. Üst birim şirkette kayıtlı olmalı ya da dosyada daha önceki bir satırda gelmelidir; sonra gelirse hata satırı aşağı taşımayı söyler.
- `hr.positions` (Pozisyonlar): şirket, kod, birim, unvan, bağlı olduğu pozisyon (kayıtlı ya da daha önceki satır), norm kadro, yönetici pozisyonu, "Birimin yöneticisi". Sonuncusu birimin yönetici pozisyonunu ayarlar; birimin zaten yöneticisi varsa hatadır.
- `hr.employees` (Personel): kişi, birincil e-posta ve cep telefonu, istihdam ve ilk görev aynı satırda (işe giriş tarihi, çalışma şekli, sözleşme bitişi, haftalık saat, deneme süresi, kıdem tarihi ve gerekçesi, pozisyon, şube, yönetici pozisyon, kademe, masraf merkezi). Yetki `hr.employees.create`; sicil numarası numaratörden alınır ve `EmployeeHired` yayınlanır. IBAN ve özel nitelikli veriler dosyada yoktur, kartta ayrı yetkiyle girilir. Zorunlu özel alanı olan şirkette satır, alanın adıyla hata verir.
- Şirket kodla, şube şirketin aktif şubeleri arasında kodla bulunur. Bunun için `IOrganizationLookup`'a iki salt-okunur sorgu eklenir: `FindCompaniesByCodeAsync` ve `GetBranchesOfCompaniesAsync`.
- Veri kapsamı: her satırın şirketi kullanıcının ilgili yetkideki kapsamında olmalıdır (Tenant ya da şirket); arka planda kapsam kullanıcının kimliğiyle okunur (D35).

**Personelin güncellenmesi:**
- `CreateOrUpdate` ile kayıtlı TCKN'nin satırı kişiyi günceller: ad, soyad, doğum tarihi ve yeri, cinsiyet, uyruk, medeni ve askerlik durumu. Boş hücre mevcut değeri korur.
- E-posta ve telefon birincil olur: listede varsa birincil işaretlenir, yoksa eklenir; diğerleri kalır.
- İstihdam ve görev sütunları kayıtlı kişide kullanılmaz. Görev değişikliği tarih ve gerekçesiyle karttan yapılır (D38).
- Kişinin satırdaki şirkette açık istihdamı olmalıdır; yoksa satır hata verir ve tekrar işe giriş ya da grup içi nakil karttan yapılır. Güncelleme için `hr.employees.update` yetkisinin kapsamı şirketi kapsamalıdır.
- `Create` modunda kayıtlı TCKN hatadır.

**Domain olayları (yapı taşı değişikliği):** Unit of work bir turdaki eşit domain olaylarını (aynı değerli record'lar) bir kez işler. 100 pozisyonluk bir parti şirketin raporlama hatlarını pozisyon başına değil bir kez yeniden kurar.

**Örnek dosyalar:**
- `samples/` klasöründe `DEMO` kodlu şirket için dört dosya vardır: 24 unvan, 71 birim, 147 pozisyon ve 2.000 personel. Veriler hayalidir, sabit tohumla Bogus ile üretilir.
- Üretici `tests/Merp.Modules.HR.UnitTests/Imports/SampleFiles.cs`'tedir. Başlıklar ve seçenekler şablonlarınkiyle aynıdır, İK'nın `tr-TR.json`'undan okunur. Bir birim testi commit edilen dosyaların üreticinin çıktısıyla aynı olduğunu denetler; dosyalar açıkça çalıştırılan bir testle yeniden yazılır (`samples/README.md`).
- Bogus D19'da Test kategorisindedir; üretici test projesinde kalır, uygulamaya girmez.

**Uygulama notları (H4b):**
- "Excel'den içe aktar" İK → Unvanlar, Organizasyon (birimler), Pozisyonlar ve Personel ekranlarında, kaydı ekleme yetkisi olana görünür (`hr.org.manage`, personelde `hr.employees.create`). İçe aktarma sonrası İK'nın bütün listeleri yenilenir.
- Güncellemeyi destekleyen içe aktarmada çekmece yüklemeden önce "Yalnız yeni kayıt ekle" (varsayılan) ya da "Kayıtlı olanları güncelle" sorar. Hatasız dosyada güncelleme modunda kaç kaydın ekleneceği ve güncelleneceği yazar.
- Yalnız bazı satırlarında hata olan dosyada hata raporunun yanında "Hatasız N satırı aktar" düğmesi çıkar; onay penceresi kaç satırın atlanacağını söyler.

## D41 — İzin türleri ve politikaları

**Bağlam:** L1, `docs/domain/izin-kurallari.md` §1–3'teki üç katmanlı modeli (izin türü, politika, atama kuralı) kurar. Bölünme ve sürüm davranışı kullanıcıyla kararlaştırıldı (2026-10-24).

**Bölünme:** L1 iki PR'dır: L1a backend (modeller, seed, kanuni alt sınır, uç noktalar), L1b web.

**Yer ve yetkiler:**
- İzin İK modülündedir (`hr` şeması, `Merp.Modules.HR.*` içinde `Leave`), ayrı modül değildir. Süre hesabı ve talepler (L2–L6) de burada olacak.
- Yeni yetkiler: `hr.leave.settings.read` (türler, politikalar, kurallar ve "hangi politika" sorgusu) ve `hr.leave.settings.manage`. Sistem rollerindeki `hr.leave.*` deseni bunları İK Yöneticisi ve İK Uzmanı'na kendiliğinden verir. Yönetici rolü yalnız `hr.leave.requests.read` ve `hr.leave.calendar.read` aldığı için almaz.
- Tür ve politikalar ya tüm şirketlerde geçerlidir (`CompanyId` boş) ya da bir şirkete aittir. Tüm şirketlerinkini yönetmek Tenant kapsamı ister (unvan kataloğu gibi, D37); şirketinkini şirket kapsamı yeter. Okuyan, tüm şirketlerinkini ve kapsamındaki şirketlerinkini görür.

**Sürümler:**
- Tür limitleri (`LeaveTypeVersion`: olay başına gün, olaydan önceki gün, çoğul gebelik eki, yıllık ve talep başına üst sınır, günlük saat, bildirim süresi) ve politika kuralları (`LeavePolicyVersion` içinde `AnnualLeaveRules`) `ValidFrom` ile sürümlenir. İlk sürüm `0001-01-01` olabilir: "başlangıçtan beri".
- **Başlamış sürüm kilitlidir:** `ValidFrom` bugün veya daha önce olan sürüm değiştirilemez ve silinemez. Yeni sürüm en erken yarından başlar; henüz başlamamış sürümler değiştirilebilir veya silinebilir. "Bugün", kaydın şirketinin saat dilimine göre (yoksa Europe/Istanbul) hesaplanır. Böylece geçmiş hak edişler hep aynı kurala dayanır.
- Güncelleme sürüm listesinin tamamını gönderir; domain başlamış sürümleri değer olarak karşılaştırır (JSON). Yeni kayıtta sürüm tarihleri serbesttir.
- Olaya bağlı türlerde (BABALIK, DOGUM) sürümü olayın tarihi seçer (doğum tarihi), diğerlerinde iznin ilk günü. Seed BABALIK için 5 ve 10 gün, DOGUM için 16 ve 24 hafta (doğumdan önce 8, çoğul gebelikte +2) sürümlerini 01.05.2026 sınırıyla kurar.

**Atama ve öncelik:**
- Atama kuralları şirket başına tek bir sıralı listedir (`LeavePolicyRuleSet`). Böylece sıra tek komutta değişir (kural 7). Her kural bir politika ve kriterler taşır; kriterlerin hepsi eşleşmelidir, kriteri olmayan kural herkese uyar.
- Kriterler: şube, istihdam tipi, birim (alt birimler hariç ya da dahil), kademe, özel alan. Değerlerden biri eşleşirse kriter eşleşir; şube ve birim kimlikleri şirketin olmalıdır.
- Personelin politikası sırasıyla şöyle bulunur:
  1. İstihdama elle verilmiş politika (`Employment.LeavePolicyId`, `PUT /api/hr/employees/{id}/employments/{employmentId}/leave-policy`).
  2. Şirket kurallarından ilk eşleşen.
  3. Şirketin varsayılan politikası.
  4. Tüm şirketlerin varsayılan politikası.
- Her şirkette bir ve tüm şirketlerde bir varsayılan olabilir; yeni varsayılan eskisini aynı işlemde düşürür (domain olayı). Varsayılan politika pasif yapılamaz. Seed politikası `KANUNI` tüm şirketlerin varsayılanıdır.
- "Bu personele hangi politika uygulanıyor?" (`GET /api/hr/leave/policy-resolution?employmentId=&date=`) politikayı, kaynağını (elle, kural ve sırası, şirket ya da kiracı varsayılanı), o günkü sürümü, tamamlanan kıdem yılını, yaşı ve politikanın vereceği yıllık izin gününü döner.

**Kanuni alt sınır:**
- `LegalMinimums` politikanın şu ayarlarını uyarı olarak döner: hak kazanma için 1 yıldan uzun hizmet; 1–4, 5–14 ve 15+ yıl bantlarında kanundan az gün; 18 ve 50 yaşta 20 günden az; devir sınırı; 10 gün kuralının kapalı olması; yol izninin 4 günden az olması.
- Seed türlerinde EVLILIK, EVLAT_EDINME, VEFAT, BABALIK, DOGUM, ENGELLI_COCUK ve SUT kanundaki sürelerle karşılaştırılır.
- Kayıt engellenmez. Uyarılar listelerde sürümüyle döner; politika formu kaydetmeden önce `POST /api/hr/leave/policies/legal-check` ile sorar.
- Yıllık izin günü `AnnualLeaveRules.Entitlement` ile hesaplanır: kıdem kademesinin günü, yaş alt sınırına yükseltilir, sonra eşleşen ek gün kuralları eklenir (§8.2 E2–E7 birim testleri). Hak ediş hareketleri L3'tedir.

**Yapı taşı ve sonrası:** Domain olaylarının bir turda bir kez işlenmesi (D40) varsayılan politika değişimini de tek seferde yapar. Grup kıdemi, ücretsiz iznin hak edişi kaydırması ve birikim yöntemleri politikada tutulur, L3 hak ediş işinde uygulanır.

**Uygulama notları (L1b):**
- İK → "İzin ayarları" (`/hr/leave-settings`, `hr.leave.settings.read`) dört sekmedir: izin türleri, politikalar, atama kuralları ve "Hangi politika?". Sekme adreste tutulur (`?tab=`). Değiştirme düğmeleri `hr.leave.settings.manage` ile görünür.
- Tür ve politika formlarında sürümler ayrı kartlar ya da sekmelerdir. Başlamış sürümler kilit simgesiyle salt okunur gösterilir; yeni sürüm en erken yarından başlar. Yeni politika, tüm şirketlerin varsayılan politikasının son kurallarıyla açılır.
- Politika formu, açık sürümün kurallarını yazmayı bıraktıktan kısa süre sonra kanuni alt sınıra karşı denetler ve uyarıları formun üstünde gösterir.
- Atama kuralları şirket seçilerek düzenlenir; sıra yukarı/aşağı düğmeleriyle değişir, liste tek seferde kaydedilir. Kriter kurucu şube, birim ve istihdam tipini listeden seçtirir; kademe ve özel alan değerleri yazılır.
- "Hangi politika?" sekmesi personeli ve tarihi alır; politikayı, nedenini, sürümünü, kıdemi, yaşı ve yıllık izin gününü gösterir. Ayarları yönetebilen kişi burada istihdama elle politika verir ya da kurallara bırakır.

## D42 — İzin süresi hesabı

**Bağlam:** L2, `docs/domain/izin-kurallari.md` §4'teki süre hesabını kurar. Kurallar belgede tanımlı; aşağıdakiler uygulamada verilen ayrıntılardır.

**Hesaplayıcı:**
- `ILeaveDurationCalculator` (İK, Application) istihdamın takvimini bulur, günleri Platform'un `IWorkingCalendar` sözleşmesinden alır ve saf `LeaveDurations.Count` ile sayar. Talepler (L4) aynı hesaplayıcıyı kullanacak.
- Takvim sırası: istihdamın kendi takvimi (`Employment.WorkScheduleId`), yoksa iznin ilk günündeki görevin şubesinin takvimi, yoksa şirketin, yoksa kiracının varsayılanı. Son üçünü Platform çözer (`ResolveScheduleIdAsync`). Takvim yoksa hata verir.
- Bir izin en fazla 400 gündür (takvim sözleşmesinin sınırı).

**Sayma:**
- İş günü: günün değeri `WorkingDay.LeaveDayValue`'dur (çalışma günü 1, yarım gün tatil 0,5, tatil ve hafta sonu 0). Yarım gün tatil (arife, 28 Ekim) öğleden sonradır; sabahı sayılır.
- İlk gün "öğleden sonra" (`startPart = Afternoon`) ise yalnız öğleden sonrası, son gün "sabah" (`endPart = Morning`) ise yalnız sabahı sayılır. İstenen yarım tatile denk gelirse o gün 0'dır (S6). Toplam 0 ise `hr.leave.errors.zero_duration` döner.
- Takvim günü: aralıktaki her gün 1 sayılır.
- Saat: ilk gün başlangıç saatinden, son gün bitiş saatine kadar mesaiyle kesişen süre, en fazla günün çalışma saati kadar sayılır. Yarım gün tatil o günün mesaisini tatilin başladığı saatte bitirir. Gün karşılığı, aralıktaki tam çalışma günlerinin en uzununa bölünerek verilir (`dayEquivalent`).
- Sabit süreli türlerde (EVLILIK, BABALIK…) bitiş tarihi verilmezse sürüm (olay tarihine ya da ilk güne göre) süreyi verir. Takvim gününde bitiş, sürenin son günüdür (S8); iş gününde, sayılan süreye ulaşılan gündür.

**Önizleme:** `POST /api/hr/leave/duration-preview` (`hr.employees.read`, istihdamın şirketi kapsamda olmalı; değilse 404) aralığı, süreyi ve her günün payını tatil adıyla döner. Çalışanın kendi önizlemesi self servis izinle (L5) `/api/hr/me` altında gelecek; böylece D22 listesine yeni bir oturum-yeter uç noktası eklenmedi.

## D43 — İzin bakiyesi ve hak ediş

**Bağlam:** L3, `docs/domain/izin-kurallari.md` §5'teki bakiye defterini (ledger) ve hak ediş işini kurar. Hak edişin başlangıcı kullanıcıyla kararlaştırıldı (2026-10-24).

**Bölünme:** L3 iki PR'dır: L3a backend (defter, hak ediş işi, bakiye sorgusu, düzeltme, açılış bakiyesi aktarımı, nakil ve kapanış), L3b web (personel kartı İzinler sekmesi, düzeltme formu, açılış bakiyesi aktarma düğmesi).

**İzin hesabı (aggregate):**
- Bakiye bir sayı olarak saklanmaz. Her istihdam ve bakiyeli izin türü için bir `LeaveAccount` vardır; hareketler (`LeaveLedgerEntry`: tarih, ±gün 2 ondalık, kaynak, kaynak kaydı, dönem anahtarı, not, kaydeden) onun içindedir. Bakiye hareketlerin toplamıdır.
- Hareketleri ayrı aggregate yapmak, birkaç yıllık telafiyi ya da kapanışı tek komutta yazmayı kural 7'ye aykırı kılardı. Hesap tek aggregate olduğu için bunlar tek değişikliktir.
- Kaynaklar: `Accrual`, `Request`, `RequestCancel`, `Adjustment`, `OpeningBalance`, `Transfer`, `Termination`.
- Dönem başına tek hak ediş vardır (E8): hesap tekrarı atlar, veritabanında `(account_id, period_key)` hak ediş için tekildir.
- Bakiyeli türler, kendi bakiyesinden düşen (`DeductsFrom` kendi kodu) aktif türlerdir; seed'de YILLIK. Politika hak edişi şirketin, yoksa tüm şirketlerin yıllık türüne yazar.
- Kapanmış hesaba hareket eklenmez. Talep (L4) için `CanTake(gün, bekleyen, avans)` kullanılır (§6 kural 7, E1, E10, E10b). Onaylı talep `Request`, iptali `RequestCancel` yazar (E12).

**Hak ediş işi (`hr.leave-accruals`, her gece 01:00):**
- **Başlangıç (kullanıcı kararı):** şirkette ya da kiracıda ayarlanan "İzin hak edişi başlangıç tarihi" (`hr.leave.accrual_start`, yeni tarih tipi ayar). Bu tarihten önceki hak edişler yazılmaz, geçmiş açılış bakiyesiyle gelir. Ayar boşken açık istihdamlara hak ediş yazılmaz.
- Her istihdam ayrı komuttur; biri hata verse diğerleri sürer, sonraki gece telafi edilir. Tarih, şirketin saat dilimindeki bugündür.
- Hak edişler başlangıç tarihinden ve işe girişten sonra, bugüne (biten istihdamda son güne) kadar yazılır. Kaçırılan günler telafi edilir.
- Yöntemler (`LeaveAccruals`):
  - Kıdem yıldönümü: tamamlanan yıl ve o günkü yaşa göre toptan.
  - Aylık: kıdem tarihinin her ayki gününde, devam eden hizmet yılının gününün 1/12'si.
  - 1 Ocak: tamamlanan yıla ve yaşa göre toptan; ilk yıl kısmi verilmez.
- Politika, son günde personele uygulanan politikadır ve her hak ediş gününde o politikanın o günkü sürümü kullanılır. Ek gün kurallarının kriterleri personelin bugünkü durumuyla değerlendirilir.
- Grup kıdemi ve ücretsiz iznin hak edişi kaydırması politikada tutulur; ücretsiz izinler talep akışıyla (L4) geleceği için şimdilik uygulanmaz.

**Bakiye sorgusu:** `GET /api/hr/employees/{id}/employments/{employmentId}/leave-balances` (`hr.leave.balances.read`, şirket kapsamı) her bakiye için kazanılanı, kullanılanı, bekleyeni (talepler L4'te gelince), kullanılabiliri, kapanış tarihini, sonraki hak edişin tarihini ve gününü ve hareketleri döner. Hesabı henüz olmayan yıllık bakiye sıfırlarla görünür.

**Bakiye hareketleri:**
- **Düzeltme:** `POST …/leave-balances/adjustments` (`hr.leave.balances.manage`), ± gün ve zorunlu gerekçe.
- **Açılış bakiyesi:** `hr.leave-opening-balances` içe aktarması (D35, D40). Satırda şirket kodu, TCKN, tür kodu (boşsa yıllık), tarih, gün (eksi olabilir) ve açıklama vardır. Kişinin şirkette açık istihdamı olmalıdır; her bakiyenin tek açılışı olur.
- **Grup içi nakil:** nakil domain olayı (`EmploymentTransferredInGroup`, aynı işlemde) bakiyeyi kaynak istihdamdan nakil gününün bir gün öncesinde çıkarır, hesabı kapatır ve yeni istihdama nakil gününde ekler (E9). "Bakiyeyi taşı" seçilmezse kaynak, bitişten sonra hak ediş işiyle kapanır ve ödenir.
- **Çıkış:** son günü geçmiş istihdamın hesabını hak ediş işi, son günün hak edişlerinden sonra kapatır. Kalan (avans kullanılmışsa eksi) `Termination` hareketiyle çıkar. `EmployeeLeaveBalanceClosed` entegrasyon olayı kalan günü bordroya bildirir; nakille taşınan bakiye 0 ile kapanır.

**Yetkiler:** `hr.leave.balances.read` ve `hr.leave.balances.manage`; `hr.leave.*` deseniyle İK Yöneticisi ve İK Uzmanı rollerine geçer.

**Yapı taşı:** Ayarlara `Date` tipi eklendi (`yyyy-MM-dd`, varsayılanı boş, nullable `DateOnly` olarak okunur). Ayarlar ekranı bu tip için tarih seçici gösterir.

**Uygulama notları (L3b):**
- Personel kartında "İzinler" sekmesi `hr.leave.balances.read` ile görünür. Her bakiye bir karttır: kullanılabilir, kazanılan, kullanılan, onay bekleyen gün, sonraki hak ediş ve hareketler (tarih, hareket türü, ±gün, açıklama veya dönem, kayıt anı). Birden çok istihdamı olan kişide istihdam seçilir; kapanmış bakiye kapanış tarihiyle gösterilir.
- "Bakiye düzelt" (`hr.leave.balances.manage`) tür, tarih, ±gün ve zorunlu açıklama alır.
- Personel listesinde "İzin açılış bakiyeleri" içe aktarma düğmesi `hr.leave.balances.manage` ile görünür.

## D44 — İzin talebi ve onay

**Bağlam:** L4, `docs/domain/izin-kurallari.md` §6'daki talep doğrulamasını ve `docs/domain/yetki-ve-onay.md` §2.7–2.8'deki onay entegrasyonunu kurar. Kullanıcı kararları (2026-10-24): iki PR (L4a backend, L4b web); belge isteyen türlerde ek gönderirken zorunludur; yol izni yıllık izin talebinin bir alanıdır.

**Talep (`LeaveRequest`, aggregate):**
- Numara `hr.leave-request` (`IZN-{yyyy}-{seq:6}`, şirket başına, yıllık sıfırlanır) taslak oluşturulurken alınır.
- Alanlar: istihdam, tür, başlangıç ve bitiş, ilk günün ve son günün yarısı (öğleden sonra / sabah), saatlik türde saatler, olay tarihi (doğum), yol izni günü, süre, açıklama, izindeki adres ve telefon, vekil personel, ekler.
- Süre her kayıtta hesaplanır ve talepte saklanır: `Quantity` (gün ya da saat) ve `Days` (gün karşılığı). Bakiyeden düşen türde düşülen bakiyenin kodu (`DeductsFrom`) da saklanır.
- Durumlar: `Draft` → `Pending` → `Approved` / `Rejected` / `Returned` (düzeltilip yeniden `Pending`). `Draft`, `Pending` ve `Returned` iptal edilebilir (`Cancelled`). `Approved` → `CancellationPending` → `Cancelled` ya da yeniden `Approved`.
- Kaynak: `Self` (çalışan), `OnBehalf` (İK adına), `Record` (geçmiş kayıt), `Bulk` (toplu izin).
- Onaylanan talep `Days` kadar `Request` hareketi, iptali `RequestCancel` hareketi yazar. Bunlar domain olayıyla aynı işlemde `LeaveAccount`'a yazılır (kural 7). Kapanmış hesaba yazılmaz.
- **Yol izni (kullanıcı kararı):** yıllık izin talebinde `TravelDays`. Politika izin veriyorsa (`AllowTravelDays`) en fazla `MaxTravelDays` gündür. İzin bitişinden sonraki takvim günleridir; çakışma denetiminde sayılır, bakiyeden düşmez.

**Doğrulama (§6):** Kurallar sırayla denetlenir; ilk hata döner, uyarılar kod ve metin olarak listelenir.
1. İstihdam iznin tüm aralığında açıktır (işe girişten sonra, çıkıştan önce).
2. Tür aktiftir, istihdamın şirketinde geçerlidir ve cinsiyet kısıtına uyar. Düzenleme türleri (SUT) ve yol izni türü (YOL) talep edilemez. Yarım gün yalnız birimi yarım gün olan türde ve yıllıkta politika izin veriyorsa (`AllowHalfDay`) seçilebilir.
3. Geçmiş tarih yalnız İK adına talepte ve türün `AllowPastDates` ayarı açıksa girilebilir; yıllık izinde ayrıca politikanın `AllowPastDatesForHr` ayarı gerekir. Geçmiş kayıt bu kuralı atlar.
4. Süre 0'dan büyüktür (L2 hesaplayıcısı). Talep başına sınır tür sürümünden, yıllıkta ayrıca politikadan gelir. Yıllık sınır, ilk günün takvim yılındaki aynı istihdam ve türün bekleyen ve onaylı talepleriyle birlikte sayılır.
5. Aynı personelin (tüm istihdamları) bekleyen, iadeli veya onaylı başka talebiyle çakışmaz (E13). Biri sabah biten, diğeri aynı gün öğleden sonra başlayan talepler ve aynı gündeki ayrık saatler çakışmaz.
6. Bildirim süresi tür sürümünden, yoksa yıllıkta politikadan gelir. Sağlanmazsa çalışana hata, İK'ya uyarıdır.
7. Bakiyeden düşen türde: bakiye − bekleyen − gün ≥ −avans. Avans, politikanın iznin ilk günündeki sürümünden gelir (E1, E10, E10b).
8. Belge isteyen türde (kullanıcı kararı) gönderirken en az bir ek gerekir. Çalışana hata, İK adına talepte ve geçmiş kayıtta uyarıdır; taslakta ve önizlemede uyarıdır.
9. 10 gün kuralı: Yıllık talep 10 günden kısaysa, aynı takvim yılında 10 gün ve üzeri yıllık talep yoksa ve kullanılabilir bakiye talepten önce 10 gün ve üzeriyken talepten sonra 10 günün altına düşüyorsa (10 günlük parça artık kullanılamaz), `Warn` ayarında uyarı verilir. `Enforce` ayarında bu çalışana hata, İK'ya uyarıdır.

**Onay:**
- Belge türleri: `hr.leave-request` ve `hr.leave-cancellation`. Olgular: `leaveTypeCode`, `days`, `requesterEmployeeId`, `orgUnitId`, `branchId`, `companyId`, `isPastDate`. Tanım yoksa talep otomatik onaylanmaz.
- Varsayılan akışlar (§2.8): talepte 1. adım `line-manager(1)`, 2. adım "gün > 5 veya tür UCRETSIZ" koşuluyla `role(hr-specialist)`. İptalde `line-manager(1)`. İade edilebilir.
- Onay istemeyen tür (`RequiresApproval = false`) gönderilince hemen onaylanır.
- `ApprovalRequestCompleted` talebi onaylar ya da reddeder; `ApprovalRequestReturned` iade eder. Kapanmış talebe gelen olay yok sayılır.
- Bekleyen ya da iadeli talebin iptali `ApprovalCancellationRequested` yayınlar.
- İptal talebi onaylı ve başlamamış talepte açılır, `hr.leave-cancellation` belge türüyle ve aynı belge kimliğiyle gider. Onay motoru tamamlanmış talebi yeniden başlatmadığı için bir talepte bir kez açılabilir. Reddedilirse talep onaylı kalır; İK doğrudan iptal edebilir.
- Talep edenin kendisi olmadığı (İK adına) taleplerde akışın talep edeni İK kullanıcısı, olgulardaki ve çözücülerdeki personel izinli kişidir.

**İK işlemleri:**
- Adına talep (`hr.leave.requests.manage`): taslaksız oluşturulup gönderilir.
- Geçmiş kayıt (`hr.leave.requests.record`): akışsız onaylı kayıttır. Geçmiş tarih serbesttir; bildirim süresi, belge ve 10 gün kuralı uyarıdır.
- İptal (`hr.leave.requests.manage`): onaylı ve başlamamış talebi akışsız iptal eder, açık iptal talebinin akışı da kapanır.
- Erken dönüş (`hr.leave.requests.manage`): başlamış onaylı talepte yeni son gün girilir. Süre yeniden hesaplanır, fark `RequestCancel` olarak iade edilir. İlk son gün saklanır.
- Toplu izin (`hr.leave.bulk`): Personeller ya da bir birim (alt birimleriyle), tür, tarihler ve açıklama seçilir. Önizleme her kişi için süreyi, talep sonrası bakiyeyi, hatayı ve uyarıları döner. Oluşturma, hatasız kişiler için `Bulk` kaynaklı onaylı kayıt açar. Her kişi ayrı komut ve işlemdir (kural 7); biri hata verse diğerleri sürer, sonuç kişi başına döner.

**Yetkiler:**
- `hr.leave.requests.read`: kapsamdaki talepler ve detayları. Yönetici rolü bunu ekip kapsamıyla (`ManagedTeam`) alır.
- `hr.leave.requests.manage`, `hr.leave.requests.record`, `hr.leave.bulk`. `hr.leave.*` deseniyle İK Yöneticisi ve İK Uzmanı'na geçer.
- Self servis: `hr.self.leave.read` (bakiyelerim, taleplerim) ve `hr.self.leave.request` (taslak, düzeltme, gönderme, iptal, iptal talebi, önizleme, ekler). `hr.self.*` deseniyle Çalışan rolüne geçer. D42'de anılan çalışanın kendi süre önizlemesi bu önizlemedir.

**Ekler (D31):** Sahip türü `hr.leave_request`. Talep eden, talep kapanmadıkça ek yükler; taslak ve iadeli talepte siler. `hr.leave.requests.manage` sahibi talep reddedilmedikçe ve iptal edilmedikçe yükler ve siler. Talebi görebilen ekleri görür.

**Bakiye:** Bekleyen gün, aynı bakiyeden düşen `Pending` ve `Returned` taleplerin günüdür. Kullanılabilir = bakiye − bekleyen.

**Bildirimler (D32):** `hr.leave_request_submitted` (talep edene ve personele), `hr.leave_request_completed` (onay ya da ret; talep edene, personele ve vekile), `hr.leave_request_returned` (talep edene), `hr.leave_cancellation_completed` (iptal talebinin sonucu ya da İK iptali; talep edene ve personele), `hr.leave_reminder` (izin başlamadan bir gün önce personele ve 1. seviye yöneticisine).

**Hatırlatma işi (`hr.leave-reminders`, her gün 06:00 UTC):** Şirketin saat dilimine göre yarın başlayan onaylı talepler için bir kez gönderilir.

**Uygulama notları (L4a):**
- Bildirimde personelin kendi kullanıcısı talebi "İzinlerim"de (`/hr/me/leave?request={id}`), diğerleri İK ekranında (`/hr/leave-requests/{id}`) açar. Onay kutusundaki bağlantı İK ekranıdır; yöneticiler ekip kapsamıyla `hr.leave.requests.read` taşır.
- Uç noktalar: çalışan `/api/hr/me/leave-requests` (liste, detay, önizleme, oluştur, düzelt, gönder, iptal, iptal talebi, ekler) ve `/api/hr/me/leave-balances`; İK `/api/hr/leave-requests` (liste, detay, önizleme, adına talep, `records` ve önizlemesi, iptal, erken dönüş, `bulk` ve önizlemesi, ekler).
- Toplu izin uç noktası önce önizlemeyi çalıştırır, sonra sorunsuz her kişi için ayrı komut gönderir; yanıt kişi başına oluşan talebi ya da sorunu döner. En fazla 500 kişi.
- Bakiye sorgusu ve "Bakiyelerim" aynı okuyucuyu kullanır; bekleyen gün artık taleplerden gelir.

**Uygulama notları (L4b):**
- Menü: "İzinlerim" (`/hr/me/leave`, `hr.self.leave.read`) ve "İzin talepleri" (`/hr/leave-requests`, `hr.leave.requests.read`). Onay kutusundaki bağlantı `/hr/leave-requests/{id}` talebi aynı sayfada açar; bildirimdeki `/hr/me/leave?request={id}` çalışanın talebini açar.
- İzinlerim: bakiye kartları, taleplerim (duruma göre süzülür) ve talep detayı (bilgiler, ekler, onay ve iptal zaman çizelgeleri, düzelt, gönder, iptal, iptal talebi).
- Talep formu türün yapısına göre alan gösterir: sabit süreli türde bitiş yerine olay tarihi, saatlik türde tarih ve saatler, yarım günlük türde "öğleden sonra başla / öğlene kadar", yıllıkta yol izni. Form yazıldıkça önizleme (süre, talep öncesi ve sonrası bakiye, sorun, uyarılar, onaylayacaklar) yenilenir; sorun varken "Gönder" kapalıdır.
- Eklenen dosyalar talep gönderilmeden önce yüklenir: çalışanın talebi önce taslak kaydedilir, dosyalar yüklenir, sonra gönderilir. Böylece belge isteyen tür belgesiyle gider. Gönderim başarısız olursa aynı taslak düzeltilir, ikinci taslak açılmaz.
- İK sayfası: kapsamdaki talepler (arama, durum, tarih aralığı), "Personel adına talep", "Geçmiş izin kaydet" ve "Toplu izin" (yetkiye göre). İK detayında iptal (onaylıda gerekçe zorunlu), erken dönüş ve ekler.
- Toplu izin: şirket, tür, personel listesi ya da birim (alt birimlerle), tarih aralığı ve açıklama. Önce önizlenir; "Onaylı kaydet" sorunsuz kişiler için kaydeder ve kişi başına sonucu gösterir.
- Çalışanların İK listelerine yetkisi olmadığından iki uç nokta eklendi: `GET /api/hr/me/leave-types` (talep edilebilir türler, form bilgileriyle) ve `GET /api/hr/me/colleagues?search=` (vekil seçimi; çalışanın şirketlerinde çalışanların yalnızca adı, en fazla 20). İK formu türleri `GET /api/hr/leave-requests/leave-types?employmentId=` ile alır.

## D45 — İzin takvimi

**Bağlam:** L5, kapsamdaki personelin izinlerini tatillerle birlikte takvimde gösterir ve aynı birimde aynı gün çok kişi izindeyse uyarır. Görev küçük olduğu için tek PR'dır (backend ve web).

**Uç nokta ve kapsam:**
- `GET /api/hr/leave/calendar?from=&to=` (`hr.leave.calendar.read`) en fazla 100 günlük aralık alır. Ay görünümünün baştaki ve sondaki haftaları ya da bir çeyrek bu sınıra sığar.
- Veri kapsamı personel listesiyle aynıdır (D38). Yönetici rolü yetkiyi ekip kapsamıyla (`ManagedTeam`) aldığı için yalnızca astlarını görür (L5 kabul kriteri).
- Süzgeçler kapsamı daraltır: şirket, birim (varsayılan olarak alt birimler dahil) ve "Ekibim". "Ekibim", kullanıcının pozisyonlarının altındaki kişileri ve yönetici ataması kullanıcının pozisyonunu gösteren kişileri seçer; böylece geniş kapsamlı bir İK kullanıcısı da kendi ekibine bakabilir.
- Onaylı izinler (iptal talebi bekleyenler dahil) her zaman gösterilir. Onay bekleyen ve iade edilmiş talepler varsayılan olarak gösterilir, `includePending=false` ile gizlenir. Yol izni günleri iznin parçası olarak gösterilir.
- Personelin birimi, bugünkü görevinin pozisyonunun birimidir (personel listesindeki gibi).

**Tatil katmanı:** Seçilen şirketin, şirket seçilmemişse gösterilen ilk personelin şirketinin çalışma takviminden (Platform `IWorkingCalendar`) gelir. Yarım gün tatil işaretlenir.

**Kalabalık gün uyarısı:**
- Ayar `hr.leave.calendar_threshold` (tam sayı, şirket ezebilir). Varsayılanı 0'dır, yani uyarı kapalıdır. Birim büyüklükleri çok farklı olduğu için tek bir varsayılan sayı yanlış uyarı üretirdi; İK ayarı kendi açar.
- Bir birimde bir günde izinde olan kişi sayısı eşiği aşarsa (gösterilen izinlerle; onay bekleyenler dahilse onlar da sayılır) o gün ve birim uyarı olarak döner.

**Web:**
- İK → "İzin takvimi" (`/hr/leave-calendar`): FullCalendar ay ve liste görünümü, türe (kategori) göre renk, onay bekleyen izin soluk ve "onay bekliyor" notlu, tatiller arka plan günü. Talepleri görebilen kullanıcı izne tıklayınca talep detayına gider.
- Paketler: `@fullcalendar/core`, `react`, `daygrid`, `list` 6.1.20 (MIT, D19'daki "yalnızca MIT çekirdek paketleri"). 7.x paket yapısını değiştirip ek bir `temporal-polyfill` bağımlılığı istediği için olgun 6.1 sürümü seçildi.

## D46 — İzin raporları ve belgeleri

**Bağlam:** L6, izin raporlarını (Excel'e aktarılabilir) ve izin belgelerini (P12 altyapısıyla PDF) kurar. Diğer izin görevleri gibi iki PR'dır: L6a backend, L6b web.

**Yetki:** `hr.leave.reports.read` raporları görmeyi ve Excel'e aktarmayı verir; `hr.leave.*` deseniyle İK Yöneticisi ve İK Uzmanı'na geçer. Raporlar personel listesindeki veri kapsamıyla süzülür (D38). Ayrı bir dışa aktarma yetkisi yoktur: raporu gören dosyasını da alır, dışa aktarma audit'e yazılır (D35).

**Raporlar (`/api/hr/leave/reports/...`, hepsi sayfalı, sıralanabilir ve `/export` ile Excel):**
- **Bakiye** (`balances?asOf=`): her istihdamın her bakiyesi için o güne kadarki hareketlerden kazanılan, kullanılan ve kalan. O günden önce kapanmış bakiyeler gösterilmez. Şirket, tür ve ad/sicil araması süzer.
- **Kullanım** (`usage?from=&to=`): dönemde başlayan onaylı izinler, kişi ve tür bazında talep sayısı ve gün.
- **Yaklaşan izinler** (`upcoming`): bugünden (ya da verilen günden) itibaren en fazla 366 gün içinde başlayan onaylı izinler; istenirse onay bekleyenler de.
- **Uzun süredir izin kullanmayanlar** (`long-time-no-leave?months=`, varsayılan 12): o süreden eski açık istihdamlardan, bakiyeden düşen son izni bu süreden önce biten ya da hiç izin almamış olanlar, şu anki bakiyeleriyle. "İzin" bakiyeden düşen (yıllık) izindir; mazeret ve idari izinler sayılmaz.

**Belgeler (D36 belge türleri, yerleşik şablonlu; kiracı Belge şablonları ekranından kendi şablonunu yazabilir):**
- `hr.leave_record` — Yıllık izin kayıt belgesi: personel, sicil, işe giriş, kıdem başlangıcı ve tamamlanan kıdem yılı, unvan, birim; hak edişler (tarih, hareket, açıklama, gün), kullanılan yıllık izinler (talep no, başlangıç, bitiş, yol izni, gün), toplamlar ve bugünkü bakiye; işçi ve işveren imza alanları. `GET /api/hr/employees/{id}/employments/{employmentId}/leave-record` (`hr.leave.balances.read`, şirket kapsamı).
- `hr.leave_form` — İzin talep formu: talep bilgileri, personel, talep eden, yönetici ve İK imza alanları. `GET /api/hr/leave-requests/{id}/form` (`hr.leave.requests.read`) ve çalışanın kendi talebi için `GET /api/hr/me/leave-requests/{id}/form` (`hr.self.leave.read`).
- Modeldeki tarih ve sayılar belgenin dilinde metin olarak hazırlanır (`d.MM.yyyy`, `3,5`). Etiketler `hr.documents.{tür}.texts.*` anahtarlarından gelir; şablonda metin yoktur.
- Dosya adı belgenin dilindeki `file_name` metninden gelir (sicil ya da talep numarasıyla).
- Entegrasyon testlerinde HR, PDF dönüştürücüsünü sayfanın HTML'ini döndüren bir sahte ile değiştirir; belgenin içeriği HTML'den kontrol edilir. Gerçek PDF üretimi Platform'un Gotenberg'li testlerinde doğrulanır (D36).

**Uygulama notları (L6b):**
- İK → "İzin raporları" (`/hr/leave-reports`, `hr.leave.reports.read`) dört sekmedir; açık sekme adreste tutulur (`?tab=`). Bakiye sekmesi bugünü, kullanım sekmesi yılın başından bugüne kadarki dönemi, yaklaşan izinler 30 günü, izin kullanmayanlar 12 ayı açar. Her sekme şirket süzgeci, sütun seçici ve Excel düğmesiyle gelir; personeli görebilen kişi adına tıklayıp karta gider.
- Personel kartı İzinler sekmesinde "Kayıt belgesi" düğmesi seçili istihdamın yıllık izin kayıt belgesini indirir.
- Talep detayında (taslak dışında) "Talep formu" düğmesi formu indirir; çalışan kendi talebinin, İK kapsamındaki talebin formunu alır.

## D47 — İşe alım ayarları ve aday e-postaları

**Bağlam:** R1, işe alım modülünün ayarlarını kurar. Kullanıcı kararları (2026-10-25): adaylara giden e-postalar Platform'un e-posta altyapısıyla gider (dış alıcı); aday verisinin varsayılan saklama süresi 24 aydır; R1 tek PR'dır.

**Yetkiler:** `recruitment.settings.read` ve `recruitment.settings.manage`; `recruitment.*` deseniyle İK Yöneticisi ve İşe Alım Uzmanı rollerine geçer. Aşama şablonları ve listeler kiracının kataloglarıdır, değiştirmek Tenant kapsamı ister (unvan kataloğu ve izin türleri gibi, D37, D41). KVKK metinleri şirket bazındadır: şirketin metni şirket kapsamıyla, tüm şirketlerin metni Tenant kapsamıyla yayınlanır.

**Aşama şablonu (`StageTemplate`, aggregate):**
- Ad (çok dilli), sıralı aşamalar, varsayılan ve aktiflik. Aşama: kalıcı kimlik, ad (çok dilli), tip ve süre (SLA, gün).
- Tipler: Applied, Screening, Interview, Assessment, Reference, Offer, Hired, Rejected, Withdrawn.
- Kurallar: ilk aşama Applied'dır ve tektir; Hired, Rejected ve Withdrawn birer kez bulunur ve listenin sonundadır. Son aşamalarda süre girilmez, diğerlerinde 1–365 gündür. Aşamalar satırın jsonb kolonundadır.
- Bir şablon kiracının varsayılanıdır; yeni varsayılan eskisini aynı işlemde düşürür (domain olayı). Varsayılan pasif yapılamaz.
- İlan (R3) şablonun kopyasını alır; şablonun sonraki değişiklikleri açık ilanları etkilemez. Aşama kimlikleri kopyada da aynıdır.
- Seed: "Standart işe alım" (Başvuru, Ön eleme, İK mülakatı, Yönetici mülakatı, Teklif, İşe alındı, Reddedildi, Geri çekildi).

**Listeler (`RecruitmentLookup`):** Tek tablo, tür alanıyla: red nedenleri, çekilme nedenleri, aday kaynakları, etiketler. Kod listesinde tekildir ve değişmez; ad çok dillidir; sıra ve aktiflik vardır. Pasif kayıt onu kullanan kayıtlarda kalır. Seed: yaygın red ve çekilme nedenleri ile kaynaklar (kariyer sayfası, LinkedIn, iş ilanı sitesi, çalışan referansı, danışmanlık firması, iç aday).

**KVKK metinleri (`ConsentText`):**
- Türler: aydınlatma metni ve açık rıza metni (adayın CV'sinin havuzda başka pozisyonlar için saklanması).
- Her yayın tür × şirket (ya da tüm şirketler) × dil için yeni bir sürümdür (revizyon 1, 2, …). Yayınlanmış sürüm değişmez; adayın onayı gördüğü sürümle kaydedilir (R3).
- Adayın göreceği metin: önce adayın dili, sonra şirketin metni, sonra tüm şirketlerinki, en yeni revizyon. Hiç yayın yoksa yerleşik metin (revizyon 0) kullanılır. Yerleşik aydınlatma metni, kiracıda yapay zeka CV analizi açıksa yapay zeka ve yurt dışına aktarım maddesini içerir (`docs/domain/yapay-zeka.md` §10.3). Yerleşik metin seed edilmez; çalışma anında üretilir, böylece yapay zeka ayarı değişince güncel kalır.

**Aday e-postaları (kullanıcı kararı):**
- Platform'a `ExternalEmailRequested` olayı eklendi (Platform.Contracts): tür, adres, ad, dil ve argümanlar. Platform bunu e-posta kuyruğuna yazar ve mevcut yeniden deneme ile gönderir. Kiracıda ya da şirkette e-postalar kapalıysa gönderilmez.
- Bildirim türlerine `External` işareti eklendi: dış alıcılı türler zile düşmez, kullanıcı tercihlerinde görünmez ve kullanıcılara gönderilemez. Konu ve metin türün `{modül}.notifications.{ad}.title/body` metinlerinden gelir; kiracı Sistem → Bildirim şablonları ekranında şirket ve dil bazında kendi şablonunu yazar (D32 seçim sırası).
- İşe alımın türleri: `recruitment.application_received`, `recruitment.interview_invitation`, `recruitment.application_rejected`, `recruitment.offer_sent`. Gönderimleri R3–R6'da gelir. Takvim daveti (R5) ve teklif PDF'i (R6) için olaya ek desteği o görevlerde eklenecek.

**Saklama:** `recruitment.retention_months` ayarı (varsayılan 24, şirket ezebilir, 1–120). Anonimleştirme işi R8'de gelir.

**Yapı taşı:** jsonb kolonlarındaki çok dilli adlar (`LocalizedString`) kendi kolonlarıyla aynı biçimde (`{"tr-TR": "…"}`) yazılır.

**Web:** İşe alım → "İşe alım ayarları" (`/recruitment/settings`, `recruitment.settings.read`): aşama şablonları (aşama ekleme, sıralama, tip ve süre), listeler, KVKK metinleri (tür, şirket ve dil seçilerek geçerli metin, sürüm geçmişi ve yeni sürüm yayınlama) ve aday e-postalarının ve saklama süresinin hangi ekranda ayarlandığını gösteren sekme.

## D48 — Personel talebi

**Bağlam:** R2, işe alımın ilk belgesi olan personel talebini (`JobRequisition`) getirir. Kullanıcı kararları (2026-10-25): talebi yalnızca İK ve işe alım açar (Yönetici rolüne yetki verilmez; yöneticiler onaycıdır); boş kadro hesabında açık talepler de düşülür; R2 tek PR'dır.

**Yetkiler:** `recruitment.requisitions.read` (şirket kapsamındaki talepleri görme) ve `recruitment.requisitions.manage` (açma, düzeltme, gönderme, iptal ve maaş aralığını görme). `recruitment.*` deseniyle İK Yöneticisi ve İşe Alım Uzmanı rollerine geçer. Kapsam şirket bazındadır: Tenant ya da talebin şirketini kapsayan Company kapsamı gerekir; kapsam dışındaki talep 404 döner.

**Talep:**
- Numara P5 `recruitment.requisition` (`PT-{yyyy}-{seq:5}`), şirketin saat dilimindeki güne göre.
- Pozisyon: mevcut bir pozisyon (birim pozisyondan alınır) ya da bir birim için yeni pozisyon önerisi (ad ve seviye). Kişi sayısı 1–99. Neden: yeni kadro ya da ayrılan yerine (yerine alınacak kişi şirkette bir istihdamı olan personeldir). Çalışma şekli, istenen başlama tarihi, brüt aylık maaş aralığı ve para birimi (`[Sensitive]`), gerekçe ve işe alan yönetici (aktif kullanıcı).
- Durumlar: Taslak → Onay bekliyor → (İade edildi → Onay bekliyor) → Onaylandı / Reddedildi; İptal edildi; Dolduruldu (R7'de işe alımlar `FilledCount`'u artırınca).
- Taslak ve iade edilmiş talep düzeltilir ve gönderilir; karar verilmemiş ya da onaylanmış ve dolmamış talep iptal edilir. Açık onay süreci iptalde `ApprovalCancellationRequested` ile kapanır.

**Boş kadro (kullanıcı kararı):** Mevcut pozisyon için istenebilecek kişi sayısı = pozisyonun bugünkü boş kadrosu (norm − dolu; pasif pozisyonda 0) − aynı pozisyondaki açık taleplerin (Onay bekliyor, İade edildi, Onaylandı) kalan kişi sayısı (`Headcount − FilledCount`). Kontrol kayıtta ve gönderimde yapılır (`recruitment.requisitions.errors.no_vacancy`, kalan sayıyla). Yeni pozisyon önerisi kadro kontrolüne girmez.

**İK'dan okuma (kural 3):** HR.Contracts'a salt-okunur sorgular eklendi: `IOrgQuery.GetUnitsAsync`, `GetPositionsAsync` (birim ve alt birimleri), `GetPositionAsync` (norm, dolu ve boş kadroyla) ve `IEmployeeLookup` (şirketin personeli, yalnızca ad ve sicil numarası). Bu sorgular kapsam uygulamaz; işe alım kendi yetki ve kapsamıyla erişimi denetler.

**Onay:** Belge türü `recruitment.requisition`. Olgular: `companyId`, `orgUnitId`, `headcount`, `reason` (NewHeadcount, Replacement), `isNewPosition`. Tanım yoksa varsayılan akış: hiyerarşik yönetici (1. seviye, talep edenin pozisyonuna göre) ve İK Yöneticisi rolü; iki adımda da iade açıktır. Tanım yoksa otomatik onay yoktur. Sonuç ve iade inbox ile talebe işlenir; talep edene bildirim gider (`recruitment.requisition_completed`, `recruitment.requisition_returned`).

**Erişim ve maaş:** Talebi şirket kapsamındaki okuma yetkisi olanlar ve onaycıları görür (onaycılar için okuma yetkisi de gerekir; yetkisiz onaycı talebi onay görevindeki özetten değerlendirir, belgeye görev üzerinden tam erişim (yetki-ve-onay kural 5) sonraki bir işte ele alınacak). Maaş aralığı yalnızca yönetme yetkisi olanlara ve onaycılara gösterilir; görülmesi audit'e `Read` olarak yazılır. Onay zaman çizelgesini talep eden ve şirket kapsamındaki okuyucular da görür.

**İlan:** Onaylanmış ve dolmamış talepte `CanCreateOpening` true döner; web detayında "ilan açılabilir" bilgisi çıkar. "İlan oluştur" düğmesi ve ilanın talebe bağlanması R3'te gelir.

**Web:** İşe alım → "Personel talepleri" (`/recruitment/requisitions`, `/recruitment/requisitions/:id` onay kutusunun bağlantısı): durum ve metin süzgeçli liste, taslak kaydetme ve onaya gönderme seçenekli form (pozisyonlar açık taleplerden sonra kalan boş kadroyla listelenir), onay zaman çizelgeli detay ve iptal.

## D49 — İş ilanları ve kariyer sayfası

**Bağlam:** R3, ilanları (`JobOpening`) ve herkese açık kariyer sayfasını getirir. Kullanıcı kararları (2026-10-25):
- R3 iki PR'dır: R3a ilanlar (backend ve web), R3b kariyer sayfası ve başvuru.
- İlan metinleri HTML olarak saklanır ve sunucuda temizlenir.
- Aday kiracıda e-posta adresiyle tektir.
- Kiracı başına tek kariyer sayfası vardır.

**Yetkiler:** `recruitment.openings.read` (şirket kapsamındaki ilanları görme) ve `recruitment.openings.manage` (açma, düzeltme, yayınlama, bekletme ve kapatma). `recruitment.*` deseniyle İK Yöneticisi ve İşe Alım Uzmanı rollerine geçer. Kapsam talepteki gibi şirket bazındadır; kapsam dışındaki ilan 404 döner.

**İlan (R3a):**
- Alanlar:
  - şirket ve isteğe bağlı onaylanmış personel talebi
  - başlık, açıklama ve aranan nitelikler (zengin metin)
  - şube, çalışma şekli (işyerinde, uzaktan, hibrit), istihdam tipi (talepteki tipler), açık kadro sayısı (1–99)
  - görünürlük ve yayın tarihleri (başlangıç, bitiş)
- **Görünürlük:** İç ilan yalnızca çalışanlara görünür. Dış ilan kariyer sayfasında ve çalışanlara görünür.
- **Talepten ilan:** Talep onaylanmış olmalı, aynı şirkette olmalı ve ilanın kadro sayısı talebin kalan kişi sayısını (`Headcount − FilledCount`) aşmamalı. Bir talepten birden çok ilan açılabilir (örn. farklı kanallar). Talep ekranındaki "İlan oluştur" formu talebin bilgileriyle doldurur.
- **Aşamalar:** İlan, seçilen aşama şablonunun (varsayılan: kiracının varsayılanı) aşamalarını kopyalar (D47). Şablon yalnızca taslakta değiştirilebilir; yayından sonra aşamalar sabittir. Aşamaların ilan içinde düzenlenmesi R4a'da geldi (D50).
- **İşe alım ekibi:** işe alan yönetici (zorunlu), işe alım uzmanları ve mülakatçılar (aktif kullanıcılar). Ekibin adaylara erişimi R4–R5'te kullanılır.
- **Başvuru formu:**
  - Standart alanlar: ad, soyad ve e-posta her zaman istenir. Telefon ve CV'nin zorunluluğu ile ön yazı sorulup sorulmayacağı ilanda seçilir.
  - En fazla 20 özel soru. Tipler: kısa metin, uzun metin, evet/hayır, tek seçim, çok seçim, sayı.
  - Evet/hayır ve seçimli sorular eleme sorusu olabilir: kabul edilen cevaplar işaretlenir. Bu cevapların dışında kalan başvuru "elemeye takıldı" olarak işaretlenir (R3b); başvuru yine alınır, ne yapılacağına işe alım ekibi karar verir (R4).
- **Durumlar:** Taslak → Yayında ⇄ Beklemede → Kapalı. Taslak ve yayındaki ya da bekleyen ilan düzeltilebilir; kapalı ilan değişmez.
  - Yayınlama: talepli ilanda talep hâlâ onaylı olmalı. Bitiş tarihi geçmiş olmamalı. Başlangıç tarihi verilmemişse şirketin bugünü yazılır.
  - Kariyer sayfasında yalnızca yayındaki ve tarih aralığı içindeki dış ilanlar görünür.
- **Zengin metin:**
  - TipTap (MIT) HTML üretir. Sunucu, `IRichTextSanitizer` ile kayıtta izinli etiketler dışındaki her şeyi siler.
    - İzinli etiketler: paragraf, satır sonu, kalın, italik, altı çizili, üstü çizili, başlık 2–4, listeler, alıntı, bağlantı (`http`, `https`, `mailto`).
    - Bağlantılar `rel="noopener noreferrer nofollow"` ve `target="_blank"` ile yazılır.
  - Temizlik kütüphanesi HtmlSanitizer'dır (Ganss.Xss, MIT; bağımlılıkları AngleSharp ve AngleSharp.Css, MIT).
  - Temizlenmiş HTML kariyer sayfasında, e-postada ve PDF'te doğrudan kullanılabilir.
  - Boş içerik (yalnızca boş paragraflar) boş sayılır.

**Kariyer sayfası ve başvuru (R3b):**
- Herkese açık API `/api/public/{tenantCode}/careers/**` (D20).
  - İçerik: ilan listesi (şirket, şube ve çalışma şekli süzgeçli, şirket adı ve logosuyla), ilan detayı ve başvuru.
  - Koruma: IP başına rate limit ve honeypot alanı. CV yalnızca PDF, DOC ya da DOCX olabilir (en fazla 5 MB).
- Sayfa `/careers/:tenant` kiracının tüm şirketlerinin dış ilanlarını listeler.
- **Aday:** Kiracıda e-posta adresiyle tektir (kullanıcı kararı). Başvuruda aday yoksa oluşturulur; varsa iletişim bilgileri ve CV'si güncellenir.
  - Aynı adayın aynı ilana ikinci başvurusu reddedilir.
- **Başvuru:** İlanın ilk (Applied) aşamasında başlar. Soruların cevaplarını ve eleme sonucunu taşır.
- **KVKK onayı:** Başvuru, aydınlatma metninin onaylandığını işaretlemeden reddedilir. Onay, adayın gördüğü sürümle (D47) kaydedilir.
  - Açık rıza (havuzda saklama) isteğe bağlıdır.
- **E-posta:** "Başvurunuz alındı" e-postası `ExternalEmailRequested` (`recruitment.application_received`) ile gider (D47).
- **İç başvuru:** Çalışan self servisten iç ya da dış ilana başvurur. Başvuru "iç aday" işaretlidir; aday bilgisi personel kaydından gelir.

**R3b uygulaması (kullanıcı kararı: tek PR):**
- **Uç noktalar:** `GET /api/public/{tenantCode}/careers` (şirketler ve açık dış ilanlar), `GET …/careers/openings/{id}` (form ve KVKK metinleri; eleme sorularının kabul edilen cevapları gösterilmez), `GET …/careers/companies/{companyId}/logo`, `POST …/careers/openings/{id}/applications`. Başvuru çok parçalı formdur: başvuru `application` alanında JSON, CV `cv` dosyasıdır. Başarılı başvuru 202 döner ve gövdesi yoktur.
- **Koruma:** Grup `public` istek sınırı politikasını kullanır (`RateLimiting:Public:PermitLimit`, varsayılan IP başına dakikada 30). Bal küpü alanı `website`'tır: doluysa başvuru 202 ile kabul edilmiş gibi görünür ama hiçbir şey kaydedilmez. İlan yayında değilse, tarih aralığı dışındaysa ya da iç ilansa 404 döner.
- **CV:** Yalnızca PDF ve DOCX, en fazla 5 MB. Eski DOC biçimi içerik denetiminden geçirilemediği ve makro taşıyabildiği için kabul edilmez (CV analizi de aynı biçimleri alır). CV adayın dosyası olarak saklanır (`recruitment.candidate`); başvuru o anki CV'yi gösterir.
- **KVKK sürümü:** Form, gösterilen aydınlatma ve açık rıza metinlerinin revizyonlarını geri gönderir. Metin bu arada değiştiyse başvuru `recruitment.applications.errors.consent_changed` ile reddedilir; aday sayfayı yenileyip güncel metni onaylar.
- **Kural 7:** Komutun aggregate'i başvurudur (`JobApplication`). Aday, `ApplicationSubmitted` domain olayıyla aynı işlemde oluşturulur ya da güncellenir.
- **Logo:** Platform.Contracts'a `ICompanyLogos` eklendi. Logo yalnızca kariyer sayfasında açık ilanı olan şirket için verilir; böylece sayfa kiracının bütün şirketlerini açığa çıkarmaz.
- **İç başvuru:** Yetki `recruitment.self.apply`. Çalışan rolünün varsayılan desenine `recruitment.self.*` eklendi; İK Yöneticisi ve İşe Alım Uzmanı `recruitment.*` ile alır. Uç noktalar `/api/recruitment/me/openings`, `/me/openings/{id}`, `/me/openings/{id}/applications` ve `/me/applications`.
  - Çalışan kiracının tüm şirketlerindeki açık iç ve dış ilanları görür.
  - Ad ve soyad personel kaydından gelir. E-posta kullanıcının e-postasıdır; yoksa personel kaydının birincil e-postası kullanılır. Telefon formdan, boşsa personel kaydından gelir. HR.Contracts'a `IEmployeeLookup.GetContactAsync` eklendi.
  - Kullanıcı bir personel kaydına bağlı değilse başvuru `recruitment.applications.errors.not_employee` ile reddedilir.
- **Web:** Kariyer sayfası `/careers/:tenant` ve ilan sayfası `/careers/:tenant/openings/:id` girişsizdir ve uygulama kabuğu olmadan açılır. Çalışanlar İşe alım → "Açık pozisyonlar" (`/recruitment/my-openings`) ekranında ilanları ve kendi başvurularını görür.

## D50 — Aday havuzu ve başvuru süreci

**Bağlam:** R4, başvuruları ilanın aşamalarında yürütür ve aday havuzunu getirir. Kullanıcı kararları (2026-10-27):
- R4 iki dikey PR'dır: R4a süreç (aşamalar, geçmiş, ret, geri çekilme, kanban), R4b aday ve havuz (aday profili, sürümlü CV'ler, notlar, etiketler, tam metin arama, birleştirme, başka ilana ekleme, aday kartı). A3 (CV analizi) R4b'den sonra ayrı PR'dır.
- İşe alım ekibi kendi ilanının başvurularını yetkisi olmadan görür; aşama değiştirmek yetki ister.
- Tekrar eden aday: aynı e-posta engellenir ve o aday önerilir; aynı telefon ya da ad soyad "olası tekrar" olarak gösterilir; birleştirmeyi kullanıcı başlatır (R4b).
- `@dnd-kit/core`'un bağımlılığı `tslib` için 0BSD izinli lisanslara eklendi (D19).

**Yetkiler (R4a):** `recruitment.applications.read` (şirket kapsamındaki ilanların başvuruları ve süreçleri) ve `recruitment.applications.manage` (taşıma, ret, geri çekilme, yeniden açma). `recruitment.*` deseniyle İK Yöneticisi ve İşe Alım Uzmanı rollerine geçer. Kapsam ilandaki gibi şirket bazındadır; kapsam dışındaki başvuru 404 döner.

**İşe alım ekibi (kullanıcı kararı):** `recruitment.self.hiring`, Çalışan rolüne `recruitment.self.*` deseniyle geçer. İlanın işe alan yöneticisi ve işe alım uzmanları, bu yetkiyle yalnızca o ilanın sürecini, başvurularını ve CV'lerini okur (`/api/recruitment/me/hiring/**`). Taslak ilanlar listelenmez. Mülakatçıların erişimi R5'te gelir. Ekip jsonb kolonunda olduğundan üyelik uygulamada süzülür.

**Başvuru süreci (R4a):**
- Durum ile aşama birlikte yürür: süreçteki başvuru (Active) bir çalışma aşamasındadır. İşe alındı, Reddedildi ve Geri çekildi aşamaları aynı adlı durumlara karşılık gelir.
- **Taşıma:** Süreçteki başvuru ilanın bir çalışma aşamasına ya da İşe alındı aşamasına taşınır. Ret ve geri çekilme ayrı işlemdir, çünkü neden ister.
- **Ret:** Etkin bir red nedeni (D47 listesi) ve isteğe bağlı not gerekir. Ret e-postası (`recruitment.application_rejected`, D47 şablonu) hemen ya da 1–14 gün sonra gider. Gecikmeli e-postayı 15 dakikada bir çalışan iş (`recruitment.rejection-emails`) gönderir.
- **Geri çekilme:** Etkin bir çekilme nedeni gerekir; e-posta gitmez.
- **Yeniden açma:** Reddedilmiş ya da geri çekilmiş başvuru ayrıldığı çalışma aşamasına döner; o aşama ilandan kalkmışsa Başvuru aşamasına döner. Gönderilmemiş ret e-postası iptal olur. İşe alınmış başvuru yeniden açılmaz.
- **Aşama geçmişi** (`application_stage_changes`, PLAN kabul kriteri): Her adım için önceki ve yeni aşama, adımdan sonraki durum, neden, not, yapan kullanıcı ve zaman tutulur. İlk kayıt başvurunun kendisidir. Aşama süresi (SLA) aşamaya giriş anından sayılır.
- **Eşzamanlılık:** Değişiklikler okunan sürümle gönderilir; eski sürüm 409 döner. Kanbanda bırakılan kart sürüm taşımadığından web, değiştirmeden hemen önce sürümü okur.
- Puan özeti R5'in değerlendirmeleriyle gelir.

**İlanın kendi aşamaları (D49'da R4'e bırakılan):** Kapanmamış ilanın aşamaları `PUT /api/recruitment/openings/{id}/stages` ile şablon kurallarıyla değiştirilir (`recruitment.openings.manage`). Başvuruların bulunduğu ya da süreçten ayrıldığı aşama kaldırılamaz (`recruitment.openings.errors.stage_in_use`). Şablonun sonraki değişiklikleri ilanı yine etkilemez.

**Uç noktalar (R4a):** `GET /openings/{id}/pipeline` (aşamalar, aşama başına başvuru sayısı, yönetene red ve çekilme nedenleri), `GET /applications` (ilan, aşama, durum, eleme ve metin süzgeçli sayfalı liste), `GET /applications/{id}` (cevaplar, CV, KVKK onayı, geçmiş), `GET /applications/{id}/cv` ve `POST /applications/{id}/move|reject|withdraw|reopen`. İlan listesi aşama başına başvuru sayılarını taşır.

**Web (R4a):** İlan detayında "Başvurular" düğmesi `/recruitment/openings/:id/pipeline` sayfasını açar. Sayfada kanban (@dnd-kit; ret ve geri çekilme sütununa bırakılan kart neden modalını açar) ve liste görünümü vardır. Kanban en fazla 200 başvuru gösterir; daha fazlası liste görünümündedir. Başvuru çekmecesinde iletişim, cevaplar, CV, KVKK onayı, geçmiş ve işlemler bulunur. İlan listesinde başvuru sayısı ve aşama dağılımı görünür. İlan detayında "Aşamaları düzenle" vardır. Ekip üyeleri "Açık pozisyonlar" ekranındaki "İşe alım ekibimdeki ilanlar" sekmesinden süreci salt okunur açar (`/recruitment/hiring/:id`).

**Yetkiler (R4b):** `recruitment.candidates.read` (aday havuzu ve aday kartı) ve `recruitment.candidates.manage` (aday ekleme ve düzeltme, etiketler, CV yükleme, not yazma, birleştirme). `recruitment.*` deseniyle İK Yöneticisi ve İşe Alım Uzmanı rollerine geçer. Havuz kiracı genelidir (PLAN R4): yetkinin herhangi bir kapsamı havuzu gösterir. Karttaki başvurular ise `recruitment.applications.read` kapsamındaki şirketlerle sınırlıdır. Adayı bir ilana eklemek `recruitment.applications.manage` ister ve ilanın şirketi kapsamda olmalıdır.

**Aday (R4b):**
- **Profil:** konum, mevcut unvan ve şirket, deneyim yılı (0–60), eğitim, LinkedIn ve portfolyo bağlantıları (http/https), kaynak (D47 listesi) ve referans veren personel. Referans veren, kapsamdaki bir şirketin personeli arasından seçilir.
- **Etiketler:** D47 etiket listesinden en fazla 20 etiket; jsonb kolonunda tutulur ve havuzda süzgeçtir. Her dildeki adları aramaya girer.
- **Notlar:** `candidate_notes` tablosunda tutulur; en fazla 2000 karakterdir ve notu yalnızca yazan siler. Notta anılan aktif kullanıcılara `recruitment.candidate_mentioned` bildirimi gider (zil ve e-posta, bağlantı aday kartı). Web'de anma `@ad` ile yapılır; metinden silinen ad bildirim almaz. İşe alım ekibi kendi ilanlarının adaylarına `/api/recruitment/me/hiring/candidates/{id}/notes` ile not yazar (kullanıcı kararı).
- **CV sürümleri:** CV'ler adayın dosyalarıdır (`recruitment.candidate`). Kartta tarihleriyle listelenir; en yenisi günceldir ve yeni başvurularla gider. Kartta ayrıca PDF ya da DOCX (en fazla 5 MB) yüklenebilir.
- **Rıza kayıtları:** Havuzda saklanma rızası sürüm ve tarihiyle tutulur. Her başvurunun formda onaylanan aydınlatma metni sürümü kartta görünür. İK'nın havuzdan eklediği başvurunun form onayı yoktur (`Consent` null).
- **Tam metin arama:** `search_vector`, PostgreSQL'in `turkish` sözlüğüyle saklanan, üretilmiş bir tsvector kolonudur (GIN indeksli). Ad, e-posta, profil, etiket adları, notlar ve güncel CV metnini kapsar ve `websearch_to_tsquery` ile eşlenir. Yazılırken ad ve e-postada parça arama da yapılır. Sağlayıcıya özgü sorgu Infrastructure'daki `ICandidateSearch` uygulamasındadır.
- **CV metni:** `IDocumentTextExtractor` ile beş dakikada bir çalışan bir iş (`recruitment.candidate-cv-text`) okur. Metni olmayan (taranmış) CV okundu sayılır ve tekrar denenmez. Metin en fazla 100.000 karakter tutulur.
- **Tekrar eden aday (kullanıcı kararı):** E-posta tektir. Formda ya da düzeltmede başka adayın e-postası `recruitment.candidates.errors.email_taken` ile reddedilir; web o adayı havuzda bulup "Adayı aç" önerir. Telefonun son 10 hanesi ya da ad soyadı aynı olan adaylar kartta "olası tekrar" olarak listelenir.
- **Birleştirme:** Kullanıcı başlatır; tekrar eden aday karttaki adaya katılır. Boş profil alanları doldurulur; etiketler ve notlar birleşir; daha yeni rıza ve başvuru tarihi alınır; adayın CV'si yoksa diğerininki alınır. Komutun aggregate'i hedef adaydır (kural 7). `CandidateMerged` domain olayıyla başvurular ve CV dosyaları hedefe taşınır, tekrar eden aday silinir; izi audit'te kalır. İki aday aynı ilana başvurmuşsa birleştirme reddedilir (`both_applied`).
- **Başka ilana ekleme:** Yayındaki ya da beklemedeki ilana eklenir. Form ve cevap yoktur; güncel CV ve adayın kaynağı başvuruya geçer. Başvuru Başvuru aşamasında başlar ve geçmişe ekleyen kullanıcıyla yazılır. Aynı ilana ikinci kez eklenemez.

**Uç noktalar (R4b):** `GET /candidates` (arama, etiket ve kaynak süzgeçli sayfalı liste), `POST /candidates`, `GET|PUT /candidates/{id}`, `POST /candidates/{id}/cvs`, `GET /candidates/{id}/cvs/{fileId}`, `POST|DELETE /candidates/{id}/notes[/{noteId}]`, `POST /candidates/{id}/merge`, `POST /candidates/{id}/applications`, `GET /candidates/options|referrers|mention-users`. İşe alım ekibi için: `GET /me/hiring/candidates/{id}`, not ekleme ve silme, `GET /me/hiring/mention-users`.

**Web (R4b):** İşe alım → "Aday havuzu" (`/recruitment/candidates`): arama, etiket ve kaynak süzgeçli liste ile yeni aday formu. Aday kartında (`/recruitment/candidates/:id`) profil, başvurular, notlar (@anma), CV'ler ve zaman çizelgesi sekmeleri, düzenleme, CV yükleme, ilana ekleme ve olası tekrarlar için birleştirme vardır. Başvuru çekmecesindeki "Aday kartı" düğmesi kartı açar. İşe alım ekibi kartı `/recruitment/hiring/candidates/:id` üzerinden okur ve not yazar.

## D51 — Aday kartında CV analizi

**Bağlam:** A3, A2'nin durumsuz CV analizini başvuruya bağlar ve aday formunu CV'den doldurur (`docs/domain/yapay-zeka.md` §3.2). Kullanıcı kararları (2026-10-29):
- Otomatik analiz yalnızca süreçteki ve son 30 günde gelen başvuruları kapsar. Özellik sonradan açılırsa eski birikim analiz edilmez.
- Analizi başvuruyu gören herkes (işe alım ekibi dahil) görür. Yeniden çalıştırmak `recruitment.cv.analyze` yetkisi ve başvurunun şirketini kapsayan `recruitment.applications.read` ister.

**Analiz:**
- Başvuru başına tek kayıttır (`application_cv_analyses`, anahtarı başvuru, başvuruyla birlikte silinir). İçinde A2 yanıtının JSON'u (analiz, maskeleme sayıları, kesilme, model, talimat sürümü), analiz tarihi ve isteyen kullanıcı tutulur. Başarısız çalışmada yalnızca hata kodu tutulur.
- Girdi başvurunun CV'sidir; eşleşme ilanın başlığı ve aranan nitelikleriyle (HTML'den düz metne çevrilerek) yapılır. Maskelemede adayın adı ve CV'nin ilk satırlarındaki ad kullanılır. Saklanan analiz, kimin çalıştırdığından bağımsız olarak kiracının varsayılan dilindedir. Özellik anahtarı A2 ile aynıdır (`recruitment.cv_analysis`); aylık bütçe her çağrıda geçerlidir.
- **Arka plan:** `recruitment.application-cv-analysis` işi 2 dakikada bir, kiracı başına en fazla 5 başvuru analiz eder (saatte en fazla 150). Kiracıda özellik kapalıysa hiçbir şey yapmaz. Başarısızlık da kaydedilir ve iş o başvuruyu tekrar denemez; ekip karttan yeniden çalıştırır.
- **Yeniden analiz:** `POST /api/recruitment/applications/{id}/cv-analysis` (`ai` hız sınırı). Başarısız bir yeniden analiz önceki analizi silmez.
- **Veritabanı işlemi:** Komutlar baştan bir veritabanı işlemi açtığından 30–60 saniyelik yapay zeka çağrısı bir sorgu işleyicisinde yapılır, sonuç kısa bir komutla (`SaveApplicationCvAnalysisCommand`) kaydedilir. İş her başvuru için ayrı bir sorgu gönderir.
- **Silme:** Başvuru silinince analiz de silinir. Adayın anonimleşmesinde (R8) analiz silinecektir; kabul kriterinin bu kısmı R8'de karşılanır.

**Aday formunu doldurma:** `POST /api/recruitment/candidates/cv-prefill` (`recruitment.candidates.manage`, `ai` hız sınırı). Dosya saklanmaz.
- Ad, e-posta, telefon, LinkedIn ve diğer web bağlantısı CV'den düzenli ifadelerle yerelde okunur ve sağlayıcıya gönderilmez (PLAN A3).
- Özellik açıksa unvan, şirket, deneyim yılı ve eğitim maskelenmiş metnin analizinden gelir. Analiz yapılamazsa yalnızca yerel alanlar döner ve hata kodu gösterilir.
- Web yalnızca boş alanları doldurur. CV, aday kaydedilince adaya yüklenir.

**Web:** Başvuru çekmecesinde "Özgeçmiş analizi (yapay zeka)" kartı vardır. Kart A2 sonuç görünümünü "yapay zeka önerisidir" uyarısıyla gösterir, yetkililere "Analiz et / Yeniden analiz et" sunar ve başarısız çalışmanın nedenini gösterir. Yeni aday formunda "Özgeçmişten doldur" vardır.
