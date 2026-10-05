# Yetki, Rol ve Onay Motoru

Bu iki yapı tüm modüllerde ortaktır. Yeni bir modül yazarken önce bu belgeyi oku.

---

## 1. Yetki modeli

### 1.1 Kavramlar

| Kavram | Açıklama | Nerede |
|---|---|---|
| Permission | Kodda tanımlı yetki: `{modül}.{kaynak}.{eylem}`. Örnekler: `hr.employees.read`, `hr.leave.requests.create` | Her modülün `Permissions.cs` dosyası |
| Role | Tenant'a özel yetki kümesi. Sistem rolleri seed edilir, müşteri yenisini tanımlar. | `platform.roles`, `platform.role_permissions` |
| RoleAssignment | Kullanıcı + rol + kapsam + geçerlilik tarihleri | `platform.role_assignments` |
| Scope | Rolün hangi veride geçerli olduğu | Aşağıdaki tablo |

### 1.2 Kapsam türleri

| Tür | ScopeId | Anlamı | Tanımlayan |
|---|---|---|---|
| `Tenant` | — | Tüm şirketler | Platform |
| `Company` | CompanyId | Bir şirketin tüm verisi | Platform |
| `Branch` | BranchId | Bir şubeye bağlı kayıtlar | Platform |
| `OrgUnit` | OrgUnitId | Birim; `IncludeSubUnits` true ise alt birimleri de kapsar | HR |
| `ManagedTeam` | — | Kullanıcının doğrudan ve dolaylı astları (raporlama hattı) | HR |
| `Self` | — | Sadece kullanıcının kendisiyle ilişkili kayıtlar | Platform |

- Platform kapsamları opak saklar: tür kodu + id.
- Kapsam türleri modüller tarafından kaydedilir (`IScopeTypeRegistry`). Platform'un HR'a referansı olmaz.

### 1.3 Yetki kontrolü

1. **Endpoint seviyesi:** Endpoint `.RequirePermission(HrPermissions.Employees.Read)` ile işaretlenir. Kullanıcının bu yetkiyi içeren geçerli en az bir rol ataması yoksa yanıt 403 olur.
2. **Veri seviyesi:**
   - Handler `IAccessScopeProvider.GetScopesAsync(userId, permission)` çağırır.
   - Dönen kapsam listesini modül kendi sorgusunda predikata çevirir.
   - Örnek: HR personel listesi şu kümelerin birleşimini gösterir:
     - şirket id'leri
     - şube id'leri
     - birim alt ağacı (ltree `path <@`)
     - astlar (`reporting_lines` closure tablosu)
     - kendi personel id'si
3. **Tekil kayıt:** Kayıt kapsam dışındaysa 404 döner. 403 dönülmez; kaydın varlığı sızdırılmaz.
   - Şirket ve şubelerde (P1): Tenant kapsamı hepsini, Company kapsamı şirketi ve şubelerini, Branch kapsamı yalnızca o şubeyi yönetir. Branch kapsamıyla şirket listesinde şubenin şirketi okunabilir. Yeni şirket yalnızca Tenant kapsamıyla açılır (aksi 403); kapsam dışındaki şirkete şube açmak 403'tür.
   - Aktif şirket başlığı (`X-Company-Id`, D4) listeleri sadece daraltır; kapsamın dışına çıkarmaz.
4. **Alan seviyesi:**
   - `[Sensitive]` alanlar için ek yetki gerekir (`hr.employees.sensitive.read`).
   - Yetki yoksa değer maskelenir. Örnek: TCKN `123******01`.
5. **Görev bazlı erişim:**
   - Bekleyen veya geçmiş bir onay görevi olan kullanıcı, o belgenin özetini ve detayını görebilir; modül yetkisi gerekmez.
   - Kontrol `IApprovalAccess.HasTaskAsync(userId, documentType, documentId)` ile yapılır.

**Performans:**
- Kullanıcının etkin yetki ve kapsamları HybridCache'te tutulur. Tag: `{kiracı}:user:{id}:perm`; rol değişikliklerinde kiracı geneli `{kiracı}:perm`.
- Rol, atama, aktiflik veya organizasyon değişince tag commit'ten sonra silinir (D26).

