# Megadere

Danbooru 이미지를 서버 캐시에 저장하고 탐색하는 C# ASP.NET Core + Vue 앱입니다. 홈 화면은 기본적으로 `tags=all`로 `/api/posts`를 호출하고, 스크롤 하단에 닿으면 다음 `page`를 불러옵니다.
이미지는 프론트에서 Danbooru 원격 URL을 직접 표시하지 않고, 서버가 로컬 파일 캐시에 저장한 경로를 표시합니다.

## API

```http
GET /api/posts?tags=all&page=0&limit=30
```

- `tags`: Gelbooru 검색 태그입니다. 비어 있으면 `all`을 사용합니다.
- `page`: 프론트에서 쓰는 0 기반 페이지입니다. 서버가 Danbooru의 1 기반 `page`로 변환합니다.
- `limit`: 한 번에 가져올 개수입니다. 최대 100으로 제한합니다.

Danbooru 공식 엔드포인트인 `/posts.json`을 사용합니다. `tags=all`은 Danbooru에는 빈 태그 검색으로 보내 최신 전체 목록을 가져옵니다.

## 이미지 캐시

- 썸네일: `/api/posts` 호출 시 서버가 `cache/media/thumb`에 다운로드합니다. 프론트의 `previewUrl`은 `/media-cache/thumb/...` 로컬 경로입니다.
- 원본: 카드 클릭 시 `/api/media/original/{id}`가 원본 파일을 `cache/media/original`에 다운로드한 뒤 캐시 파일로 리다이렉트합니다.
- 갤러리 캐시: 저장된 갤러리에서 조회/자동 캐시된 파일은 `cache/media/galleries/{galleryId}/thumb`, `cache/media/galleries/{galleryId}/original`에 별도로 저장합니다.
- 캐시 위치는 기본적으로 `cache/media`이며, `Cache:MediaPath` 설정으로 바꿀 수 있습니다.

## 갤러리 자동 캐시

검색 결과를 갤러리에 추가하면 서버가 `data/galleries.json`에 태그를 저장합니다. 백그라운드 작업은 저장된 갤러리를 주기적으로 순회하면서 다음 페이지를 계속 캐시하고, 전체 페이지를 끝까지 캐시하면 이후 새 포스트가 있는지 첫 페이지를 확인합니다.

```json
{
  "Gallery": {
    "RefreshMinutes": 15,
    "ActiveCacheSeconds": 5,
    "PreloadLimit": 40,
    "PagesPerCycle": 3,
    "MaxCatchupPages": 25,
    "PageDelayMs": 250,
    "CacheOriginals": false
  }
}
```

갤러리 자동 캐시는 기본적으로 썸네일만 로컬 캐시에 저장합니다. 원본 파일까지 미리 받고 싶으면 `CacheOriginals`를 `true`로 바꿀 수 있지만, 디스크/네트워크/CPU 사용량이 크게 늘어납니다. 전체 캐시가 끝나지 않은 갤러리는 `ActiveCacheSeconds` 간격으로 계속 이어서 캐시하고, 완료된 갤러리는 `RefreshMinutes` 간격으로 새 포스트를 확인합니다. 전체 캐시가 끝난 갤러리는 첫 페이지의 마지막 포스트 ID를 경계값으로 기억하고, 새 포스트가 감지되면 그 경계값이 다시 나올 때까지 앞쪽 페이지를 따라가며 누락 없이 갱신합니다.

갤러리 화면의 포스트 목록은 Danbooru를 직접 다시 조회하지 않고 `data/gallery-posts/{galleryId}.json`에 저장된 캐시 메타데이터를 읽습니다. 이 파일은 백그라운드 캐시 작업이 페이지를 받을 때마다 갱신합니다.

## 실행

```bash
dotnet run
```

이 머신에서는 Homebrew `dotnet@8`이 설치되어 있으므로 PATH에 `dotnet`이 없다면 아래처럼 실행할 수 있습니다.

```bash
DOTNET_CLI_HOME="$PWD/.dotnet-home" \
DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
/opt/homebrew/opt/dotnet@8/bin/dotnet run
```

Danbooru API 인증이 필요해지면 환경변수 또는 ASP.NET 설정으로 지정합니다.

```bash
DANBOORU_USERNAME="..." DANBOORU_API_KEY="..." dotnet run
```

또는 `appsettings.Local.json`을 만들어서 넣을 수 있습니다. 이 파일은 `.gitignore`에 들어가 있으니 개인 키를 넣어도 커밋되지 않습니다.

```json
{
  "Danbooru": {
    "Username": "...",
    "ApiKey": "...",
    "Cookie": "",
    "UserAgent": "",
    "ProxyUrl": ""
  }
}
```

형식은 `appsettings.Local.example.json`을 참고하면 됩니다.

테스트용 프록시나 미러를 쓰려면 `Danbooru:BaseUrl` 설정을 바꿀 수 있습니다.

Danbooru HTTPS가 현재 네트워크에서 reset되는 경우에는 먼저 서버가 안정적인 HTTP/1.1 경로로 요청합니다. 그래도 reset이 계속되면 `ProxyUrl`에 프록시를 넣을 수 있습니다.

```json
{
  "Danbooru": {
    "ProxyUrl": "http://127.0.0.1:7890"
  }
}
```

브라우저에서는 열리지만 서버에서 Cloudflare 검증 때문에 실패하는 경우에는 브라우저의 요청 헤더에서 `Cookie`와 `User-Agent`를 복사해 `appsettings.Local.json`에 넣을 수 있습니다. 쿠키는 만료될 수 있어서 장기적인 해결책은 아닙니다.

```json
{
  "Danbooru": {
    "Cookie": "cf_clearance=...; ...",
    "UserAgent": "Mozilla/5.0 ..."
  }
}
```

서버 통신은 `IHttpClientFactory` 기반으로 수행하고, 일시적인 429/5xx/타임아웃에는 서버 내부 재시도 로직을 적용합니다. Chrome 원격 디버깅이나 브라우저 프로필을 통신 경로로 사용하지 않습니다.
