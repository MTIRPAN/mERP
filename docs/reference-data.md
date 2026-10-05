# Referans veriler

Migration'larla her kiracı veritabanına yüklenen hazır veriler, kaynakları ve lisansları.

## İller ve ilçeler (Platform, PLAN P1)

| | |
|---|---|
| Tablolar | `platform.provinces` (81 il), `platform.districts` (973 ilçe) |
| Migration | `20261005215414_AddCompaniesAndBranches` |
| Kaynak | [ubeydeozdmr/turkiye-api](https://github.com/ubeydeozdmr/turkiye-api), `datasets/2025/provinces.json` ve `datasets/2025/districts.json` (commit `be12165`, 2026-04-30) |
| Lisans | MIT, telif: Ubeyde Emir Özdemir (2022–2026). D19'daki izinli lisanslar arasında. |
| Kimlikler | İl: plaka kodu (1–81). İlçe: Nüfus ve Vatandaşlık İşleri ilçe kodu (1101–2107). |
| Kontrol | 81 il ve 973 ilçe; her ilin ilçe sayısı kaynaktaki `districtCount` ile aynı. Toplam, İçişleri Bakanlığı'nın resmi sayısıyla (973) aynı. |

**Güncelleme:** İl veya ilçe değişikliğinde (yeni ilçe, ad değişikliği) yeni bir migration yazılır. Mevcut migration değiştirilmez. Kullanılmayan bir ilçe silinmez, adresler onu göstermeye devam edebilir.

**Kullanım:** `GET /api/platform/locations/provinces` ve `GET /api/platform/locations/provinces/{il}/districts` (oturum yeterli, D22). Adreslerde il ve ilçe kodu (`Address.ProvinceCode`, `Address.DistrictCode`) ile adları birlikte saklanır.