**Geçerlilik:** Atama, başlangıç ve bitiş günleri dahil Europe/Istanbul takvimine göre bugün geçerliyse yetki verir. Pasif kullanıcının hiçbir ataması geçerli değildir.

**Yönetim:** Kullanıcı ve rol yönetimi kiracı düzeyindedir: `platform.users.*` ve `platform.roles.*` yalnızca Tenant kapsamıyla geçerlidir. Son Sistem Yöneticisi ataması kaldırılamaz (D26).

### 1.4 Sistem rolleri (seed)

| Rol | Önerilen kapsam | Yetkiler (özet) |
|---|---|---|
| Sistem Yöneticisi | Tenant | `platform.*` |
| İK Yöneticisi | Tenant / Company | `hr.*`, `recruitment.*`, hassas veri dahil |
| İK Uzmanı | Company / Branch | `hr.employees.*` (hassas hariç), `hr.leave.*`, `hr.org.read` |
| Yönetici | ManagedTeam | `hr.employees.read`, `hr.leave.requests.read`, `hr.leave.calendar.read` |
| Çalışan | Self | `hr.self.*` (kendi profili, izinleri, talepleri) |
| İşe Alım Uzmanı | Company | `recruitment.*` (hassas dahil) |
| Mülakatçı | Self | `recruitment.interviews.read` (atandıklarıyla sınırlı), `recruitment.feedback.submit` |

Sistem rolleri silinemez ama yetkileri düzenlenebilir. "Varsayılana dön" ile seed haline geri alınır.

### 1.5 Yetki tanımlama (kod)

```csharp
public static class HrPermissions
{
    public const string Module = "hr";
    public static class Employees
    {
        public const string Read = "hr.employees.read";
        public const string Create = "hr.employees.create";
        public const string Update = "hr.employees.update";
        public const string Terminate = "hr.employees.terminate";
        public const string SensitiveRead = "hr.employees.sensitive.read";
        public const string Export = "hr.employees.export";
    }
}
// Modül başlangıçta IPermissionDefinitionProvider ile ağacı kaydeder.
// Ağaçtaki her düğümün etiketi "permissions.hr.employees.read" gibi bir yerelleştirme anahtarıdır.
// Platform açılışta tanımları platform.permissions tablosuna senkronlar.
```

---

## 2. Onay motoru

### 2.1 Kavramlar

| Kavram | Açıklama |
|---|---|
| DocumentType | Modülün kaydettiği onaylanabilir belge türü: `hr.leave-request`, `recruitment.requisition`, `recruitment.offer`. Metadata: ad anahtarı, olgu şeması, bağlantı şablonu, tanım yoksa davranış. |
| Facts | Belgenin koşullarda kullanılan alanları (JSON). Örnek: `{ "leaveTypeCode": "YILLIK", "days": 7, "orgUnitId": "...", "requesterEmployeeId": "..." }`. Olgu şeması alan adı, tür ve etiket anahtarı içerir; tanım editörü bunu kullanır. |
| ApprovalDefinition | Akış tanımı: belge türü, şirket (null ise tüm şirketler), öncelik, giriş koşulu, adımlar, fallback onaylayıcı, sürüm, aktif/pasif |
| ApprovalStep | Sıra, ad (`LocalizedString`), onaylayıcı kuralı, mod (`Any`/`All`), adım koşulu, süre (saat), eskalasyon, seçenekler |
| ApprovalRequest | Çalışan örnek: belge referansı, talep eden, olgu snapshot'ı, tanımın snapshot'ı, durum, mevcut adım |
| ApprovalTask | Bir adımda bir onaylayıcıya düşen görev: atanan, durum, karar veren (vekil olabilir), karar, yorum, zaman |
| Delegation | Vekalet: asıl kullanıcı, vekil, tarih aralığı, belge türleri (boşsa tümü) |

### 2.2 Tanım seçimi

