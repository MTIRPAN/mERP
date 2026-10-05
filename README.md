# mERP

Holdingler ve büyük şirketler için ERP ürünü. Modüler monolit (.NET 10) + React.

İlk hedef: Platform çekirdeği → İK (organizasyon, personel, izin, işe alım).

## Belgeler

| Belge | İçerik |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Çalışma kuralları, proje yapısı, mimari ve kod kuralları, Definition of Done |
| [`docs/PLAN.md`](docs/PLAN.md) | Görevler, sıra ve kabul kriterleri |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Mimari kararlar, izinli ve yasaklı kütüphaneler |
| [`docs/domain/`](docs/domain) | Alan kuralları (yetki ve onay, izin mevzuatı, yapay zeka) |

## Gereksinimler

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json` 10.0.100 ve üstü feature sürümlerini kabul eder)
- [Node.js 22+](https://nodejs.org/) ve npm
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (veya Docker Engine + Compose v2)

## İlk çalıştırma

```bash
# 1. Araçları geri yükle (dotnet-ef)
dotnet tool restore

# 2. Geliştirme altyapısını başlat
#    postgres, valkey, rabbitmq, keycloak, mailpit, gotenberg, aspire-dashboard
cp .env.example .env
docker compose up -d

# 3. Derle ve testleri çalıştır (integration testleri Docker gerektirir)
dotnet build -warnaserror
dotnet test

# 4. API'yi başlat — https://localhost:5001, dokümantasyon: /scalar
dotnet run --project src/Host/Merp.Api

# 5. Web arayüzünü başlat — http://localhost:5173
cd web
npm install
npm run dev
```

Geliştirme kullanıcıları (Keycloak realm importundan gelir): `admin / admin`, `ik / ik`, `yonetici / yonetici`, `calisan / calisan`. Hepsinde `tenant=demo` attribute'u vardır.

## Geliştirme altyapısı

| Servis | Adres | Not |
|---|---|---|
| PostgreSQL 18 | `localhost:5432` (`merp` / `merp`) | `merp_catalog` ve `merp_demo` veritabanları ilk açılışta oluşur |
| Valkey 8 | `localhost:6379` | Redis uyumlu cache |
| RabbitMQ 4 | `localhost:5672`, yönetim: http://localhost:15672 (`merp` / `merp`) | |
| Keycloak 26 | http://localhost:8080 (master admin: `admin` / `admin`) | Realm `merp`, `deploy/keycloak/merp-realm.json`'dan import edilir |
| Mailpit | http://localhost:8025, SMTP `localhost:1025` | Giden tüm e-postalar burada görünür |
| Gotenberg 8 | http://localhost:3000 | HTML → PDF |
| Aspire Dashboard | http://localhost:18888, OTLP gRPC `4317`, OTLP HTTP `4318` | Log, trace ve metrikler |

Kontrol scriptleri (Windows için `.ps1` karşılıkları da var):

```bash
deploy/scripts/check-infra.sh    # tüm servisler healthy olana kadar bekler
deploy/scripts/check-tokens.sh   # 4 geliştirme kullanıcısı için token alır, tenant claim'ini doğrular
```

Keycloak realm'i sadece ilk açılışta import edilir. `merp-realm.json` değişirse:

```bash
docker compose rm -sf keycloak && docker volume rm merp_keycloak-data && docker compose up -d keycloak
```

Keycloak istemcileri:
- `merp-web`: public, PKCE (S256). Token'a `tenant` claim'i ve `merp-api` audience'ı eklenir. Geliştirme kolaylığı için password grant açıktır; üretim realm'inde kapatılmalıdır.
- `merp-api`: confidential (geliştirme secret'ı `merp-api-dev-secret`). Servis hesabında `manage-users` ve `view-users` rolleri var.

## API

| Adres | Not |
|---|---|
| `https://localhost:5001/scalar` | API dokümantasyonu (Development). "Authorize" ile Keycloak'a PKCE ile giriş yapılır |
| `https://localhost:5001/openapi/v1.json` | OpenAPI dokümanı (web istemcisi bundan üretilir) |
| `/health/live`, `/health/ready` | Canlılık ve bağımlılık (PostgreSQL, Valkey, RabbitMQ, Keycloak) kontrolleri |
| `/admin/jobs` | Hangfire paneli (salt okunur). Önce `POST /api/platform/jobs/dashboard-session` ile oturum çerezi alınır; sadece `Jobs:DashboardTenants` listesindeki tenant'lar açabilir |
| `/hubs/notifications` | SignalR bildirim kanalı |

