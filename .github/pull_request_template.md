## Görev

<!-- PLAN.md görev numarası ve adı, örn. "P2a — Kullanıcılar, roller (backend)" -->

## Değişiklikler

-

## Definition of Done

- [ ] `dotnet build -warnaserror` uyarısız, `dotnet test` yeşil; web'de `lint`, `typecheck`, `test` ve `build` yeşil
- [ ] Domain kuralları için unit test yazıldı
- [ ] Her yeni endpoint için integration test yazıldı: başarılı akış, 400 (validasyon), 403 (yetkisiz), veri kapsamı dışı kayıt
- [ ] Migration eklendi ve boş veritabanında sorunsuz çalışıyor
- [ ] Yeni yetkiler tanımlandı ve sistem rollerine seed edildi
- [ ] Yeni metinler `tr-TR.json`'a eklendi, hard-coded metin yok
- [ ] OpenAPI güncel, web istemcisi yeniden üretildi
- [ ] Yeni aggregate'ler audit'e dahil
- [ ] `docs/PLAN.md` işaretlendi; davranış değiştiyse ilgili doküman güncellendi
- [ ] Yeni paketlerin lisansı `docs/DECISIONS.md` D19'a uygun

## Nasıl denenir

<!-- Demo adımları, ekran görüntüsü -->
