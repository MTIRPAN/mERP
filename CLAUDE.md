# mERP

Holdingler ve büyük şirketler için ERP ürünü. Modüler monolit (.NET 10) + React.
İlk hedef: Platform çekirdeği → İK (organizasyon, personel, izin, işe alım). Bordro bu aşamada YOK.

## Önce oku

- `docs/PLAN.md` — görevler, sıra, kabul kriterleri. Her oturumda ilk işaretlenmemiş görevi al.
- `docs/DECISIONS.md` — mimari kararlar, izinli ve yasaklı kütüphaneler. Bunlara aykırı kod yazma.
- `docs/domain/yetki-ve-onay.md` — yetki/rol/veri kapsamı modeli ve onay motoru kuralları.
- `docs/domain/izin-kurallari.md` — izin mevzuatı varsayılanları, politika parametreleri, test senaryoları.

## Çalışma şekli

- Bir görev = bir branch = bir PR. Branch adı: `feat/<görev-id>-<kısa-ad>` (örn. `feat/p2-roles`).
- PR açmadan önce Definition of Done'ın tamamı sağlanmış olmalı (aşağıda).
- Görev bitince `docs/PLAN.md`'deki kutuyu aynı PR'da işaretle.
- Görev çok büyükse alt PR'lara böl, PLAN.md'ye alt madde ekle.
- Planda olmayan bir mimari karar gerekiyorsa sessizce karar verme. `docs/DECISIONS.md`'ye öneri ekle ve kullanıcıya sor.
- Yeni NuGet/npm paketi eklemeden önce lisansını kontrol et. Sadece `docs/DECISIONS.md` D19'daki izinli lisanslar kullanılabilir. Listede olmayan paket için kullanıcıya sor.
- Commit mesajları İngilizce, Conventional Commits: `feat(hr): add leave request approval`.

## Komutlar

```bash
docker compose up -d                                  # postgres, valkey, rabbitmq, keycloak, mailpit, gotenberg, aspire-dashboard
dotnet build -warnaserror
dotnet test                                           # unit + integration (Testcontainers, Docker gerekir)
dotnet run --project src/Host/Merp.Api                # API: https://localhost:5001, dokümantasyon: /scalar
cd web && npm install && npm run dev                  # Web: http://localhost:5173
cd web && npm run gen:api                             # API çalışırken OpenAPI'den istemci üret
cd web && npm run lint && npm run typecheck && npm test

# Migration (modül başına ayrı DbContext ve şema)
dotnet ef migrations add <Name> \
  -p src/Modules/HR/Merp.Modules.HR.Infrastructure \
  -s src/Host/Merp.Api -c HrDbContext -o Persistence/Migrations
```

Geliştirme kullanıcıları (Keycloak realm importundan gelir): `admin / admin` (tenant admin), `ik / ik`, `yonetici / yonetici`, `calisan / calisan`.

## Proje yapısı

```
src/
  Host/Merp.Api                      # tek deploy edilen uygulama, composition root
  Host/Merp.Migrator                 # catalog + tüm tenant DB'lerini migrate eden konsol
  BuildingBlocks/
    Merp.SharedKernel                # Entity, AggregateRoot, ValueObject, Result, Error, ortak value object'ler
    Merp.Application.Abstractions    # ICommand/IQuery, handler'lar, ICurrentUser, ITenantContext, IPermissionChecker
    Merp.Infrastructure              # EF tabanı, interceptor'lar, outbox/inbox, mediator, bus, cache, job, dosya, PDF
    Merp.Web                         # endpoint yardımcıları, ProblemDetails, izin filtresi, IModule
  Modules/
    Platform/  Merp.Modules.Platform.{Domain,Application,Infrastructure,Api,Contracts}
    HR/        Merp.Modules.HR.{Domain,Application,Infrastructure,Api,Contracts}
    Recruitment/ Merp.Modules.Recruitment.{Domain,Application,Infrastructure,Api,Contracts}
tests/
  Merp.Testing                       # ortak test altyapısı (WebApplicationFactory, Testcontainers, builder'lar)
  Merp.ArchitectureTests
  Merp.Modules.<Modül>.UnitTests
  Merp.Modules.<Modül>.IntegrationTests
web/                                 # React + TS (Vite)
  src/app  src/shared  src/modules/{platform,hr,recruitment}
deploy/                              # docker-compose ekleri, keycloak realm, ileride helm
docs/
```

## Mimari kurallar (mimari testlerle denetlenir)