1. Belge türü eşleşen aktif tanımlar alınır.
2. Şirkete özel tanım genelden önce gelir, aynı seviyede `Priority` küçük olan önce gelir.
3. Giriş koşulu sağlanan ilk tanım seçilir.
4. Hiç tanım yoksa:
   - `AutoApproveWhenNoDefinition = true` ise talep otomatik onaylanır (gerekçe: `no_definition`).
   - Değilse talep `NeedsAttention` durumuna geçer ve Sistem Yöneticisi rolüne bildirim gider.
5. Seçilen tanımın adımları talebe snapshot olarak kopyalanır. Sonradan yapılan tanım değişiklikleri çalışan talepleri etkilemez.

### 2.3 Koşul dili

Kod çalıştırılmaz; JSON kural ağacı kullanılır:

```json
{ "all": [
  { "fact": "leaveTypeCode", "op": "in", "value": ["YILLIK", "UCRETSIZ"] },
  { "any": [
    { "fact": "days", "op": "gt", "value": 5 },
    { "fact": "isPeakSeason", "op": "eq", "value": true }
  ]}
]}
```

- Operatörler: `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `in`, `nin`, `contains`, `isEmpty`, `isNotEmpty`.
- Olgu adı ve türü belge türünün şemasına göre doğrulanır. Tanım kaydedilirken geçersiz koşul reddedilir.

### 2.4 Onaylayıcı çözücüler

| Anahtar | Parametre | Kaydeden | Sonuç |
|---|---|---|---|
| `user` | userId | Platform | Belirli kullanıcı |
| `role` | roleId | Platform | Bu role sahip olup kapsamı belgenin şirket/şube/birimini kapsayan kullanıcılar |
| `line-manager` | level (1..n) | HR | Talep eden personelin n. seviye yöneticisinin kullanıcısı |
| `org-unit-manager` | factName? | HR | Talep edenin birim yöneticisi; parametre verilirse olgudaki birimin yöneticisi |
| `position` | positionId | HR | Pozisyonun mevcut sahibinin kullanıcısı |
| `fact-user` | factName | Platform | Olgudaki kullanıcı id'si (örn. `hiringManagerUserId`) |

- Çözücüler `IApproverResolver` arayüzünü uygular ve Platform.Contracts üzerinden kaydedilir. HR kendi çözücülerini kaydeder.
- Çözücü boş dönerse sırayla şunlar denenir:
  1. Tanımdaki fallback (rol veya kullanıcı)
  2. Yoksa `NeedsAttention` durumu
- Personeli olup kullanıcısı olmayan yönetici de boş sonuç sayılır ve bir uyarı loglanır.

### 2.5 Adım seçenekleri

- **Mod:**
  - `Any`: ilk onay adımı tamamlar.
  - `All`: herkes onaylamalı; tek ret talebi reddeder.
- **Adım koşulu:** Sağlanmazsa adım atlanır ve geçmişe "atlandı" kaydı düşer.
- **`SkipIfApproverIsRequester`** (varsayılan true): Talep eden kendi onaylayıcısı çıkarsa adım atlanır. Bu durumda hat yöneticisi bir üst seviyeye çıkar.
- **`SkipIfAlreadyApproved`** (varsayılan true): Aynı kişi önceki adımda onay verdiyse tekrar sorulmaz.
- **`DueHours`** ve **`Escalation`**:
  - `notify`: hatırlatma gönderilir.
  - `escalate-to-manager`: görev onaylayıcının yöneticisine aktarılır.
  - `auto-approve` / `auto-reject`
  - Hangfire'da 15 dakikada bir çalışan iş kontrol eder.
- **`AllowReturn`:** Onaylayıcı talebi iade edebilir. Talep eden düzeltip tekrar gönderdiğinde akış 1. adımdan yeniden başlar.

### 2.6 Kararlar ve durumlar

**Talep durumları:**

```
Pending ──(tüm adımlar onay)──> Approved
   │──(ret)──────────────────> Rejected
   │──(iade)─────────────────> Returned ──(tekrar gönder)──> Pending
   │──(talep eden iptal)─────> Cancelled
   └──(çözücü boş, fallback yok)──> NeedsAttention ──(admin atama)──> Pending
