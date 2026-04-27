# GitUI

Windows용 GitHub 리포지토리 관리 GUI 도구. WPF + ModernWpfUI(윈11 Fluent 스타일).

## 기능

### 리포지토리 관리
- 새 리포지토리 생성 (.gitignore·라이선스 템플릿 선택)
- **드래그한 폴더 → 새 리포 즉시 생성** — 메인 창에 폴더를 던지면 다이얼로그에서 이름 확인 후 자동 생성+초기 업로드
- 리포 검색/필터/정렬
- 리포 단건 삭제, **다중 선택 후 일괄 archive/delete**
- 공개여부 토글, 아카이브, GitHub Pages 활성/비활성
- Release/Tag 생성

### 업로드
- **드래그&드롭 업로드** — 파일과 폴더 혼합 가능
- **클립보드 이미지 페이스트 (Ctrl+V)** — 스크린샷이 `screenshots/{timestamp}.png`로 자동 커밋
- **폴더 동기화** — 선택한 폴더 → 한 번에 업로드
- **동기화 미리보기 (dry-run)** — 추가/수정/스킵 분류 표시 후 확인하면 실행
- **로컬 .gitignore 존중** — 폴더 안의 .gitignore 파싱하여 제외 패턴 적용
- **자동 폴더 감시 (auto-sync)** — `FileSystemWatcher` + 디바운스. 파일 바뀌면 자동으로 변경분만 푸시

### 브랜치/커밋
- 브랜치 선택 드롭다운 + 새 브랜치 생성
- 모든 업로드/동기화 작업이 선택된 브랜치 기준
- 커밋 히스토리 뷰어 (최근 30개, 더블클릭 → 브라우저 열기)

### README / Issues / PRs
- README.md 인라인 편집 (텍스트 에디터 + 저장 시 자동 커밋)
- Issues / PullRequests 목록 (Open/Closed 토글, 더블클릭 → 브라우저 열기)

### UX
- **명령 팔레트 (Ctrl+P)** — 리포 빠른 점프 + 명령 실행
- **키보드 단축키**
  - `Ctrl+P` 명령 팔레트
  - `Ctrl+N` 새 리포지토리
  - `Ctrl+R` 리포 새로고침
  - `Ctrl+T` 다크/라이트 테마 전환
  - `Ctrl+V` (Upload 탭에서) 클립보드 이미지 업로드
- **시스템 트레이 아이콘** — 최소화 시 트레이로, 더블클릭으로 복원
- **다크/라이트 테마**

### 인증 (3가지, 모두 DPAPI 영구 저장)
1. **GitHub 계정으로 로그인 (OAuth Web Flow)** — 브라우저 자동, "Authorize" 한 번
2. **Device 코드 로그인 (OAuth Device Flow)** — Client Secret 없이 가능
3. **Personal Access Token (PAT)** — 즉시 사용

토큰 + OAuth 설정 모두 `%APPDATA%\GitUI\` 안에 Windows DPAPI로 암호화 저장.

## 빌드 / 실행

요구사항: **.NET 8 SDK** (Windows)

```bash
cd GitUI
dotnet run
```

또는 Visual Studio 2022에서 `GitUI.sln` 열고 F5.

배포용 단일 실행파일:

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## OAuth App 등록 (Web Flow / Device Flow 사용 시)

1. https://github.com/settings/applications/new
2. Application name: `GitUI`
3. Homepage URL: `http://localhost`
4. **Authorization callback URL**: `http://localhost:8765/callback`
5. 등록 후 OAuth App 설정에서 **Enable Device Flow** 체크 (Device Flow도 쓰려면)
6. 발급된 Client ID + Client Secret을 앱의 OAuth App 설정에 입력

PAT만 쓸 거면 등록 불필요. https://github.com/settings/tokens/new 에서 `repo` + `delete_repo` 스코프로 발급.

## 프로젝트 구조

```
GitUI/
├── App.xaml(.cs)
├── MainWindow.xaml(.cs)         쉘 (사이드바 + 컨텐츠 + 드롭존 + 트레이)
├── Models/RepoItem.cs
├── Services/
│   ├── TokenStorage.cs              DPAPI 암호화 토큰
│   ├── OAuthConfig.cs               OAuth Client ID/Secret 저장
│   ├── OAuthWebFlowAuthenticator    HttpListener 기반 Web Flow
│   ├── DeviceFlowAuthenticator      Device Flow 폴링
│   ├── GitHubService                Octokit 래퍼 (모든 GitHub API)
│   ├── GitignoreMatcher             .gitignore 패턴 매칭
│   ├── SyncPreview                  로컬 vs 원격 SHA diff
│   └── FolderWatcherService         FileSystemWatcher + 디바운스
├── ViewModels/
│   ├── MainViewModel                전체 상태
│   ├── LoginViewModel               3가지 로그인 방식
│   ├── CreateRepoViewModel          새 리포지토리
│   ├── RepoDetailViewModel          탭 컨테이너
│   ├── CommandPaletteViewModel      Ctrl+P 검색
│   └── Tabs/
│       ├── UploadTabViewModel       드롭존 + 폴더 동기화 + dry-run
│       ├── WatchTabViewModel        자동 감시
│       ├── ReadmeTabViewModel       README 에디터
│       ├── HistoryTabViewModel      커밋 히스토리
│       ├── IssuesTabViewModel       Issues/PRs
│       └── SettingsTabViewModel     visibility/archive/pages/release
├── Views/
│   ├── LoginView                    3-state 로그인
│   ├── CreateRepoView               새 리포 폼
│   ├── RepoDetailView               6-탭 컨트롤
│   ├── CommandPalette               Ctrl+P 오버레이
│   ├── Tabs/                        탭별 UserControl
│   └── Dialogs/
│       ├── SyncPreviewDialog        Dry-run 확인
│       └── CreateRepoFromFolderDialog  드롭→새 리포
└── Converters/Converters.cs
```

## 알려진 제한

- 폴더 동기화는 **upsert만** — 원격에서만 존재하는 파일을 삭제하지 않음
- 큰 파일(>100MB)은 GitHub Contents API로 업로드 불가 (Git LFS 필요) — dry-run에서 자동으로 Skipped로 표시
- Pages 활성화는 default branch + `/` 경로 기준
- README 편집 시 충돌 감지 없음 (마지막 저장이 이김)
