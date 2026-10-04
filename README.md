# mERP

Holdingler ve büyük şirketler için ERP ürünü. Modüler monolit (.NET 10) + React.

İlk hedef: Platform çekirdeği → İK (organizasyon, personel, izin, işe alım).

## Belgeler

| Belge | İçerik |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Çalışma kuralları, proje yapısı, mimari ve kod kuralları, Definition of Done |
| [`docs/PLAN.md`](docs/PLAN.md) | Görevler, sıra ve kabul kriterleri |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Mimari kararlar, izinli ve yasaklı kütüphaneler |
| [`docs/domain/`](docs/domain) | Alan kuralları (yetki ve onay, izin mevzuatı) |

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

Geliştirme kullanıcıları (Keycloak realm importundan gelir): `admin / admin`, `ik / ik`, `yonetici / yonetici`, `calisan / calisan`.

## Sık kullanılan komutlar

```bash
cd web && npm run gen:api                      # API çalışırken OpenAPI'den istemci üret
cd web && npm run lint && npm run typecheck && npm test

# Migration (modül başına ayrı DbContext ve şema)
dotnet ef migrations add <Name> \
  -p src/Modules/HR/Merp.Modules.HR.Infrastructure \
  -s src/Host/Merp.Api -c HrDbContext -o Persistence/Migrations
```

## Paket yönetimi

- NuGet sürümleri merkezi olarak `Directory.Packages.props` dosyasında tutulur. Proje dosyalarında sürüm yazılmaz.
- Her proje `packages.lock.json` üretir. CI `dotnet restore --locked-mode` ile çalışır; paket değiştirdikten sonra lock dosyalarını da commit edin.
- Yeni paket eklemeden önce lisansını kontrol edin: sadece `docs/DECISIONS.md` D19'daki lisanslar kullanılabilir.