```

**Kararlar:**
- Onay: yorum isteğe bağlı.
- Ret ve iade: yorum zorunlu.

**Yönetici işlemleri:** Sistem Yöneticisi görevi başka kullanıcıya atayabilir veya zorla onaylayabilir. Ayrı yetki gerekir (`platform.approvals.admin`) ve işlem audit'e yazılır.

**Vekalet:**
- Asıla düşen yeni görevler vekilin kutusunda da görünür.
- Vekil karar verirse kayıt "X adına Y" olarak düşer.
- Vekil kendi talebini onaylayamaz.

**Teknik kurallar:**
- Karar endpoint'i idempotenttir ve optimistic concurrency kullanır. Aynı göreve iki karar gelirse ikincisi 409 alır.

### 2.7 Modül entegrasyonu

```
Modül (örn. HR)                               Platform
───────────────                               ────────
LeaveRequest.Submit()
  durum = PendingApproval
  outbox: ApprovalRequested {
    DocumentType, DocumentId, CompanyId,
    RequesterUserId, RequesterEmployeeId,
    Facts, Title(key+args), Summary, Link }
                                    ───────▶  ApprovalRequest oluşturulur, görevler atanır
                                              outbox: ApprovalTaskAssigned (bildirim)
                                              ... kararlar ...
                                    ◀───────  outbox: ApprovalRequestCompleted {
                                                DocumentType, DocumentId, Outcome, DecidedAt }
Inbox handler:
  Outcome=Approved → LeaveRequest.Approve()
  Outcome=Rejected → LeaveRequest.Reject()
  Outcome=Returned → LeaveRequest.Return()
```

- **Gönderim öncesi önizleme:** `IApprovalPreview.PreviewAsync(documentType, companyId, facts, requester)` (Contracts, salt-okunur) "Bu talebi kimler onaylayacak?" listesini döner. Form ekranında gösterilir.
- **İptal:** Modül `ApprovalCancellationRequested` olayını yayınlar. Platform açık görevleri kapatır.
- **Onay kutusu:** `GET /api/platform/approvals/my-tasks` tüm modüllerin bekleyen görevlerini başlık, özet ve bağlantıyla listeler. Arayüzde üst bardaki rozet bunu gösterir.
- **Zaman çizelgesi bileşeni:** `<ApprovalTimeline documentType documentId />` her modülün detay ekranında kullanılır.

### 2.8 Varsayılan tanımlar (seed)

| Belge türü | Akış |
|---|---|
| `hr.leave-request` | 1. adım `line-manager(1)`. 2. adım "gün > 5 veya tür ÜCRETSİZ" koşuluyla `role(İK Uzmanı)`. |
| `hr.leave-cancellation` | `line-manager(1)` |
| `hr.profile-change` | `role(İK Uzmanı)` |
| `recruitment.requisition` | 1. adım `line-manager(1)`, 2. adım `role(İK Yöneticisi)` |
| `recruitment.offer` | 1. adım `fact-user(hiringManagerUserId)`, 2. adım `role(İK Yöneticisi)` |

### 2.9 Test edilmesi gereken senaryolar (zorunlu)

1. Şirkete özel tanım genel tanımı ezer.
2. Adım koşulu sağlanmayınca adım atlanır ve geçmişe "atlandı" yazılır.
3. Talep eden kendi yöneticisiyse (tepe yönetici) adım atlanır veya fallback'e gider.
4. `All` modunda 3 onaylayıcıdan 2'si onaylar, 1'i reddederse talep reddedilir.
5. Vekil asıl adına onaylar ve kayıtta iki kullanıcı da görünür.
6. Süre aşımında `escalate-to-manager` görevi doğru kişiye taşır.
7. İade edilip tekrar gönderilen talep 1. adımdan başlar ve eski kararlar geçmişte kalır.
8. Tanım güncellendikten sonra çalışan talep eski snapshot ile devam eder.
9. Çözücü boş döner, fallback yoktur; talep `NeedsAttention` olur ve admin bildirimi gider.
10. Aynı göreve eşzamanlı iki karar gelir; biri 409 alır.
11. Onay olayı iki kez teslim edilirse inbox sayesinde belge bir kez onaylanır.
