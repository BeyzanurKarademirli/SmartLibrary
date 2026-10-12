# SmartLibrary

SmartLibrary, React + TypeScript arayüzü ve ASP.NET Core API'si ile PostgreSQL üzerinde çalışan, katmanlı bir kütüphane otomasyonu başlangıç sürümüdür.

## Hızlı başlangıç (Docker)

1. `.env.example` dosyasını `.env` olarak kopyalayın.
2. `.env` içindeki `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY` ve `SEED_ADMIN_PASSWORD` değerlerini değiştirin. JWT anahtarı en az 32 byte olmalıdır.
3. Repository kökünde `docker compose up --build` komutunu çalıştırın.
4. Arayüz: <http://localhost:5173> · API: <http://localhost:5180> · Swagger: <http://localhost:5180/swagger>

İlk açılışta veritabanı migration'ları uygulanır ve örnek katalog eklenir. Yönetici e-postası/parolası `.env` dosyasındaki `SEED_ADMIN_EMAIL` ve `SEED_ADMIN_PASSWORD` değerleridir. Üyeler arayüzden kayıt olabilir. Yönetici API aracılığıyla kütüphaneci hesabı ekleyebilir.

## Geliştirme komutları

Backend, PostgreSQL çalışırken `dotnet run --project backend/src/SmartLibrary.Api` ile başlatılabilir. Frontend için `cd frontend`, `npm ci`, `npm run dev` komutlarını kullanın. Frontend API adresi `VITE_API_BASE_URL` ile ayarlanır; varsayılan `http://localhost:5180` değeridir.

Migration eklemek için repository kökünde `dotnet tool restore` ve `dotnet tool run dotnet-ef migrations add MigrationName --project backend/src/SmartLibrary.Infrastructure --startup-project backend/src/SmartLibrary.Api --output-dir Persistence/Migrations` komutlarını kullanın.

Unit ve API integration testlerini çalıştırmak için `dotnet test SmartLibrary.sln -c Release` komutunu kullanın. Frontend üretim derlemesi `cd frontend && npm ci && npm run build` ile alınır.

## İlk sürüm kapsamı

- Kitap arama, kategori filtreleme, kitap ayrıntısı, yazar/künye bilgileri ve fiziksel kopya barkodları
- Üye kayıt/giriş, parola hash'i, JWT ve Admin/Librarian/Member rol politikaları
- Üye ödünçleri, uzatma, favoriler, rezervasyon kuyruğu ve gecikme cezası kayıtları
- Personel paneli, ödünç verme/iade, üye arama ve dashboard özetleri
- Dönemsel raporlar: ödünç/iade, yeni üyeler, tahsilat, popüler kitaplar ve geciken ödünçler; CSV dışa aktarımı
- Admin için kalıcı kütüphane kuralları: ödünç süresi, üye limiti, günlük gecikme bedeli ve rezervasyon teslim süresi
- Desteklenen tarayıcılarda kamera ile ISBN/barkod/QR okuma; desteklenmeyen tarayıcılarda manuel giriş
- Personel için yazar, yayınevi, kategori ve raf katalog listeleri; kitap/kopya formlarında otomatik öneri
- Yazar-kitabı çoktan-çoğa; kategori/yayınevi-kitap ve raf-kopya ilişkileri FK ile korunur. Eski metin künyeleri geçiş sırasında korunup ilişkilendirilir.
- Kitap sayfa sayısı künyeye kaydedilir ve kitap ayrıntısında gösterilir.
- İlk kurulumda örnek kataloğa Open Library Covers API ISBN URL'leriyle 10 kitap eklenir; var olan katalog silinmez veya sıfırlanmaz
- Yönetici için personel hesabı oluşturma ve audit kayıtları
- EF Core migration'ları, PostgreSQL, Docker Compose ve Swagger

## İş kuralı varsayımları

- Ödünç süresi 14 gün; bir ödünç bir kez 14 gün uzatılabilir.
- Üye aynı anda en fazla 5 kitap alabilir; ödenmemiş ceza varsa yeni ödünç verilemez.
- Gecikme cezası her gün için 5 TL olarak kaydedilir. Ödeme personel API'si üzerinden kısmi/tam tutar olarak işlenir.
- Hazır rezervasyon için teslim alma süresi 2 gündür.
- Üyelik herkese açık kayıtla açılır; rol daima Member atanır. Personel hesabı yalnızca Admin oluşturabilir.
- Bildirimler uygulama içinde tutulur; SMTP ayarlandığında rezervasyon hazır bildirimi ve vade yaklaşma hatırlatması e-posta ile de gönderilir.
- Gmail SMTP için `.env` içinde `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM_ADDRESS` ve `SMTP_FROM_NAME` ayarlanır. Normal hesap parolası yerine Google Uygulama Şifresi kullanılır; SMTP alanları boşsa e-posta gönderimi kapalı kalır.
- Arayüz Türkçe/İngilizce ve açık/koyu tema tercihlerini tarayıcıda saklar.
- Üye ve personel için Gemini kütüphane asistanı eklidir. `GEMINI_API_KEY` sunucu ortam değişkenine eklenmeden chatbot yanıt vermez; anahtar frontend'e gönderilmez.
- Yönetici > Sistem ayarları üzerinden kural değiştirilebilir; aralıklar API tarafında doğrulanır ve audit kaydı tutulur.
- Rapor API'si en fazla 366 günlük dönem kabul eder; rapor ve ayar endpoint'leri personel/yönetici politikalarıyla korunur.
- Kapaklar dikey kapak oranını koruyan `contain` çerçevesinde gösterilir; kırpılmaz.

## Katmanlar

```text
frontend/                         React + TypeScript + Tailwind CSS
backend/src/SmartLibrary.Domain/  Entity ve domain enum'ları
backend/src/SmartLibrary.Application/ İş akışları ve uygulama kuralları
backend/src/SmartLibrary.Infrastructure/ EF Core ve PostgreSQL eşlemesi
backend/src/SmartLibrary.Api/     HTTP API, JWT, Swagger ve DI
```

## Güvenlik ve üretim notları

Parolalar ASP.NET Identity `PasswordHasher` ile hash'lenir; uygulama gerçek ödeme bilgisi saklamaz. JWT imzalama anahtarı ve yönetici parolası environment secret olarak verilmelidir. Demo compose Swagger'ı kolay erişim için Development modunda açar; internete açık dağıtım öncesi üretim ayarları, HTTPS/reverse proxy, refresh-token oturum yönetimi, rate limiting, e-posta doğrulaması, yedekleme ve gizli bilgi yönetimi tamamlanmalıdır. JWT mevcut ilk sürümde tarayıcı localStorage'ında tutulur.

## Başlıca uç noktalar

- `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me`
- `GET /api/books?q=&category=&page=&pageSize=`, `GET /api/books/{id}`
- `POST /api/books`, `PUT /api/books/{id}`, `POST /api/books/{id}/copies` (Admin/Librarian)
- `/api/member/loans`, `/api/member/reservations`, `/api/member/favorites`, `/api/member/fines`
- `/api/staff/loans`, `/api/staff/members`, `/api/staff/dashboard`, `/api/staff/fines` (Admin/Librarian)
- `POST /api/admin/staff`, `GET /api/staff/audit` (Admin)
- `POST /api/admin/email/test` (Admin; test iletisini oturum açmış yönetici adresine gönderir)
- `POST /api/assistant/chat` (oturum açmış kullanıcı; Gemini API anahtarı gerekir)
