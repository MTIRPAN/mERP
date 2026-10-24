# Örnek içe aktarma dosyaları

`DEMO` kodlu hayali bir şirketin organizasyonu ve personeli (PLAN H4, D40). Veriler gerçek değildir; sabit tohumla [Bogus](https://github.com/bchavez/Bogus) kullanılarak üretilmiştir. T.C. kimlik numaraları algoritmaya uyar ama kimseye ait değildir.

| Dosya | İçe aktarma | İçerik |
| --- | --- | --- |
| `1-unvanlar.xlsx` | Unvanlar | 24 unvan |
| `2-birimler.xlsx` | Birimler | 71 birim: genel müdürlük, 7 direktörlük, 21 departman, 42 ekip |
| `3-pozisyonlar.xlsx` | Pozisyonlar | 147 pozisyon; her birimin bir yönetici pozisyonu var |
| `4-personel.xlsx` | Personel | 2.000 personel |

## Nasıl yüklenir

1. Organizasyon → Şirketler'de kodu `DEMO` olan bir şirket açın.
2. Dosyaları numara sırasıyla, ilgili ekrandaki "Excel'den içe aktar" ile yükleyin. Her dosya bir öncekinin kodlarına başvurur.
3. Her dosya hatasız doğrulanır; "İçe aktar" ile kaydedin. Personel dosyası 1.000 satırı aştığı için arka planda işlenir.

## Yeniden üretmek

Üretici `tests/Merp.Modules.HR.UnitTests/Imports/SampleFiles.cs`'tedir. Bir içe aktarmanın sütunları değişirse dosyaları yeniden yazın:

```bash
dotnet build tests/Merp.Modules.HR.UnitTests
tests/Merp.Modules.HR.UnitTests/bin/Debug/net10.0/Merp.Modules.HR.UnitTests -method "*WriteTheSampleFiles" -explicit only
```

`CommittedSampleFiles_AreWhatTheGeneratorWrites` testi dosyalar üreticinin çıktısından farklıysa başarısız olur.
