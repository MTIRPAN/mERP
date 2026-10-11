# mERP — Uygulama Planı: Çekirdek + İK

## Nasıl kullanılır

- Görevleri sırayla yap. Her görev bir PR'dır. Bağımlılığı bitmemiş göreve başlama.
- Her görev üç bölümden oluşur: **Yapılacaklar**, **Kabul kriterleri**, **Boyut**.
  - Boyut: S (birkaç saat), M (1–2 gün), L (3–5 gün), XL (bölünmeli).
- Kabul kriterleri test olarak yazılır. Testi olmayan kriter "tamamlandı" sayılmaz.
- 🔍 işaretli görevlerden sonra dur ve kullanıcıya demo/inceleme iste.
- Kurallar: `CLAUDE.md`. Kararlar: `docs/DECISIONS.md`. Alan kuralları: `docs/domain/`.

## Kapsam

**Dahil:**
- Platform çekirdeği: organizasyon, kullanıcı/rol/yetki, onay motoru, çoklu dil, audit, numaralandırma, özel alanlar, dosya, bildirim, takvim, içe aktarma, belge şablonu.
- İK:
  - organizasyon yapısı, personel kartı ve yaşam döngüsü, self servis
  - izin yönetimi
  - işe alım (talep, ilan, aday, mülakat, değerlendirme, teklif, işe alım)

**Hariç (sonraki fazlar):** Bordro, puantaj/PDKS, performans, eğitim, onboarding checklist, masraf, harici takvim ve iş ilanı sitesi entegrasyonları, finans, stok, satış.

## Sıra ve paralellik

```
Faz 0 (iskelet) → Faz 1 (P1…P13) → Faz 2 (H1…H4) → Faz 3 (L1…L6)
                                                 ↘ Faz 4 (R1…R9)  [H2 + P10 bitince Faz 3 ile paralel yürüyebilir]
                                                                   → Faz 5 (X1…X6)
```

Yapay zeka görevleri (A1…A14, D24) ait oldukları görevle birlikte yapılır; bkz. "Yapay zeka" bölümü.

---

## Faz 0 — İskelet

### [x] 0.1 Repo temeli — S

**Yapılacaklar:**
- Repo dosyaları:
  - `.gitignore` (VisualStudio + Node) ve `.gitattributes` (satır sonları)
  - `.editorconfig`: C# stil ve isimlendirme kuralları
  - `global.json`: .NET 10 SDK, `rollForward: latestFeature`
- `Directory.Build.props`:
  - `net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`
  - CA1304/CA1305/CA1309/CA1311 = error
  - `InvariantGlobalization=false`
  - `RestorePackagesWithLockFile=true`
- Paket ve araç yönetimi:
  - `Directory.Packages.props`: merkezi paket yönetimi
  - `.config/dotnet-tools.json`: `dotnet-ef`
- `README.md`:
  - Gereksinimler: .NET 10 SDK, Node 22+, Docker Desktop
  - İlk çalıştırma adımları

**Kabul kriterleri:**
- Boş solution `dotnet build -warnaserror` ile derlenir.

### [x] 0.2 Solution ve proje yapısı — S

**Yapılacaklar:**
- `Merp.slnx` içinde `CLAUDE.md`'deki proje yapısı kurulur:
  - BuildingBlocks: 4 proje
  - Host: `Merp.Api`, `Merp.Migrator`
  - Platform modülü: 5 proje
  - Testler: `Merp.Testing`, `Merp.ArchitectureTests`, `Merp.Modules.Platform.UnitTests`, `Merp.Modules.Platform.IntegrationTests`
- Proje referansları mimari kurallara göre verilir.
- `docs/how-to/add-module.md` yazılır: yeni modül eklemenin adım adım tarifi.