1. Bir modül başka bir modülün yalnızca `*.Contracts` projesine referans verebilir. Domain/Application/Infrastructure'a asla.
2. Her modülün kendi PostgreSQL şeması ve kendi `DbContext`'i vardır (`platform`, `hr`, `recruitment`). Başka şemaya sorgu, join veya foreign key yok. Başka modülün kimlikleri düz `Guid` olarak tutulur.
3. Modüller arası yazma işlemleri entegrasyon olaylarıyla (outbox) yapılır. Senkron çağrı sadece `Contracts` içindeki salt-okunur sorgu arayüzleriyle (`IOrgQuery`, `IApprovalPreview` gibi) yapılır.
4. Domain katmanı EF Core, ASP.NET veya altyapı paketlerine referans vermez.
5. Application katmanı Infrastructure'a referans vermez.
6. Her endpoint bir yetki (permission) ister. İstisnalar `AllowAnonymous` ile işaretlenir ve `docs/DECISIONS.md`'deki listede olmalıdır (kariyer sayfası, health).
7. Bir komut tek aggregate değiştirir. Aynı modüldeki diğer aggregate'ler domain event ile güncellenir.

## Kod kuralları

- Kod, yorum, log ve commit İngilizce. Kullanıcıya görünen HER metin yerelleştirme anahtarıdır. Türkçe metin sadece `tr-TR.json` dosyalarında bulunur.
- Kültür güvenliği (Türkçe İ/ı sorunu): kodlar ve anahtarlar için `ToUpperInvariant`/`ToLowerInvariant`, karşılaştırmalarda `StringComparison.Ordinal(IgnoreCase)`. CA1304/CA1305/CA1309/CA1311 hata seviyesindedir.
- Kimlikler: `Guid.CreateVersion7()`, strongly typed (`readonly record struct EmployeeId(Guid Value)`).
- Tarih ve zaman:
  - Takvim tarihi için `DateOnly`, an için `DateTimeOffset` (UTC saklanır).
  - Gün sınırları şirketin saat dilimine göre hesaplanır (varsayılan `Europe/Istanbul`).
  - `DateTime.Now` yasak, `TimeProvider` kullan.
- Para ve oran için `decimal`.
- Hatalar:
  - Beklenen iş hataları exception değil `Result`/`Error` ile döner. `Error` bir kod (`hr.leave.insufficient_balance`) ve parametreler taşır.
  - API katmanı bunu yerelleştirilmiş ProblemDetails'e çevirir.
- Sorgular DTO'ya `Select` ile projekte edilir. Gerekirse Mapperly kullanılır, AutoMapper kullanılmaz.
- Listeleme sözleşmesi: `page`, `pageSize` (en fazla 200), `sort`, `filter` → `PagedResult<T>`. Tüm listeler veri kapsamı filtresinden geçer.
- Hassas alanlar `[Sensitive]` ile işaretlenir:
  - İlgili yetki yoksa DTO'da maskelenir.
  - Okunmaları audit'e yazılır (KVKK).
- Endpoint yolu: `/api/{module}/{resource}`. Minimal API endpoint grupları kullanılır, her modül kendi `IModule.MapEndpoints` metodunda tanımlar.
- Frontend:
  - Sunucu durumu için TanStack Query, API istemcisi orval ile üretilir (elle fetch yazma).
  - UI bileşenleri Ant Design.
  - Metinler `t('...')` ile, tarih ve sayılar `Intl`/dayjs ile formatlanır.

## Definition of Done (her PR)

- [ ] `dotnet build -warnaserror` uyarısız, `dotnet test` yeşil, web'de `lint`, `typecheck`, `test` ve `build` yeşil.
- [ ] Domain kuralları için unit test yazıldı.
- [ ] Her yeni endpoint için integration test yazıldı: başarılı akış, 400 (validasyon), 403 (yetkisiz), veri kapsamı dışı kayıt.
- [ ] Migration eklendi ve boş veritabanında sorunsuz çalışıyor.
- [ ] Yeni yetkiler tanımlandı ve sistem rollerine seed edildi.
- [ ] Yeni metinler `tr-TR.json`'a eklendi, hard-coded metin yok.
- [ ] OpenAPI güncel, web istemcisi yeniden üretildi.
- [ ] Yeni aggregate'ler audit'e dahil.
- [ ] `docs/PLAN.md` işaretlendi. Davranış değiştiyse ilgili doküman güncellendi.