Hata yanıtları ProblemDetails biçimindedir: yerelleştirilmiş `title` ve `detail`, sabit `code`, `traceId`; doğrulama hatalarında `errors` (alan → mesajlar) ve `fieldErrors` (alan, kod, mesaj).

> **Geçici yetki davranışı (P2'ye kadar):** Roller ve atamalar henüz yok. Development ortamında oturum açmış her kullanıcı tüm yetkilere sahiptir; diğer ortamlarda hiçbir yetki verilmez.

## Web

```bash
cd web
cp .env.example .env.local   # isteğe bağlı: API ve Keycloak adresleri
npm install
npm run dev                  # http://localhost:5173 — /api, /hubs ve /admin/jobs API'ye proxy'lenir
```

- Giriş Keycloak üzerinden (PKCE) yapılır. Geliştirme kullanıcılarından `admin` "Sistem" menüsünü görür, `calisan` sadece ana sayfayı görür (yetkiler P2'ye kadar `appsettings.Development.json` → `Development:Permissions`).
- Bütün metinler API'den (`/api/platform/localization/bundles/tr-TR`) gelir. JSX içinde metin literal'i lint hatasıdır.
- API istemcisi orval ile üretilir: API Development'ta `http://localhost:5000`'de çalışırken `npm run gen:api`. Üretilen kod `src/shared/api/generated/` altındadır ve elle düzenlenmez.
- Tema: üst bardaki seçiciyle Açık / Koyu / Sistem. Hızlı geçiş: Ctrl/⌘+K.

| Klasör | İçerik |
|---|---|
| `src/app` | Uygulama kabuğu: tema, kimlik doğrulama, i18n, menü, üst bar, rotalar |
| `src/shared` | API istemcisi, ortak bileşenler (`DataTable`, `FormDrawer`, `PageHeader`, `Can`, `StatusTag`), hata sayfaları |
| `src/modules/<modül>` | Modül sayfaları |

## Yapay zeka

Yapay zeka özellikleri (şimdilik İşe alım → CV analizi) varsayılan olarak kapalıdır (`docs/DECISIONS.md` D24, öneri). Geliştirmede açmak için Anthropic API anahtarını user-secrets'a yazın; anahtar kaynak koda veya `appsettings` dosyalarına yazılmaz:

```bash
dotnet user-secrets --project src/Host/Merp.Api set "Ai:Anthropic:ApiKey" "<anahtar>"
```

- `appsettings.Development.json` demo tenant'ında yapay zekayı aylık 20 USD bütçeyle açar. Anahtar yoksa özellikler menüde görünmez ve API 403 `ai_disabled` döner.
- CV metni sunucuda çıkarılır; ad, iletişim bilgileri ve özel alanlar maskelendikten sonra sadece metin gönderilir. Dosya ve sonuç saklanmaz.
- Kullanım ve tahmini maliyet: Sistem → Yapay zeka kullanımı (`platform.ai_usage.read`). `ik` kullanıcısı CV analizini, `admin` her ikisini görür.
- Testler gerçek API'yi çağırmaz. Kurallar, özellik listesi ve maliyet tahmini: [`docs/domain/yapay-zeka.md`](docs/domain/yapay-zeka.md).

## Sık kullanılan komutlar

```bash
cd web && npm run gen:api                      # API çalışırken OpenAPI'den istemci üret
cd web && npm run lint && npm run typecheck && npm test

# Catalog + tüm tenant veritabanlarını migrate et (Development'ta demo tenant'ı da kataloğa ekler)
DOTNET_ENVIRONMENT=Development dotnet run --project src/Host/Merp.Migrator
DOTNET_ENVIRONMENT=Development dotnet run --project src/Host/Merp.Migrator -- --tenant demo

# Migration (modül başına ayrı DbContext ve şema)
dotnet ef migrations add <Name> \
  -p src/Modules/HR/Merp.Modules.HR.Infrastructure \
  -s src/Host/Merp.Api -c HrDbContext -o Persistence/Migrations
```

## Paket yönetimi

- NuGet sürümleri merkezi olarak `Directory.Packages.props` dosyasında tutulur. Proje dosyalarında sürüm yazılmaz.
- Her proje `packages.lock.json` üretir. CI `dotnet restore --locked-mode` ile çalışır; paket değiştirdikten sonra lock dosyalarını da commit edin.
- Yeni paket eklemeden önce lisansını kontrol edin: sadece `docs/DECISIONS.md` D19'daki lisanslar kullanılabilir.