**Kabul kriterleri:**
- Build geçer.
- Referans grafiği kurallara uygun (0.10'daki testler bunu da kontrol edecek).

### [x] 0.3 Geliştirme altyapısı (docker-compose) — M

**Yapılacaklar:**
- Servisler:

  | Servis | Not |
  |---|---|
  | `postgres:18` | Init script `merp_catalog` ve `merp_demo` DB'lerini oluşturur |
  | `valkey:8` | |
  | `rabbitmq:4-management` | |
  | Keycloak 26 | `start-dev --import-realm` |
  | `mailpit` | |
  | `gotenberg:8` | |
  | Aspire Dashboard | |

  Her servis için healthcheck ve volume tanımlanır. `.env.example` eklenir.

- `deploy/keycloak/merp-realm.json`:
  - Realm `merp`, varsayılan login dili `tr`.
  - Client `merp-web`: public, PKCE, redirect `http://localhost:5173/*`.
  - Client `merp-api`: confidential. Servis hesabında `manage-users` ve `view-users` rolleri.
  - Kullanıcı attribute'u `tenant` ve onu token'a ekleyen protocol mapper.
  - Geliştirme kullanıcıları `admin`, `ik`, `yonetici`, `calisan`; hepsinde `tenant=demo`.

**Kabul kriterleri:**
- `docker compose up -d` sonrası tüm servisler healthy.
- 4 kullanıcının her biri için token alınır ve token'da `tenant` claim'i bulunur. Script: `deploy/scripts/check-tokens.(sh|ps1)`.

### [x] 0.4 SharedKernel — M

**Yapılacaklar:**
- Temel tipler: `Entity<TId>`, `AggregateRoot<TId>` (domain event listesi), `IDomainEvent`.
- Sonuç tipleri:
  - `Result`, `Result<T>`
  - `Error`: `Code`, `Type` (Validation / NotFound / Conflict / Forbidden / Failure), `Args`
- Strongly typed ID deseni.
- Value object'ler:
  - `Email`
  - `PhoneNumber` (E.164, varsayılan TR)
  - `Tckn`, `Vkn` (kontrol haneli)
  - `Iban` (mod-97, TR 26 hane)
  - `LocalizedString`, `DateRange` (`DateOnly`), `Address`
- İşaretleyiciler: `IAuditable`, `ISoftDeletable`, `ICompanyScoped`, `IHasCustomFields`, `[Audited]`, `[Sensitive]`.

**Kabul kriterleri:**
- Her value object için geçerli ve geçersiz örneklerle unit test yazıldı. TCKN, VKN ve IBAN için en az 5'er örnek.

### [x] 0.5 Uygulama soyutlamaları ve mediator — M

**Yapılacaklar:**
- Komut ve sorgu tipleri: `ICommand`, `ICommand<T>`, `IQuery<T>`, handler arayüzleri, `ISender`, `IPipelineBehavior`.
- Pipeline davranışları (bu sırayla):
  1. Logging
  2. Validation (FluentValidation; alan hataları `Error.Validation` olarak döner)
  3. Authorization (`IRequirePermission`)
  4. UnitOfWork (sadece komutlarda): transaction açılır, domain event'ler commit öncesi işlenir, outbox yazılır.
- Soyutlamalar: `ICurrentUser` (UserId, EmployeeId?, TenantId, Culture), `ITenantContext`, `ICompanyContext`, `IPermissionChecker`, `IAccessScopeProvider`, `IIntegrationEventPublisher`.
- Handler kaydı Scrutor ile modül assembly'sinden yapılır.

**Kabul kriterleri:**
- Pipeline sırası testlerle doğrulanır.
- Validasyon hatası exception değil `Result` döner.
- Yetkisiz istek `Forbidden` döner.
- Handler exception fırlatırsa transaction geri alınır ve outbox'a kayıt düşmez.

### [x] 0.6 EF altyapısı — L

**Yapılacaklar:**
- `ModuleDbContext` tabanı:
  - şema, snake_case adlandırma, `xmin` concurrency
  - strongly typed ID ve `LocalizedString` (jsonb) konvansiyonları
  - migration geçmiş tablosu modül şemasında
- Interceptor'lar:
  - Auditable alanlar (Created/Modified At/By)
  - SoftDelete
  - AuditLog → `{schema}.audit_entries`
  - Domain event toplama
- Her modül context'ine eklenen ortak tablolar: `outbox_messages`, `inbox_messages`, `audit_entries`.
- Tenant'a duyarlı DbContext fabrikası.
- `Merp.Migrator`: catalog ve tüm tenant DB'lerini migrate eder, `--tenant` parametresi alır.
- Development ortamında API açılışta migrate eder.

**Kabul kriterleri** (Testcontainers):
- Test entity'si kaydedilince `created_by`, audit kaydı ve outbox kaydı oluşur.
- Eşzamanlı güncellemede ikinci istek 409 alır.

### [x] 0.7 Çoklu kiracı (multi-tenancy) — M

**Yapılacaklar:**
- Catalog DbContext (`tenants` tablosu), tenant cache, çözümleme middleware'i (claim veya route).
- Bilinmeyen veya pasif tenant: 401 (kimlikli istek) veya 404 (anonim istek).
- Dev seed: tenant `demo`.
- `ITenantJobRunner`: aktif tenant'ları dolaşır.
- Hangfire job filter'ı tenant ve kullanıcı bağlamını iş içinde geri yükler.

**Kabul kriterleri:**
- İki tenant'lı integration test: A tenant'ının kullanıcısı B'nin verisine hiçbir endpoint'ten erişemez.

### [x] 0.8 Mesajlaşma: outbox, inbox, transport — L

**Yapılacaklar:**
- `IntegrationEvent` taban record'u, `IIntegrationEventHandler<T>`, olay tip kaydı.
- `OutboxDispatcher`:
  - modül ve tenant başına çalışır
  - 1 sn polling, 50'lik batch, `FOR UPDATE SKIP LOCKED`
  - üstel geri çekilmeyle 10 deneme, sonra dead-letter
  - metrikleri OpenTelemetry'ye yazar
- Inbox: `(message_id, handler)` tekil.
- Transport'lar: `InProcess` (varsayılan) ve `RabbitMQ`. RabbitMQ'da publisher confirm kullanılır, consumer gelen mesajı inbox'a yazar. Seçim `Messaging:Transport` ayarıyla yapılır.

**Kabul kriterleri:**
- Rollback olan transaction'daki olay yayınlanmaz.
- Aynı olay iki kez teslim edilirse handler bir kez çalışır.
- RabbitMQ transport ile modüller arası olay ulaşır (Testcontainers RabbitMQ).

### [x] 0.9 Web host — M

**Yapılacaklar:**
- Loglama ve izleme: Serilog, OpenTelemetry (OTLP → Aspire Dashboard).
- Hata yönetimi: ProblemDetails + exception handler.
  - `Result` → ProblemDetails; yerelleştirilmiş `title`/`detail`, `code` ve alan hataları.
- API dokümantasyonu: OpenAPI + Scalar, Keycloak ile PKCE girişi.
- Güvenlik:
  - JWT bearer (Keycloak)
  - CORS
  - rate limiter (genel politika ve `public` politikası)
- İstek işleme:
  - request localization (`tr-TR` varsayılan)
  - forwarded headers, response compression
  - `X-Company-Id` bağlama
- Health checks: `/health/live`, `/health/ready` (postgres, valkey, rabbitmq, keycloak).
- Altyapı servisleri:
  - HybridCache + Valkey
  - Hangfire server ve dashboard (`/admin/jobs`, yetki: `platform.jobs.view`)
  - SignalR hub tabanı (`/hubs/notifications`)
- `IModule` deseni: modüller `Program.cs`'te açıkça kaydedilir.
- Endpoint yardımcıları: `MapGroup("/api/{module}")`, `.RequirePermission()`.

**Kabul kriterleri:**
- `/health/ready` bağımlılıklardan biri kapalıyken unhealthy döner.
- Scalar'da Keycloak ile giriş yapılıp korumalı örnek endpoint çağrılabilir.
- Hata yanıtları Türkçe ProblemDetails formatındadır.

### [x] 0.10 Test altyapısı ve mimari testler — M

**Yapılacaklar:**
- `tests/Merp.Testing` ortak kütüphanesi:
  - `MerpWebApplicationFactory` (Testcontainers ile Postgres, Valkey, isteğe bağlı RabbitMQ)
  - `TestAuthHandler`: test kullanıcısı, tenant ve rol ataması
  - Respawn ile DB sıfırlama, Bogus builder'ları, `FakeTimeProvider`
- `Merp.ArchitectureTests`:
  - `CLAUDE.md`'deki kurallar 1–7
  - Her endpoint'te yetki metadata'sı var, ya da endpoint D20 listesinde (`EndpointDataSource` dolaşılarak kontrol edilir)
  - Domain projelerinde EF referansı yok
  - `DateTime.Now`/`UtcNow` kullanımı yok

**Kabul kriterleri:**
- Kasıtlı bir kural ihlali mimari testi kırmızıya çevirir. İhlal örneği PR'da gösterilir, sonra kaldırılır.
- Tüm testler CI'da 10 dakikanın altında biter.

### [x] 0.11 CI — S

**Yapılacaklar:**
- `.github/workflows/ci.yml` (PR'da ve main'e push'ta çalışır):
  - **Backend:** `restore --locked-mode`, `build -warnaserror`, coverage'lı test, sonuçları artifact olarak yükle.
  - **Web:** `npm ci`, `lint`, `typecheck`, `test`, `build`.
  - **Güvenlik:** `dotnet list package --vulnerable` ve `npm audit --audit-level=high` uyarı verirse job kırılır.
- Dependabot: nuget, npm ve actions için haftalık.
- PR şablonu: Definition of Done kontrol listesi.

**Kabul kriterleri:**
- Örnek bir PR'da tüm job'lar yeşil.

### [x] 0.12 Web iskeleti 🔍 — L

**Yapılacaklar:**
- Proje kurulumu: Vite + React + TS strict, ESLint (typescript-eslint, react-hooks, i18next/no-literal-string), Prettier, `@/` alias.
- UI temeli: antd `ConfigProvider` (`tr_TR`, tema token'ları), dayjs `tr` locale.
- Kimlik: react-oidc-context + Keycloak.
  - korumalı route'lar, sessiz yenileme, çıkış
- API istemcisi: orval (`npm run gen:api`) ve ortak fetch mutator.
  - Her isteğe token, `Accept-Language` ve `X-Company-Id` başlıklarını ekler.
  - ProblemDetails yanıtını antd notification ile gösterir.
  - 401'de girişe yönlendirir, 403 sayfası gösterir.
- i18n: i18next, metinler API'den yüklenir.
- Uygulama kabuğu:
  - Menü: `/api/platform/me`'den gelen menü ve yetkilerle
  - Üst bar: şirket seçici, bildirim zili ve onay rozeti (yer tutucu), kullanıcı menüsü, dil seçici
  - Hata sınırı (error boundary), 404 sayfası
- Ortak bileşenler:
  - `PageHeader`
  - `DataTable`: sunucu tarafı sayfalama/sıralama/filtre, kolon seçici, Excel indir
  - `FormDrawer`, `Can` (yetki kapısı), `StatusTag`
- Test kurulumu: Vitest + Testing Library, bir smoke test.

**Kabul kriterleri:**
- Giriş yapan kullanıcı boş dashboard görür.
- `calisan` ile `admin` farklı menü görür.
- Metinler API'den gelir; JSX'te metin literal'i varsa lint hata verir.

---

## Faz 1 — Platform çekirdeği

### [x] P1 Şirketler ve şubeler — M

**Yapılanlar:**
- `Company` ve `Branch` aggregate'leri (audit'li; kod değişmez, şube kodu şirket içinde tekil, şirkette tek merkez), VKN/MERSİS/SGK no/kod değer nesneleri, il/ilçe kodlu `Address`.
- 81 il ve 973 ilçe migration ile yüklenir; kaynak ve lisans `docs/reference-data.md`.
- Endpoint'ler: `/api/platform/companies`, `/api/platform/branches` (liste, detay, oluşturma, güncelleme; `version` ile eşzamanlılık), `/api/platform/locations/provinces[/{il}/districts]` (D22).
- Veri kapsamı: Tenant hepsini, Company şirketi ve şubelerini, Branch sadece o şubeyi (ve okumada şirketini) görür; kapsam dışı kayıt 404. Şube listesi aktif şirket başlığıyla daralır (D4). `/me` seçilebilir şirketleri döndürür.
- Olaylar `CompanyCreated/Updated`, `BranchCreated/Updated` outbox ile yayınlanır.
- Web: Organizasyon → Şirketler ve Şubeler ekranları, il/ilçe seçen ortak adres alanları.
- Altyapı: `Merp.Application.Persistence` (sayfalama, sıralama, arama, sürüm), geçici kapsam sağlayıcıları (P2'ye kadar), owned değerlerin audit'i (D25).
- Logo P7 ile eklendi (şirket kaydının alanı değil, şirketin dosyası; D31). Çalışma takvimi P9 ile eklendi (şube formunda seçilir). Özel alan sütunu (`custom_fields`) hazır; tanımlar ve doğrulama P6 ile gelir.
- Yetkiler: `platform.companies.read/manage`, `platform.branches.read/manage`. Sistem rolleri P2 ile gelir; bu yetkiler Sistem Yöneticisi rolünün `platform.*` kümesine girer. Development'ta `admin` (`*`) hepsine sahiptir.
- Kabul testleri: `CompanyEndpointsTests.Create_WithInvalidTaxNumber_Returns400WithATurkishMessage`, `CompanyEndpointsTests.UserScopedToOneCompany_SeesOnlyThatCompany_InTheListAndTheSelector`.

**Yapılacaklar:**
- `Company`:
  - kod, unvan, kısa ad
  - vergi dairesi, VKN, MERSİS no, SGK işyeri sicil no
  - adres, telefon, e-posta
  - varsayılan kültür, saat dilimi, logo (P7 sonrası)
  - aktif/pasif, özel alanlar
- `Branch`:
  - şirket, kod, ad, tür (Merkez / Fabrika / Depo / Mağaza / Ofis)
  - adres, il/ilçe, SGK işyeri sicil no
  - çalışma takvimi (P9 sonrası), aktif/pasif
- Seed: 81 il. İlçeler açık veri kaynağından alınır, kaynak ve lisans not edilir.
- Yetkiler: `platform.companies.read/manage`, `platform.branches.read/manage`.
- Olaylar: `CompanyCreated`, `CompanyUpdated`, `BranchCreated`, `BranchUpdated`.
- Web: şirket ve şube listeleri ile formları. Şirket seçici kullanıcının yetkili şirketleriyle dolar.

**Kabul kriterleri:**
- Hatalı VKN girilince Türkçe mesajlı 400 döner.
- Kapsamı tek şirket olan kullanıcı listede ve seçicide sadece o şirketi görür.

### [x] P2 Kullanıcılar, roller, yetkiler, kapsam 🔍 — XL (2 PR: P2a backend, P2b web)

Kurallar: `docs/domain/yetki-ve-onay.md` §1.

- [x] **P2a backend.** Yapılanlar:
  - JIT kullanıcı, Keycloak aynası ve pasif kullanıcının reddi.
  - Rol atamalarından yetki ve kapsam hesabı (`PlatformAccess`); HybridCache ve commit sonrası etiket temizliği.
  - Yetki kataloğu senkronu ve sistem rolleri (`ITenantDataSeeder`). Yeni yetki deseni uyan sistem rolüne otomatik girer.
  - Endpoint'ler: `/api/platform/users`, `/roles`, `/permissions`, `/scope-types`, `/me/preferences`.
  - Keycloak yönetim API'si: oluşturma ve davet, aktif/pasif, şifre sıfırlama.
  - Son Sistem Yöneticisi koruması; `Merp.Migrator create-admin`; geliştirmede `Platform:Bootstrap:Users`.
  - Karar önerisi: D26.
  - Kabul testleri: `AccessControlTests.ChangingThePermissionMatrix_ChangesMenuAndApiAccessOnTheNextRequest`, `ExpiredOrFutureAssignments_GrantNothing_…`, `ScopesOfSeveralAssignments_AreUnited`.
- [x] **P2b web.** Yapılanlar:
  - Menüde "Erişim yönetimi" grubu: Kullanıcılar (`/access/users`), Roller ve yetkiler (`/access/roles`).
  - Kullanıcılar: arama, rol ve durum filtresi; yeni kullanıcı (davet e-postası). Detayda aktif/pasif, şifre sıfırlama e-postası ve rol atamaları. Atamada kapsam seçici (Tüm şirketler, şirket, şube, yalnızca kendisi) ve geçerlilik tarihleri.
  - Roller: liste; düzenleme ekranında dil başına ad ve yetki matrisi (modül → kaynak → yetki ağacı); özel rolü silme, sistem rolünü varsayılana döndürme.
  - Profilim (`/profile`): hesap bilgisi ve dil tercihi. Üst bardaki dil seçici de tercihi sunucuya kaydeder; başka cihazda seçilen dil açılışta uygulanır.
  - Modül sayfaları rota başına yüklenir (`React.lazy`); açılış paketi 1,6 MB'tan 430 kB'a indi.

**Yapılacaklar:**
- **Yetki kaydı:** Yetki tanımları açılışta DB'ye senkronlanır. Yetki ağacı endpoint'i yerelleştirilmiş etiketlerle döner.
- **Roller:** CRUD; sistem rolleri seed edilir ve silinemez; "varsayılana dön" işlemi.
- **Kullanıcılar:**
  - Keycloak aynası.
  - İlk girişte otomatik oluşturma (JIT provisioning).
  - Admin işlemleri: kullanıcı oluşturma (Keycloak admin API + davet e-postası), pasif/aktif yapma, şifre sıfırlama e-postası.
- **Rol atamaları:**
  - kapsamlı ve tarih aralıklı
  - kapsam türü kaydı (`IScopeTypeRegistry`)
  - `IAccessScopeProvider`, `IPermissionChecker`
  - HybridCache ve tag ile önbellek temizleme
- **`GET /api/platform/me`:** kullanıcı, tenant, yetkili şirketler, düz yetki listesi, menü (modüller yetkiye bağlı menü öğesi kaydeder), tercihler.
- **Bootstrap:** dev seed `admin` kullanıcısına Sistem Yöneticisi (Tenant kapsamı) verir. Üretim için `Merp.Migrator create-admin` komutu.
- `platform.jobs.view` (Hangfire paneli) sadece Sistem Yöneticisi rolüne seed edilir (D22).
- Yetkiler: `platform.users.read/manage`, `platform.roles.read/manage`.
- **Web:**
  - Kullanıcılar: liste; detayda rol atamaları ve kapsam seçici
  - Roller: liste; yetki matrisi (onay kutulu ağaç)
  - Profilim: dil tercihi

**Kabul kriterleri:**
- Yetki matrisi değişince kullanıcının menüsü ve API erişimi bir sonraki istekte değişir (cache temizlenir).
- Süresi dolmuş rol ataması yetki vermez.
- Birden fazla atamada kapsamların birleşimi doğru hesaplanır.

### [x] P3 Audit görüntüleme — S

Yapılanlar (D27): her modüle kendiliğinden kaydedilen `IAuditReader`; `GET /api/platform/audit` ve `/audit/entity-types`; `ISensitiveReadLog`; `occurred_at` indeksi; web'de "Geçmiş" çekmecesi (şirket, şube, kullanıcı, rol) ve Sistem → Denetim kayıtları sayfası. Kabul testi: `AuditEndpointsTests.CompanyUpdate_ShowsOldAndNewValuePerField_InTheHistory`.

**Yapılacaklar:**
- Her modülde `IAuditReader` uygulaması.
- Sorgu API'si: entity veya kullanıcı ve tarih aralığına göre.
- `[Sensitive]` alan okumaları `Read` eylemiyle loglanır.
- Web: `<AuditHistory entityType entityId />` çekmecesi ve admin audit arama sayfası.
- Yetki: `platform.audit.read`.

**Kabul kriterleri:**
- Şirket güncellenince geçmişte alan bazında eski ve yeni değer görünür.

### [x] P4 Çoklu dil — M

Yapılanlar (D28):
- Kiracıya ait `cultures` ve `localization_texts` tabloları; dil zinciri (dil → yedek → varsayılan → tr-TR) ile çalışan istek başına `ITextLocalizer`; HybridCache.
- Kültür seçimi: tercih → `Accept-Language` → kiracı varsayılanı. Hata mesajları da seçilen dilde gelir.
- API: diller CRUD, metin listesi ("eksikler" filtresi), tek metin güncelleme, Excel dışa ve içe aktarma (ClosedXML).
- `LocalizedString` girişinde varsayılan dil zorunlu.
- Web: Sistem → Diller ve çeviriler sayfası. Dil seçici yalnızca aktif dilleri gösterir; oturum açılınca kiracı çevirileri yüklenir.
- `IStringLocalizer` yerine mevcut `ITextLocalizer` genişletildi (D28).
- Kabul testi: `LocalizationEndpointsTests.EnglishFromExcel_ReachesTheUser_MissingTextsInTurkish_ErrorsInEnglish`.

Kurallar: D11.

**Yapılacaklar:**
- Backend:
  - `cultures` ve `localization_texts` tabloları, modüllerin gömülü JSON yükleyicisi
  - birleşik sözlük önbelleği, `IStringLocalizer` uygulaması
  - ProblemDetails yerelleştirmesi
  - `LocalizedString` doğrulaması: varsayılan dil zorunlu
- API:
  - `bundles` (D20)
  - admin: kültür CRUD
  - metin listeleme: anahtar, varsayılan, çeviri, "eksikler" filtresi
  - metin güncelleme, Excel dışa ve içe aktarma
- Seed: sadece `tr-TR` (varsayılan, aktif).
- Web: "Diller ve Çeviriler" sayfası. Dil seçici sadece aktif dilleri gösterir.

**Kabul kriterleri:**
- Admin `en-US` ekler ve Excel'den 10 çeviri yükler.
- Kullanıcı dili değiştirince bu metinler İngilizce, eksikler Türkçe görünür.
- API hata mesajları seçilen dilde gelir.

### [x] P5 Numaralandırma — S

Yapılanlar (D29):
- Desenler `platform.number_sequences` tablosunda (varsayılan ve şirkete özel); sayaçlar her modülün `number_counters` tablosunda.
- `INumberGenerator`: çağıranın transaction'ında tek bir upsert ile satır kilidi alır.
- API ve Sistem → Numaralandırma ekranı (canlı önizleme).
- Seed: `recruitment.requisition`, `recruitment.offer`. **`hr.employee` (H2) ve `hr.leave-request` (L4) HR modülüyle eklenecek**; HR modülü henüz yok.
- Kabul testleri: `NumberGeneratorTests.FiftyParallelRequests_GetFiftyDifferentNumbers`, `NewYear_StartsTheCounterAgain`.

**Yapılacaklar:**
- `NumberSequence`: şirket, belge türü, desen (`IZN-{yyyy}-{seq:6}`), sıfırlama periyodu (Never / Yearly / Monthly), periyot bazında son değer.
- `INumberGenerator.NextAsync(companyId, documentType, date)`: çağıranın transaction'ında satır kilidi kullanır.
- Seed: `hr.employee` (sicil no), `hr.leave-request`, `recruitment.requisition`, `recruitment.offer`.
- Web: desen düzenleme ekranı.

**Kabul kriterleri:**
- 50 paralel istekte tekrar eden numara olmaz.
- Yıl değişince sayaç sıfırlanır.

### [x] P6 Özel alanlar — M

Yapılanlar (D30):
- `platform.custom_field_definitions` (değişmez kod, tip ve şirket; silme yerine pasifleştirme).
- Kayıt türü kaydı (`ICustomFieldEntityProvider`), `ICustomFieldValidator` (önbellekli).
- Liste filtresi: jsonb `@>` ve GIN indeks; uygulama katmanında sağlayıcıdan bağımsız yer tutucu.
- Web: Sistem → Özel alanlar; `<DynamicFields />`; listelerde özel alan kolonları ve filtre.
- İlk entegrasyon: şirketler (form, detay, liste kolonu, filtre).
- Kabul testi: `CustomFieldEndpointsTests.ShoeSize_IsOnTheForm_Required_AndFilterable`.

Kurallar: D12.

**Yapılacaklar:**
- Entity türü kaydı (modüller kaydeder), tanım CRUD, doğrulama servisi, liste filtre yardımcısı.
- Web:
  - Tanım ekranı
  - `<DynamicFields />` (tüm veri tipleri)
  - `DataTable` kolon seçicide özel alanlar

**Kabul kriterleri:**
- Bir entity'ye "Ayakkabı numarası (sayı, zorunlu)" alanı eklenir:
  - Formda görünür.
  - Boş bırakılırsa Türkçe hata verir.
  - Listede filtrelenebilir.

### [x] P7 Dosyalar — M

Yapılanlar (D31):
- `IFileStorage`: dosya sistemi (varsayılan) ve S3 uyumlu depo (AWSSDK.S3; testlerde SeaweedFS).
- `stored_files` her modülün kendi şemasında (`ModuleDbContext`). Ekler ayrı bir `*_attachments` tablosu yerine bu tabloda sahip ve amaçla (`attachment`, `logo`) tutulur.
- `IFileStore`: boyut, uzantı beyaz listesi (`Files:AllowedExtensions`), içerik denetimi (magic bytes, makrolu Office belgeleri reddedilir), SHA-256. Silinen ve sahipsiz içerik commit sonrasında ve gece işinde (`files.sweep`) temizlenir.
- Erişim: kaydın modülü `IFileAccessPolicy` ile karar verir; dosyaların ayrı yetkisi yoktur. Kapsam dışı kayıt 404, salt okuma yetkisiyle yükleme/silme 403.
- API: `/api/platform/attachments` (listele, yükle, sil), `/api/platform/files/{id}` (indir), `/api/platform/companies/{id}/logo`.
- Web: `<Attachments ownerType ownerId />`; şirket formunda logo ve ekler.
- Kabul testleri: `AttachmentEndpointsTests`, `CompanyLogoTests`, `StoredFileSweepTests`, `FileContentInspectorTests`, `S3FileStorageTests`.

Kurallar: D16, D31.

**Yapılacaklar:**
- `IFileStorage` (FileSystem, S3) ve `stored_files` metadata tablosu.
- Yükleme:
  - multipart, boyut ve uzantı beyaz listesi
  - içerik koklama (magic bytes), SHA-256
- İndirme: sahip modülün `IFileAccessPolicy` kontrolünden geçer.
- Ekler: her modül kendi `*_attachments` tablosunu tutar.
- Web: `<Attachments ownerType ownerId />` (sürükle-bırak, liste, indir, sil).

**Kabul kriterleri:**
- İzinsiz uzantı ve uzantısıyla uyuşmayan içerik reddedilir.
- Yetkisiz kullanıcı dosyayı indiremez (404).

### [x] P8 Bildirimler — M (2 PR: P8a backend, P8b web)

- [x] **P8a — backend (D32):**
  - `NotificationRequested` (Platform.Contracts) ve modüllerin tür kaydı (`INotificationTypeProvider`).
  - Zil kayıtları (`notifications`), SignalR ile anında iletim, e-posta kuyruğu (`email_messages`): MailKit, commit sonrası gönderim, `notifications.email` işiyle yeniden deneme.
  - Fluid şablonları (tür × şirket × kültür), yönetim API'si ve `platform.notification_templates.read/manage`.
  - Kullanıcı tercihleri (tür × kanal), okundu işaretleme, test bildirimi.
  - Inbox işleyicileri de commit sonrası işleri (`IAfterCommit`) çalıştırır.
  - Kabul testleri: `NotificationEndpointsTests.Notification_ArrivesAtOnce_InTheBell_AndByEmail` (SignalR + Mailpit), `NotificationEndpointsTests.UserWhoSwitchedEmailOff_GetsOnlyTheInAppNotification`.
- [x] **P8b — web:**
  - Zil: okunmamış sayısı, son bildirimler, tümünü okundu say; bildirime tıklayınca okundu olur ve bağlantısı açılır.
  - Bildirimler sayfası (`/notifications`): tümü / okunmamışlar, sayfalama.
  - Profilim → Bildirimler: tür × kanal anahtarları, test bildirimi.
  - SignalR istemcisi (`@microsoft/signalr`): `notificationReceived` gelince sayı ve listeler yenilenir; bağlantı yoksa odaklanınca yenilenir.


**Yapılacaklar:**
- `notifications` tablosu: alıcı, tür, başlık anahtarı ve argümanları, gövde, bağlantı, okunma zamanı.
- Kanallar:
  - Uygulama içi: SignalR ile anında
  - E-posta: MailKit, e-posta kuyruğu ve Hangfire ile tekrar deneme
- Şablonlar: Fluid, şirket ve kültür bazında.
- Kullanıcı tercihleri: bildirim türü × kanal.
- `NotificationRequested` olayı (Platform.Contracts) ile her modül bildirim isteyebilir.
- Web: zil (okunmamış sayısı), açılır liste, tümünü okundu yap, bildirimler sayfası, tercihler.

**Kabul kriterleri:**
- Olay gelince alıcıya anında bildirim düşer ve Mailpit'e e-posta gelir.
- Kullanıcı e-postayı kapatırsa sadece uygulama içi bildirim gelir.

### [x] P9 Çalışma takvimi ve resmi tatiller — M (2 PR: P9a backend, P9b web)

- [x] **P9a — backend (D33):**
  - `HolidayCalendar` ve `Holiday` (tam ya da yarım gün, hareketli mi), seed 2026–2027, sabit tatilleri önceki yıldan kopyalama.
  - `WorkSchedule` (gün başına mesai ve mola), şirket ve kiracı varsayılanı, şube ezmesi (`branches.work_schedule_id`).
  - `IWorkingCalendar` (Platform.Contracts): takvim çözümü, gün ve aralık sorgusu, önbellek.
  - Hatırlatma işi `calendars.holiday-reminder` (1 Ekim) ve `platform.holidays_missing` bildirimi.
  - Kabul testleri: `WorkingDaysTests` (S1–S7, 6 günlük hafta) ve seed'lenmiş takvimlerle `CalendarEndpointsTests.Days_OfTheSeededSchedules_CountAsTheRulesSay`.
- [x] **P9b — web:**
  - Sistem → Resmi tatiller: takvim ve yıl seçimi, 12 aylık görünüm (tam ve yarım gün), güne tıklayarak ekleme ve düzenleme, liste, önceki yılın sabit tatillerini kopyalama, dini bayramlar girilmemişse uyarı.
  - Sistem → Çalışma takvimleri: liste (haftalık gün ve saat), gün başına mesai ve mola düzenlenen çekmece; gün hataları (`days[i]`) ilgili satırda gösterilir.
  - Şube formunda çalışma takvimi seçimi; boş bırakılırsa şirketin varsayılanı geçerlidir.


**Yapılacaklar:**
- Resmi tatiller:
  - `HolidayCalendar` (TR) ve `Holiday`: tarih, ad, tam veya yarım gün (başlangıç saati)
  - Seed: `izin-kurallari.md` §7 (2026–2027)
- `WorkSchedule`:
  - haftanın her günü için: çalışma günü mü, başlangıç, bitiş, mola
  - şirket varsayılanı, şube ezmesi
- `IWorkingCalendar` (Contracts):
  - `GetDay(scheduleId, date)` → çalışma günü mü, tatil oranı (0 / 0,5 / 1), çalışma saati
  - aralık sorgusu, önbellek
- Hatırlatma işi: her 1 Ekim'de ertesi yılın dini bayramları girilmemişse admin'e bildirim gönderir.
- Web: resmi tatiller (yıl görünümü), çalışma takvimleri.

**Kabul kriterleri:**
- `izin-kurallari.md` §8.1 S1–S7'deki günlerin gün bazlı değerleri doğru hesaplanır.
- 6 günlük takvimde cumartesi çalışma günü sayılır.

### [ ] P10 Onay motoru 🔍 — XL (3 PR: P10a domain + motor, P10b API + olaylar, P10c web)

Kurallar: `docs/domain/yetki-ve-onay.md` §2. Bu belgeyi birebir uygula.

**Yapılacaklar:**
- **Domain:** tanım, adım, kural değerlendirici, talep, görev, vekalet.
- **Platform çözücüleri:** `user`, `role`, `fact-user`.
- **Belge türü kaydı:** anahtar, ad, olgu şeması, bağlantı şablonu, `AutoApproveWhenNoDefinition`.
- **Akışlar:**
  - `ApprovalRequested` handler'ı
  - karar, iptal, iade, yeniden gönderim
  - admin: yeniden atama, zorla onay
  - 15 dakikada bir çalışan eskalasyon işi
- **Contracts:** `ApprovalRequested`, `ApprovalCancellationRequested`, `ApprovalRequestCompleted`, `ApprovalRequestReturned`, `ApprovalTaskAssigned`, `IApprovalPreview`, `IApprovalAccess`.
- **API:** tanım CRUD ve doğrulama, önizleme, görevlerim, talep detayı ve zaman çizelgesi, karar, vekalet CRUD.
- **Web:**
  - Onay Kutum: liste, detay çekmecesi, karar butonları
  - `<ApprovalTimeline />`
  - Onay Akışları editörü: adım listesi, çözücü seçici, mod, all/any gruplu koşul kurucu, süre ve eskalasyon
  - Vekaletlerim

**Kabul kriterleri:**
- §2.9'daki 11 senaryo integration testiyle geçer.
- Testlere özel bir belge türüyle akış arayüzden uçtan uca tamamlanır.

**Alt PR'lar (D34):**
- [x] **P10a domain + motor:**
  - tanım, talep, görev, vekalet, koşul dili
  - `user` / `role` / `fact-user` çözücüleri
  - başlatma, karar, iade, yeniden gönderim, iptal, admin işlemleri, eskalasyon işi
  - bildirim türleri, yetkiler, migration
  - §2.9 senaryo 1–10 motor düzeyinde integration testiyle
- [ ] **P10b API + olaylar:**
  - endpoint'ler (tanım CRUD, önizleme, görevlerim, talep detayı ve zaman çizelgesi, karar, vekalet, admin)
  - `IApprovalPreview`, `IApprovalAccess`
  - D22 listesi
  - seed tanımlar (§2.8)
  - senaryo 10'un HTTP 409'u ve senaryo 11 (inbox)
- [ ] **P10c web:** Onay Kutum, `<ApprovalTimeline />`, akış editörü, Vekaletlerim. 🔍 demo.

### [ ] P11 Ayarlar ve içe/dışa aktarma altyapısı — M

**Yapılacaklar:**
- **Ayarlar:** modüllerin kaydettiği tipli tanımlar (anahtar, tip, varsayılan, kapsam: Tenant / Company), API, önbellek, modüllere göre gruplu genel ayar ekranı.
- **İçe aktarma** (`IImportDefinition`):
  - Kolonlar: yerelleştirilmiş başlıklar, ayrıştırıcılar, satır doğrulayıcı, commit handler.
  - Şablon indirme: ClosedXML; başlık açıklamaları ve liste doğrulamalı hücreler.
  - Akış: yükle → doğrula → hata önizlemesi → commit.
    - 1.000 satırı aşan dosyalar Hangfire'da işlenir.
    - Commit batch bazında transaction kullanır.
    - Sonuç raporu Excel olarak indirilir.
- **Dışa aktarma:** Liste sorgusu Excel'e dönüşür. Kolon seçimi, veri kapsamı ve maskeleme korunur.

**Kabul kriterleri:**
- 5.000 satırlık örnek dosya 1 dakikadan kısa sürede doğrulanır.
- Hatalar Excel raporunda hücre bazında açıklanır.

### [ ] P12 Belge şablonları ve PDF — M

Kurallar: D14.

**Yapılacaklar:**
- `DocumentTemplate`: modülün kaydettiği anahtar, şirket, kültür, Liquid HTML + CSS, sürüm, aktiflik.
- Render ve PDF:
  - Fluid, sadece beyaz listedeki üyelerle (sandbox)
  - Gotenberg istemcisi
  - örnek veriyle önizleme
- Logo ve antet partial'ları.
- Web: şablon editörü (`@monaco-editor/react`, MIT) ve canlı önizleme.

**Kabul kriterleri:**
- Örnek şablon logo ve Türkçe karakterlerle doğru PDF üretir.
- Şablon hatası satır numarasıyla gösterilir.

### [ ] P13 Platform toparlama 🔍 — S

**Yapılacaklar:**
- Admin menü yapısı.
- Ana sayfa: bekleyen onay sayısı, son bildirimler, hızlı bağlantılar.
- Sistem bilgisi: sürüm, tenant. Hangfire bağlantısı.

**Kabul kriterleri:**
- Tüm Platform ekranları menüden erişilebilir ve yetkiye göre gizlenir.
- Faz 1 demo senaryosu `docs/demo/faz1.md`'de yazılı ve çalışıyor.

---

## Faz 2 — İK: Organizasyon ve Personel

`HR` modülü burada kurulur: 5 proje, şema `hr`, test projeleri.

### [ ] H1 Organizasyon yapısı — L

**Yapılacaklar:**
- `OrgUnit`:
  - şirket, üst birim, kod, ad, tür (Direktörlük / Departman / Takım ...)
  - yönetici pozisyonu, masraf merkezi kodu, şube
  - `path` (ltree), geçerlilik tarihleri
- `Job` (unvan kataloğu): kod, ad, aile, seviye (M1, U2...).
- `Position` (kadro):
  - birim, unvan, kod, bağlı olduğu pozisyon
  - norm kadro (varsayılan 1), durum (Aktif / Dondurulmuş / Kapalı), yönetici pozisyonu mu
- Doluluk görev atamalarından türetilir. Boş kadro = norm − dolu.
- `reporting_lines` closure tablosu:
  - Kaynak: pozisyon hiyerarşisi + görev atamaları
  - Olaylarla artımlı güncellenir, gece tam yeniden inşa edilir.
- HR.Contracts:
  - `IOrgQuery`: yönetici zinciri, birim yöneticisi, alt ağaç, kullanıcının organizasyon bilgisi
- Platform'a kayıtlar:
  - Çözücüler: `line-manager`, `org-unit-manager`, `position`
  - Kapsam türleri: `OrgUnit`, `ManagedTeam` (predikat üreticileriyle)
- Yetkiler: `hr.org.read/manage`.
- Web: organizasyon ağacı ve detay, pozisyonlar (norm / dolu / boş), unvanlar, organizasyon şeması (d3-org-chart, fotoğraflı).

**Kabul kriterleri:**
- 3 seviyeli yapı kurulur ve boş kadrolar raporlanır.
- `line-manager(2)` doğru kişiyi döner.
- Birim başka üst birime taşınınca alt birimlerin `path`'i ve kapsam sonuçları güncellenir.

### [ ] H2 Personel kartı ve yaşam döngüsü 🔍 — XL (3 PR: H2a domain + komutlar, H2b API, H2c web)

Kurallar: D6, D13.

**Yapılacaklar:**
- Numaralandırma: `hr.employee` (sicil no, `{seq:6}`, sıfırlanmaz) tanımı HR modülünden bildirilir (P5, D29).
- **`Employee`:**
  - Kimlik: TCKN (tenant içinde tekil), ad, soyad, doğum tarihi ve yeri, cinsiyet, uyruk, medeni hal.
  - İletişim: e-postalar, telefonlar, adresler, acil durum kişileri.
  - Aile: yakınlık, ad, doğum tarihi, çalışıyor mu.
  - Nitelikler: eğitim, sertifika ve ehliyetler (geçerlilik tarihli), yabancı diller, askerlik durumu.
  - IBAN (hassas), fotoğraf, özel alanlar.
- **Hassas veri tablosu** (şifreli, `hr.employees.sensitive.*` yetkisi): engellilik derecesi, kan grubu, sağlık notları, adli sicil belgesi tarihi.
- **`Employment`:**
  - şirket, sicil no (P5), işe giriş tarihi
  - kıdem başlangıç tarihi ve değişiklik gerekçesi
  - istihdam tipi: Belirsiz / Belirli süreli (bitiş) / Kısmi (saat) / Stajyer / Mevsimlik
  - deneme süresi bitişi; çalışma takvimi ve izin politikası ezmeleri; durum
  - çıkış bilgisi: tarih, SGK çıkış kodu (seed), gerekçe, tekrar işe alınabilir mi, not
- **`JobAssignment`** (tarihçeli):
  - pozisyon (birimi pozisyondan gelir), şube, yönetici ezmesi, unvan, seviye, masraf merkezi
  - değişiklik nedeni: Giriş / Nakil / Terfi / Reorganizasyon / Düzeltme
- **Komutlar:** `Hire`, `ChangeAssignment` (ileri tarihli olabilir; gece işiyle devreye girer), `Terminate`, `Rehire`, `GroupTransfer` (kıdem ve bakiye taşıma seçenekleriyle).
- **Belgeler:** türlü (kimlik, diploma, sağlık raporu, sözleşme, SRC, pasaport...), geçerlilik tarihli. Bitişten 30 ve 7 gün önce İK'ya hatırlatma.
- **Olaylar:** `EmployeeHired`, `EmployeeAssignmentChanged`, `EmployeeTerminated`, `EmployeeRehired`, `EmployeeGroupTransferred`.
- **Seed:** SGK çıkış kodları, belge türleri, eğitim seviyeleri, yakınlık dereceleri.
- **Yetkiler:** `hr.employees.read/create/update/terminate/export`, `hr.employees.sensitive.read/update`, `hr.employees.documents.read/manage`.
- **Web:**
  - Personel listesi: şirket, şube, birim, durum, pozisyon ve özel alan filtreleri; kolon seçici; Excel; avatar
  - Personel kartı sekmeleri: Genel, İletişim, İstihdam ve görev geçmişi (zaman çizelgesi), Aile, Eğitim ve sertifikalar, Belgeler, İzinler (L3 sonrası), Özel alanlar, Geçmiş (audit)
  - Sihirbazlar: İşe giriş, Görev değişikliği, İşten çıkış

**Kabul kriterleri:**
- İşe girişten sonra kadro "dolu" görünür.
- İleri tarihli görev değişikliği o gün devreye girer (`FakeTimeProvider` testi).
- Çıkışı yapılan personel varsayılan listede görünmez, filtreyle görünür.
- Hassas alanlar yetkisiz kullanıcıya maskeli gelir ve okunmaları audit'e düşer.
- Tekrarlanan TCKN anlamlı bir hata verir.
- Holding içi nakilde kıdem tarihi korunur.

### [ ] H3 Personel–kullanıcı bağı ve self servis — M

Kurallar: D6.

**Yapılacaklar:**
- **Kullanıcı oluşturma:**
  1. HR, "Sistem kullanıcısı oluştur" ile `UserProvisioningRequested` olayını yayınlar.
  2. Platform kullanıcıyı açar ve Çalışan rolünü (Self) atar.
  3. Platform `UserLinkedToEmployee` yayınlar, HR `UserId`'yi saklar.
- Mevcut kullanıcıyı bağlama ve bağı kaldırma.
- `EmployeeTerminated` gelince kullanıcı ayara göre pasifleşir: hemen veya çıkış tarihinde.
- `ICurrentUser.EmployeeId` doldurulur.
- **Self servis:**
  - Profilim: kendi kartını okuma.
  - Değişiklik talebi: iletişim, adres, acil durum, aile, IBAN.
  - Belge türü `hr.profile-change`; onaylanınca karta uygulanır.

**Kabul kriterleri:**
- Çalışan kendi kartını görür, başkasınınkini göremez (404).
- Onaylanan adres değişikliği karta yansır ve audit'te talep numarasıyla görünür.
- Çıkış tarihinden sonra kullanıcı giriş yapamaz.

### [ ] H4 Organizasyon ve personel içe aktarma — M

**Yapılacaklar:**
- P11 tanımları:
  - unvanlar, birimler, pozisyonlar
  - personel: kişi, istihdam ve görev aynı satırda
- TCKN'ye göre güncelleme modu ve deneme (dry run).
- `samples/` klasörüne Bogus ile üretilmiş 2.000 personellik örnek dosya eklenir.

**Kabul kriterleri:**
- Örnek dosya hatasız aktarılır.
- Hatalı TCKN içeren satır raporda gösterilir, diğer satırlar aktarılır.

---

## Faz 3 — İzin

Kurallar: `docs/domain/izin-kurallari.md`. Bu belgeyi birebir uygula. §8'deki her senaryo bir testtir.

### [ ] L1 İzin türleri ve politikalar — L

**Yapılacaklar:**
- Modeller: `LeaveType`, `LeavePolicy` (sürümlü), `PolicyAssignmentRule`, personel bazında ezme.
- Seed: §2–3. `BABALIK` ve `DOGUM` için iki sürüm.
- Kanuni alt sınır doğrulayıcısı: uyarı üretir, kaydı engellemez.
- Web:
  - İzin türleri
  - Politika formu: kademe tablosu, yaş alt sınırları, ek gün kuralları, birikim yöntemi, avans, yuvarlama, talep kuralları
  - Atama kuralları: öncelik listesi, kriter kurucu
  - "Bu personele hangi politika uygulanıyor?" test aracı

**Kabul kriterleri:**
- Seed politikası §2'deki tabloyla birebir aynıdır.
- Kanuni alt sınırın altındaki ayar uyarı verir.

### [ ] L2 Süre hesaplayıcı — M

**Yapılacaklar:**
- `ILeaveDurationCalculator` (§4).
- Takvim çözümlemesi: istihdam ezmesi > şube > şirket.
- `POST /api/hr/leave/duration-preview`.

**Kabul kriterleri:**
- §8.1 S1–S8 unit testleri geçer.

### [ ] L3 Bakiye ve hak ediş — L

**Yapılacaklar:**
- Ledger (§5).
- Bakiye sorgusu: kazanılan, kullanılan, bekleyen, kullanılabilir, sonraki hak ediş tarihi ve miktarı.
- Hak ediş işi: geriye dönük telafi yapar, idempotent çalışır.
- Bakiye hareketleri:
  - açılış bakiyesi içe aktarma (P11)
  - İK düzeltmesi (gerekçe zorunlu)
  - holding içi nakil handler'ı
  - çıkışta kapanış ve kalan günle `EmployeeLeaveBalanceClosed` olayı
- Web: personel kartı İzinler sekmesi (bakiye kartları + hareketler), düzeltme formu, açılış bakiyesi aktarımı.

**Kabul kriterleri:**
- §8.2 E1–E10b ve E12 testleri geçer.

### [ ] L4 İzin talebi ve onay 🔍 — XL (2 PR: backend, web)

**Yapılacaklar:**
- Numaralandırma: `hr.leave-request` (`IZN-{yyyy}-{seq:6}`, yıllık) tanımı HR modülünden bildirilir (P5, D29).
- **`LeaveRequest`:**
  - numara, istihdam, tür, başlangıç/bitiş, yarım gün parçaları, saat, süre
  - açıklama, izindeki adres ve telefon, vekil personel, yol izni günleri, ekler
  - durum makinesi, onay talebi id'si
- **Komutlar:**
  - `CreateDraft`
  - `Submit`: §6 doğrulaması yapar, `ApprovalRequested` yayınlar. Olgular: `leaveTypeCode`, `days`, `requesterEmployeeId`, `orgUnitId`, `companyId`, `isPastDate`.
  - `Cancel`: taslak veya bekleyen talepte.
  - `RequestCancellation`: onaylı ve başlamamış talepte.
  - İK: `CreateOnBehalf`, `Record` (geçmiş izni akışsız onaylı kaydetme, yetki `hr.leave.requests.record`), `EarlyReturn`.
- **Onay olaylarının işlenmesi:**
  - `ApprovalRequestCompleted` → onay (ledger −) veya ret.
  - `ApprovalRequestReturned` → iade.
- **Belge türleri:** `hr.leave-request`, `hr.leave-cancellation`. Varsayılan akışlar yetki-ve-onay §2.8'den seed edilir.
- **Bildirimler:**
  - Gönderildi: talep edene.
  - Onaylandı / reddedildi: talep eden ve vekile.
  - İzin hatırlatması: bir gün önce talep eden ve yöneticiye.
- **Toplu izin:**
  - Personel veya birim + tarih + tür seçilir.
  - Bakiye sorunları önizlenir, sonra onaylı talepler oluşur.
  - Yetki: `hr.leave.bulk`.
- **Web:**
  - İzinlerim: bakiyeler, talepler
  - Yeni talep çekmecesi: canlı süre, talep sonrası bakiye, onaylayıcı önizlemesi, uyarılar
  - Talep detayı: zaman çizelgesi
  - İK izin talepleri: kapsam dahilindeki tüm talepler, filtreler
  - Toplu izin

**Kabul kriterleri:**
- Uçtan uca akış: çalışan talep eder → yönetici onaylar → bakiye düşer → bildirimler gider.
- 6 günlük talep 2. adımda İK'ya gider.
- İade edilen talep düzeltilip tekrar gönderilebilir.
- Onaylı iznin iptali bakiyeyi geri yükler.
- E13 (çakışma) testi geçer.

### [ ] L5 İzin takvimi — S

**Yapılacaklar:**
- Kapsam (ekip / birim / şirket) ve tarih aralığına göre etkinlik endpoint'i, tatil katmanıyla.
- Web: FullCalendar ay ve liste görünümü, türe göre renk.
- Uyarı: aynı birimde aynı gün eşik sayıdan fazla kişi izindeyse. Eşik bir ayardır.

**Kabul kriterleri:**
- Yönetici sadece astlarının izinlerini görür.

### [ ] L6 İzin raporları ve belgeleri — M

**Yapılacaklar:**
- Raporlar:
  - bakiye (belirli bir tarih itibarıyla)
  - kullanım (dönem ve tür bazında)
  - yaklaşan izinler
  - uzun süredir izin kullanmayanlar
- PDF belgeler (P12 seed şablonları):
  - Yıllık izin kayıt belgesi
  - İzin talep formu (imzalı çıktı için)
- Tüm raporlar Excel'e aktarılabilir.

**Kabul kriterleri:**
- Kayıt belgesi kıdem, hak edilen ve kullanılan izin tarihleri ile imza alanlarını içeren PDF üretir.

---

## Faz 4 — İşe Alım (`Recruitment` modülü)

### [ ] R1 Modül iskeleti ve ayarlar — M

**Yapılacaklar:**
- Modül projeleri, şema `recruitment`, yetkiler, menü. *(Projeler, şema, `RecruitmentDbContext` ve "İşe alım" menüsü A2 ile geldi; kalanlar bu görevde.)*
- Ayarlar:
  - Aşama şablonları. Aşama alanları: ad, tip, sıra, SLA günü. Tipler: Applied / Screening / Interview / Assessment / Reference / Offer / Hired / Rejected / Withdrawn.
  - Red nedenleri, aday kaynakları, etiketler.
  - E-posta şablonları: başvuru alındı, mülakat daveti, ret, teklif.
  - KVKK aydınlatma ve açık rıza metinleri: şirket ve kültür bazında, sürümlü. Varsayılan aydınlatma metni, yapay zeka özellikleri açıksa yapay zekayla ön değerlendirme ve yurt dışına aktarım maddesini içerir (`docs/domain/yapay-zeka.md` §10.3).
  - Saklama süresi.

**Kabul kriterleri:**
- Seed'li varsayılan aşama şablonu ve e-posta şablonları gelir; admin bunları değiştirebilir.

### [ ] R2 Personel talebi — M

**Yapılacaklar:**
- **`JobRequisition`:**
  - numara, şirket, birim
  - pozisyon: mevcut boş kadro veya yeni kadro önerisi (unvan, seviye)
  - kişi sayısı, neden (Yeni / Yerine — ayrılan personelle), istihdam tipi
  - istenen başlangıç tarihi, ücret aralığı (hassas), gerekçe, işe alım yöneticisi
  - durum
- **Onay:** belge türü `recruitment.requisition`. Olgular: `companyId`, `orgUnitId`, `headcount`, `reason`, `isNewPosition`.
- Kadro boşluğu `IOrgQuery` ile doğrulanır.
- Web: liste, form, `ApprovalTimeline`'lı detay.

**Kabul kriterleri:**
- Yönetici talep açar, onay zinciri işler, onaylanınca "İlan oluştur" aktifleşir.
- Dolu kadroya talep açılamaz. Yeni kadro önerisi bu kuralın dışındadır.

### [ ] R3 İlanlar ve kariyer sayfası — L

**Yapılacaklar:**
- **`JobOpening`:**
  - talep (isteğe bağlı), başlık, açıklama (zengin metin), aranan nitelikler
  - şube, çalışma modeli, istihdam tipi, açık kadro sayısı
  - aşama şablonunun kopyası
  - işe alım ekibi: yönetici, uzmanlar, mülakatçılar
  - başvuru formu: standart alanlar ve özel sorular (eleme sorusu desteğiyle)
  - görünürlük (İç / Dış), yayın tarihleri, durum
- **Herkese açık API** (D20):
  - ilan listesi ve detayı
  - başvuru: aday bilgisi, CV, cevaplar, KVKK onay kutuları (metin sürümüyle)
  - rate limit + honeypot, dosya tip ve boyut kontrolü
  - aynı e-postayla aynı ilana tekrar başvuru engellenir
- **İç ilan:** Çalışan self servisten başvurur (iç aday işaretiyle).
- **Web:**
  - İlanlar: aşama başına sayılarla liste
  - İlan formu: TipTap editör
  - Kariyer sayfası `/careers/:tenant`: sade, mobil uyumlu, KVKK metni modalı

**Kabul kriterleri:**
- Anonim kullanıcı CV ile başvurur; aday ve başvuru oluşur; "başvurunuz alındı" e-postası Mailpit'e düşer.
- Rıza işaretlenmeden yapılan başvuru reddedilir.

### [ ] R4 Aday havuzu ve başvurular 🔍 — L

**Yapılacaklar:**
- **`Candidate`** (tenant geneli):
  - ad, iletişim, konum, mevcut unvan ve şirket, deneyim süresi, eğitim
  - bağlantılar (LinkedIn, portfolyo), CV dosyaları (sürümlü)
  - kaynak (+ referans veren personel), etiketler
  - rıza kayıtları
  - notlar (@kullanıcı anma → bildirim)
  - tekrar eden aday birleştirme
- **`Application`:**
  - aday × ilan, aşama, durum
  - aşama geçmişi: kimden, kime, kim, ne zaman, neden
  - cevaplar, puan özeti
  - ret (neden + isteğe bağlı gecikmeli şablon e-postası), geri çekilme
- **Arama:**
  - PostgreSQL tam metin arama (`turkish` konfigürasyonu)
  - Kapsam: ad, unvan, etiketler, notlar, CV metni
  - CV metni çıkarma: PDF için PdfPig, DOCX için DocumentFormat.OpenXml *(`IDocumentTextExtractor` A1 ile hazır)*
- **Web:**
  - İlan detayında kanban (@dnd-kit): sürükle-bırak, ret için neden modalı; ayrıca liste görünümü
  - Aday kartı: profil, başvurular, mülakatlar, değerlendirmeler, notlar, dosyalar, rıza durumu, zaman çizelgesi
  - Havuz araması, adayı başka ilana ekleme

**Kabul kriterleri:**
- Aşama değişikliği geçmişe yazılır.
- Ret e-postası şablonla gönderilir.
- Aynı e-postayla ikinci aday oluşturulmaz; birleştirme önerilir.

### [ ] R5 Mülakat ve değerlendirme — L

**Yapılacaklar:**
- **`Interview`:**
  - başvuru, tip (Telefon / Video / Yüz yüze / Teknik / İK / Yönetici / Panel)
  - başlangıç ve bitiş, yer veya online link (elle girilir)
  - mülakatçılar, organizatör, durum (Planlandı / Tamamlandı / İptal / Gelmedi), notlar
- **Davetler:**
  - Aday ve mülakatçılara ICS ekli e-posta (Ical.Net). Güncelleme ve iptalde `SEQUENCE` artırılır.
  - Hatırlatmalar: 1 gün ve 1 saat önce.
- **Çakışma kontrolü:** Mülakatçının sistemdeki diğer mülakatlarıyla yapılır.
- **Değerlendirme şablonu:**
  - bölümler
  - kriterler: 1–5 puan, her puanın açıklaması, ağırlık
  - açık uçlu sorular
  - genel tavsiye: Kesin Hayır / Hayır / Evet / Kesin Evet
- **Geri bildirim:**
  - Her mülakatçı kendi formunu doldurur.
  - `blindFeedback = true` ise kendi formunu göndermeden diğerlerini göremez.
  - İK ve işe alım yöneticisi hepsini görür.
  - Başvuru için ağırlıklı puan hesaplanır.
- **Mülakatçı erişimi:** Sadece atandığı başvuruları görür, ücret beklentisini göremez.
- **Web:**
  - Mülakat planlama modalı: çakışma uyarısı, davet önizlemesi
  - Mülakatlarım: liste ve takvim
  - Değerlendirme formu
  - Aday kartında puan özeti (ECharts)

**Kabul kriterleri:**
- Planlanan mülakat için davetler gider. Tarih değişince güncelleme daveti gider.
- Kör geri bildirim kuralı testle doğrulanır.

### [ ] R6 Teklif — M

**Yapılacaklar:**
- **`Offer`:**
  - başvuru, numara, pozisyon, başlangıç tarihi
  - ücret, para birimi, periyot (hassas), yan haklar
  - son geçerlilik tarihi, durum
- **Onay:** belge türü `recruitment.offer`. Olgular: `salary`, `grade`, `companyId`, `orgUnitId`, `hiringManagerUserId`, `isAboveBudget` (talepteki aralığa göre).
- Teklif mektubu PDF (P12) e-postayla gönderilir.
- Aday yanıtı İK tarafından kaydedilir. İsteğe bağlı: imzalı token'lı ve süreli kabul/ret bağlantısı.
- Süre dolan teklifleri kapatan iş.

**Kabul kriterleri:**
- Bütçe üstü teklif ek onay adımına gider.
- Kabul edilen teklif "İşe al" adımını açar.

### [ ] R7 İşe alımdan personele geçiş 🔍 — M

**Yapılacaklar:**
1. "İşe al" komutu `CandidateHired` olayını yayınlar. İçerik: aday, başvuru, teklif, şirket, pozisyon, birim, başlangıç tarihi, kişisel bilgiler, istihdam tipi.
2. HR handler'ı:
   - TCKN veya e-postayla mevcut personeli arar, yoksa oluşturur.
   - İstihdam ve görevi **ön kayıt** durumunda oluşturur.
3. İK eksik alanları tamamlayıp işe girişi kesinleştirir. HR `EmployeeHired` yayınlar.
4. Recruitment bu olayla:
   - başvuruyu Hired yapar
   - talepteki dolan kadro sayısını artırır
   - kadrolar dolunca ilanı Filled yapar
5. Kalan adaylar için toplu ret teklif edilir.

**Kabul kriterleri:**
- Kabul edilen aday tek tıkla personel ön kaydı olur.
- İK tamamladıktan sonra kadro dolu görünür ve talep "Doldu" olur.

### [ ] R8 KVKK ve saklama — M

**Yapılacaklar:**
- Rıza metni sürümleri ve aday bazında rıza kaydı.
- Süre dolmadan yeniden rıza e-postası: imzalı token'lı herkese açık rıza sayfası.
- Saklama işi: aktif başvurusu olmayan ve süresi dolan aday anonimleşir.
  - ad → "Anonim aday #"
  - iletişim bilgisi ve notlar silinir
  - dosyalar silinir
  - istatistikler korunur
- İlgili kişi talebi: aday verisini dışa aktarma ve talep üzerine silme (ayrı yetki).

**Kabul kriterleri:**
- Süresi dolan aday gece işinde anonimleşir, dosyaları silinir.
- İşe alım raporlarındaki sayılar değişmez.

### [ ] R9 İşe alım raporları — S

**Yapılacaklar:**
- Raporlar:
  - ilan bazında huni (aşama sayıları, dönüşüm oranı)
  - işe alım ve kadro doldurma süresi
  - kaynak etkinliği
  - mülakatçı yükü ve geri bildirim gecikmesi
  - açık talep yaşlandırması
  - teklif kabul oranı
- Excel dışa aktarma.
- Web: ECharts ile dashboard.

**Kabul kriterleri:**
- Seed verisiyle dashboard rakamları elle hesaplanan değerlerle eşleşir (integration test).

---

## Yapay zeka (D24)

Kurallar ve özellik kataloğu: `docs/domain/yapay-zeka.md`. Her yapay zeka görevi için ek Definition of Done:
- Kişisel veri maskelenmeden sağlayıcıya gitmez; entegrasyon testi sağlayıcıya giden metni doğrular.
- Sonuç tipli (structured output), talimat sürümlü; kullanım kaydı yazılır.
- Kiracıda özellik kapalıyken 403 testi var; ekranda "yapay zeka önerisi" uyarısı var.
- CI gerçek API'yi çağırmaz (sahte sağlayıcı).

### [x] A1 Yapay zeka altyapısı — M

**Yapılanlar:** `IAiService`, Anthropic sağlayıcısı (resmi SDK; structured outputs, effort, prompt caching, sunucu tarafı yedek model), kiracı ve özellik bazında açma, aylık bütçe, `platform.ai_usage` kullanım kaydı ve `GET /api/platform/ai-usage/summary`, `Merp.AI` metrikleri, kullanıcı başına `ai` hız sınırı, `PersonalDataRedactor`, `IDocumentTextExtractor` (PDF/DOCX), `/me` içinde etkin özellikler, sahte sağlayıcı (`FakeAiModelClient`).

**Kabul kriterleri:**
- Sağlayıcı yapılandırılmamışsa veya kiracıda kapalıysa özellik çağrılamaz ve menüde görünmez.
- Bütçe dolunca çağrı reddedilir; başarısız çağrılar da kullanım kaydına yazılır.
- Sağlayıcı istek biçimi (model, şema, effort, önbellek, yedek model) birim testle doğrulanır.

### [x] A2 CV analizi (durumsuz) ve kullanım ekranı — M

**Yapılanlar:** `POST /api/recruitment/cv-analyses`, İşe alım → CV analizi ekranı, Sistem → Yapay zeka kullanımı ekranı. İşe alım modülünün iskeleti (projeler, şema, menü) bu görevle geldi.

**Kabul kriterleri:**
- PDF ve DOCX analiz edilir; sağlayıcıya giden metinde ad, e-posta, telefon, bağlantı ve özel alanlar yoktur.
- Gereksinim verilmezse eşleşme döndürülmez.
- Yetkisiz kullanıcı 403, kapalı kiracı 403 `ai_disabled`, sağlayıcı hatası 503 alır.

### [ ] A3 Aday kartında CV analizi — M (R4 ile)

- CV yüklendiğinde analiz arka plan işinde çalışır ve başvuruya kaydedilir (model, talimat sürümü, tarih). Yeniden analiz edilebilir.
- Aday formu CV'den doldurulur. İletişim bilgileri yerelde (regex) çıkarılır, sağlayıcıya gönderilmez.
- Kabul: analiz sonucu başvuru kartında görünür; aday anonimleşince (R8) analiz de silinir.

### [ ] A4 İlan metni taslağı ve kapsayıcı dil kontrolü — S (R3 ile)

- Personel talebinden ve kısa açıklamadan ilan taslağı; yaş, cinsiyet ve benzeri dışlayıcı ifadeler için uyarı listesi.
- Kabul: taslak TipTap editörüne gelir, kullanıcı düzenlemeden yayınlanamaz.

### [ ] A5 Mülakat soruları, geri bildirim özeti, aday e-posta taslakları — M (R5 ile)

- Pozisyon ve değerlendirme şablonundan soru seti; mülakatçı formlarından ortak özet (kör geri bildirim kuralına uyar: özet herkes formunu gönderdikten sonra görünür).
- Ret ve davet e-postası şablonunu adaya göre kişiselleştirme (gönderim her zaman kullanıcı onayıyla).

### [ ] A6 Toplu ön tarama — M (R4 sonrası)

- Bir ilanın başvurularını Batch API ile gece analiz eder (yarı maliyet). Bütçe ve kiracı ayarı gözetilir.
- Kabul: sonuçlar sıralama değil, gereksinim bazında kanıt olarak gösterilir; otomatik ret yoktur.

### [ ] A7 Çeviri taslakları — S (P4 ile)
### [ ] A8 Onay özeti — S (P10 ile)
### [ ] A9 İçe aktarma kolon eşleştirme önerisi — S (P11 ile)
### [ ] A10 Belge şablonu taslağı — S (P12 ile)
### [ ] A11 Doğal dille izin talebi taslağı — S (L4 ile)
### [ ] A12 Asistan (Ctrl+K doğal dil, salt okunur, yetki ve veri kapsamına bağlı) — L (Faz 5)

### [ ] A13 Değerlendirme seti (eval) — M

- Anonimleştirilmiş örnek CV'ler, beklenen alanlar, eşleşme puanı aralıkları ve adalet kontrolleri (yalnızca ad veya cinsiyet değişince sonuç değişmemeli).
- Talimat veya model değişikliği bu setle ölçülür; sonuç PR'da paylaşılır.

### [ ] A14 Yapay zeka ayarları ekranı — S (P11 ile)

- Kiracı yöneticisi özellikleri ve aylık bütçeyi ayarlar ekranından açar/kapatır; yapılandırma dosyasındaki `Ai:Tenants` buraya taşınır.

### [ ] A15 Kurum içi model sağlayıcısı — M (müşteri talebiyle)

- Müşterinin kendi sunucusundaki açık lisanslı bir modele (D19 lisansları) OpenAI uyumlu HTTP API üzerinden bağlanan `IAiModelClient` uygulaması; şemaya uygun çıktı sunucunun JSON şema desteğiyle sağlanır.
- Kabul: A13 değerlendirme setinde kalite ölçülür ve müşteriye raporlanır; kurulum ve donanım rehberi `docs/` altında.

### [ ] A16 Müşterinin bulut hesabı üzerinden Claude — S (müşteri talebiyle)

- Amazon Bedrock, Google Cloud veya Microsoft Foundry üzerinden Claude (resmi SDK'nın platform istemcileri). Sunucu tarafı yedek model ve Batch API bu platformlarda yoktur.

---

## Faz 5 — Sağlamlaştırma ve v0.1

### [ ] X1 Uçtan uca testler — M

Playwright ile kritik akışlar:
- giriş
- işe giriş
- izin talebi → onay
- ilan → başvuru → mülakat → teklif → işe alım
- dil değiştirme

CI'da gece çalışır.

### [ ] X2 Performans — M

**Yapılacaklar:**
- Test verisi: 10.000 personel, 100.000 izin hareketi, 50.000 aday.
- Hedef: liste endpoint'lerinde p95 < 300 ms.
- Index'ler gözden geçirilir.
- Testlerde EF sorgu sayısı kontrol edilerek N+1 yakalanır.

### [ ] X3 Güvenlik — M

**Yapılacaklar:**
- OWASP ASVS L2 kontrol listesi.
- Otomatik test: tüm endpoint'ler yetkisiz token ile çağrılır, 403 veya 404 beklenir.
- Dosya yükleme güvenliği, rate limit, güvenlik başlıkları (web için CSP), secret yönetimi.

### [ ] X4 KVKK denetimi — S

**Yapılacaklar:**
- `docs/kvkk.md`:
  - kişisel veri envanteri: tablo, alan, amaç, saklama süresi
  - hassas veri erişim logları, maskeleme kontrolü
  - aydınlatma metinleri
  - silme ve anonimleştirme prosedürü

### [ ] X5 Paketleme ve operasyon — M

**Yapılacaklar:**
- Dockerfile'lar: API (multi-stage, chiseled imaj) ve web (statik, nginx).
- `docker-compose.prod.yml` örneği ve ortam değişkenleri dokümanı.
- Runbook'lar: yedekleme/geri yükleme (tenant başına `pg_dump`), sürüm yükseltme (Migrator).

### [ ] X6 v0.1 sürümü 🔍 — S

**Yapılacaklar:**
- `CHANGELOG.md`.
- Demo verisi komutu: `Merp.Migrator seed-demo`.
- Ekran bazlı kısa kullanıcı kılavuzu taslağı.
