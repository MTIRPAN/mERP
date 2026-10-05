# Yeni modül ekleme

Bu tarif `Platform` modülünün yapısını örnek alır. Aşağıda `<Mod>` modülün C# adı (örn. `HR`, `Recruitment`), `<mod>` ise şema ve yetki öneki olarak kullanılan küçük harfli adıdır (örn. `hr`, `recruitment`). İsimlendirme kuralları için `docs/DECISIONS.md` D2'ye bakın.

## 1. Projeler

`src/Modules/<Mod>/` altında beş proje açılır:

| Proje | SDK | Referanslar |
|---|---|---|
| `Merp.Modules.<Mod>.Domain` | `Microsoft.NET.Sdk` | `Merp.SharedKernel` |
| `Merp.Modules.<Mod>.Contracts` | `Microsoft.NET.Sdk` | `Merp.Application.Abstractions` |
| `Merp.Modules.<Mod>.Application` | `Microsoft.NET.Sdk` | kendi `Domain` ve `Contracts`, `Merp.Application.Abstractions`, gerekirse **başka modüllerin yalnızca `Contracts`** projeleri |
| `Merp.Modules.<Mod>.Infrastructure` | `Microsoft.NET.Sdk` | kendi `Application`, `Merp.Infrastructure` |
| `Merp.Modules.<Mod>.Api` | `Microsoft.NET.Sdk` + `FrameworkReference Microsoft.AspNetCore.App` | kendi `Application` ve `Infrastructure`, `Merp.Web` |

```bash
M=HR
for layer in Domain Contracts Application Infrastructure Api; do
  dotnet new classlib -n Merp.Modules.$M.$layer -o src/Modules/$M/Merp.Modules.$M.$layer
  rm src/Modules/$M/Merp.Modules.$M.$layer/Class1.cs
  dotnet sln Merp.slnx add src/Modules/$M/Merp.Modules.$M.$layer
done
```

`dotnet new classlib` proje dosyasına `TargetFramework`, `Nullable` ve `ImplicitUsings` yazar. Bunlar `Directory.Build.props`'tan geldiği için proje dosyasından silin. Paket sürümleri proje dosyasına yazılmaz; `Directory.Packages.props`'a eklenir.

**Yasaklar** (mimari testler denetler, `CLAUDE.md` kural 1–5):
- Başka modülün `Domain`, `Application`, `Infrastructure` veya `Api` projesine referans verilmez.
- `Domain` EF Core, ASP.NET veya altyapı paketlerine referans vermez.
- `Application` `Infrastructure`'a referans vermez.

## 2. Host'a kayıt

- `src/Host/Merp.Api/Merp.Api.csproj`: `Merp.Modules.<Mod>.Api` referansı.
- `src/Host/Merp.Migrator/Merp.Migrator.csproj`: `Merp.Modules.<Mod>.Infrastructure` referansı.
- `Program.cs`: modül `IModule` listesine açıkça eklenir (0.9 ile gelir).

## 3. Modül içeriği

| Ne | Nerede | Not |
|---|---|---|
| `IModule` uygulaması (`<Mod>Module`) | `Api` | Servis kaydı ve `MapEndpoints`. Endpoint grubu `/api/<mod>`. |
| Yetkiler (`<Mod>Permissions`) | `Application` | `{modül}.{kaynak}.{eylem}`; `IPermissionDefinitionProvider` ile kaydedilir. |
| `DbContext` (`<Mod>DbContext`) | `Infrastructure/Persistence` | `ModuleDbContext`'ten türer, şema `<mod>`. Migration'lar `Persistence/Migrations`. |
| Entegrasyon olayları ve salt-okunur sorgu arayüzleri | `Contracts` | Başka modüller sadece bunları görür. |
| Varsayılan metinler | `Api/Localization/tr-TR.json` (gömülü kaynak) | Anahtarlar `<mod>.` ile başlar (D11). |

## 4. Testler

`tests/` altında iki proje açılır:
- `Merp.Modules.<Mod>.UnitTests`: kendi `Domain` ve `Application` referansları.
- `Merp.Modules.<Mod>.IntegrationTests`: `Merp.Testing` referansı.

Proje adı `Tests` ile bittiği için xunit, Shouldly, NSubstitute ve coverlet `tests/Directory.Build.props`'tan otomatik gelir.

## 5. Migration

```bash
dotnet ef migrations add Initial \
  -p src/Modules/<Mod>/Merp.Modules.<Mod>.Infrastructure \
  -s src/Host/Merp.Api -c <Mod>DbContext -o Persistence/Migrations
```

## 6. Kontrol listesi

- [ ] `dotnet build -warnaserror` ve `dotnet test` yeşil (mimari testler dahil).
- [ ] Modül şeması ve tabloları boş veritabanında migration ile oluşuyor.
- [ ] Yetkiler sistem rollerine seed edildi.
- [ ] Menü öğeleri yetkiye bağlı kaydedildi.
- [ ] `CLAUDE.md` proje yapısı ve `docs/DECISIONS.md` D2 modül listesi güncellendi.
