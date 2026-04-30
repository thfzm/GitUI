# GitUI

Windows용 GitHub 통합 관리 도구. WPF + ModernWpfUI(윈11 Fluent 스타일) + Octokit + LibGit2Sharp.

웹사이트 안 가도 GitHub 작업 거의 다 됩니다 — 리포 생성/관리, 업로드, 자동 동기화, 클론, 외부 리포 탐색, 릴리스, Pages, Issues, README 편집까지.

## 핵심 차별점

- **풀오토 자동 감시** — 한 번 설정하면 창 닫아도, 컴퓨터 재부팅해도 자동으로 동기화 계속됨
- **드래그한 폴더 → 새 리포 즉시 생성** — 메인 창에 폴더 던지면 이름 확인 후 끝
- **외부 리포 탐색** — GitHub 전체 검색 → 파일/README/히스토리/이슈 다 보고 클론까지
- **단일 .exe 배포** — .NET 설치 없이 71MB 파일 하나로 실행

---

## 기능

### 리포지토리 관리
- 새 리포지토리 생성 — 이름·설명·private + **언어별 프로젝트 스타터 자동 생성** (Hello World 템플릿)
- 동적으로 가져오는 **.gitignore 템플릿**(GitHub API) + **라이선스 템플릿**(MIT/Apache/GPL/BSD/Unlicense 등)
- **드래그한 폴더 → 새 리포 즉시 생성**(다이얼로그 한 번)
- 리포 검색·필터·정렬
- 단건 삭제 + **다중 선택 후 일괄 archive/delete**
- 공개여부 토글, 아카이브, 포크, **Star/Unstar**

### 외부 리포 탐색 (검색 → 자세히 보기)
- GitHub 전체 공개 리포 **검색** (언어 필터, 정렬: stars/updated/forks)
- 결과 카드의 **"자세히"** 버튼으로 그 리포 상세 뷰 진입 (소유 안 해도 됨)
- 파일 트리, README, 커밋 히스토리, Issues/PRs 다 열람
- 그 자리에서 ⭐ Star, 🍴 Fork, 📥 Clone

### 업로드 / 동기화
- **드래그&드롭 업로드** — 파일과 폴더 혼합 가능
- **클립보드 이미지 페이스트 (Ctrl+V)** — 스크린샷이 `screenshots/{timestamp}.png`로 자동 커밋
- **폴더 동기화** — 선택한 폴더 → 한 번에 업로드/덮어쓰기
- **Dry-run 미리보기** — 추가/수정/삭제/스킵 분류 표시 후 확인하면 실행
- **로컬 .gitignore 존중** — 폴더의 `.gitignore` 파싱하여 제외
- **삭제도 미러링** — 로컬에서 사라진 파일 → 원격에서도 자동 삭제 (토글로 끄기 가능, 안전 위해 `targetPrefix` 범위 안에서만 동작)
- **충돌 자동 재시도** — SHA mismatch 시 신선한 SHA로 5회까지 자동 재시도
- **빈 파일 보존** — `__init__.py`, `.gitkeep` 같은 0바이트 파일도 Git Data API로 정확히 보존

### 자동 폴더 감시 (풀오토)
- **`FileSystemWatcher` + 디바운스** — 파일 바뀌면 N초 후 자동으로 변경분만 푸시
- **앱 lifecycle 싱글턴** — 창을 닫아도(트레이로) 감시 계속 동작
- **설정 영구 저장** — 앱 끄고 다시 켜도 자동 재개. Windows 재부팅 후에도 유지
- **Windows 시작 시 자동 실행** 토글 (레지스트리 Run 키 + `--minimized` 플래그)
- **이벤트 로그** — 어떤 파일이 언제 동기화됐는지 실시간 표시

### 파일 브라우저 / 다운로드
- 리포의 파일 트리 (확장자별 아이콘)
- 텍스트 파일 즉석 미리보기 (≤512KB), 바이너리/큰 파일은 자동으로 다운로드 모드
- **다운로드 3단계**:
  - 단일 파일 다운로드
  - 폴더 → ZIP 압축해서 다운로드
  - 전체 리포 → GitHub `zipball` API로 한 번에

### 클론 (LibGit2Sharp 기반, git CLI 불필요)
- 헤더의 **📥 클론** 버튼 → 다이얼로그에서 경로/브랜치/recursive 선택
- 진행률 + 객체 다운로드/체크아웃 단계 표시
- 인증된 토큰으로 private 리포도 OK
- 완료 후 자동으로 폴더 열림 (옵션)
- 명령 팔레트(Ctrl+P)에서도 검색 가능 — `클론: 리포명`

### 브랜치 / 커밋
- 브랜치 선택 드롭다운 + 새 브랜치 생성
- 모든 업로드/동기화 작업이 선택된 브랜치 기준
- 커밋 히스토리 뷰어 (최근 30개, 더블클릭 → 브라우저 열기)

### README / Issues / PRs
- README.md 인라인 편집 + 저장 시 자동 커밋
- Issues / PullRequests 목록 (Open/Closed 토글, 더블클릭 → 브라우저 열기)
- ISSUE / PR 색상 라벨링

### 릴리스 (자동화 풍부)
- **다음 버전 제안** — 마지막 태그(예: v1.2.3) → v1.2.4 자동 입력
- **자동 릴리스 노트** — GitHub generate-notes API로 마지막 릴리스 이후 커밋·PR 자동 요약
- **첨부 파일** — 여러 파일 추가 또는 폴더 → 자동 ZIP 압축해서 release asset으로 업로드
- Pre-release 토글, 커스텀 이름·설명

### GitHub Pages
- 활성/비활성 토글 (default branch + `/` 경로 기준)
- 활성 시 URL 표시 + 브라우저에서 열기

### 리포별 영구 기억
- 각 리포의 업로드 폴더, 감시 폴더, 커밋 메시지, 옵션이 **영구 저장**
- 다음 실행 시 그 리포 들어가면 폴더가 자동으로 채워짐
- 저장된 폴더가 사라졌으면 **팝업으로 새 폴더 지정 권유** (한 세션에 한 번만)

### UX
- **명령 팔레트 (Ctrl+P)** — 리포 빠른 점프 + 명령 실행
- **키보드 단축키**
  - `Ctrl+P` 명령 팔레트
  - `Ctrl+N` 새 리포지토리
  - `Ctrl+R` 리포 새로고침
  - `Ctrl+T` 다크/라이트 테마 전환
  - `Ctrl+V` (Upload 탭) 클립보드 이미지 업로드
- **시스템 트레이** — 창 X 누르면 트레이로(감시 유지). 트레이 메뉴에서 감시 개수, Stop All, Auto-Start 토글, 종료
- **다크/라이트 테마 토글**
- **사용자 지정 아이콘** — EXE/창/트레이 모두 동일

---

## 인증 (3가지, 모두 DPAPI 영구 저장)

| 방식 | 설명 |
|---|---|
| **GitHub 계정으로 로그인 (OAuth Web Flow)** | 브라우저가 자동으로 열리고 "Authorize" 한 번 누르면 끝. 가장 매끄러움. Client ID + Secret 필요 |
| **Device 코드 로그인 (OAuth Device Flow)** | 브라우저에 8자리 코드 입력. Client Secret 불필요 |
| **Personal Access Token (PAT)** | 토큰 붙여넣기. 가장 빠름, OAuth App 등록 불필요 |

토큰과 OAuth 설정 모두 `%APPDATA%\GitUI\`에 Windows DPAPI로 암호화 저장.

### OAuth App 등록 (Web/Device Flow 사용 시)

1. https://github.com/settings/applications/new
2. Application name: `GitUI`
3. Homepage URL: `http://localhost`
4. **Authorization callback URL**: `http://localhost:8765/callback/` ← 정확히 이 값 (끝의 `/` 포함)
5. 등록 후 OAuth App 설정에서 **Enable Device Flow** 체크 (Device Flow도 쓰려면)
6. 발급된 Client ID + Client Secret을 앱의 OAuth 설정 화면에 입력

PAT만 쓸 거면 등록 불필요. https://github.com/settings/tokens/new 에서 `repo` + `delete_repo` 스코프로 발급.

---

## 사용 시나리오

| 상황 | 사용 흐름 |
|---|---|
| 작업 폴더를 즉시 GitHub에 백업 | 폴더를 메인 창에 드롭 → 이름 확인 → 자동 생성 + 초기 업로드 |
| 활발한 프로젝트 자동 동기화 | 리포 → 자동 감시 탭 → 폴더 선택 → 시작 → 코딩만 하면 됨 |
| Windows 켜질 때부터 동기화 | 자동 감시 시작 후 트레이 메뉴에서 "Windows 시작 시 자동 실행" 체크 |
| 안전한 폴더 동기화 | 업로드 탭 → "미리보기 후 동기화" → 추가/수정/삭제 분류 확인 → 진행 |
| 이슈에 스크린샷 첨부 | 캡처 도구로 스크린샷 → 업로드 탭에서 Ctrl+V → 즉시 커밋 |
| 다른 사람 리포 둘러보기 | 사이드바 "외부 리포 검색" → 검색 → "자세히" → 파일·README·이슈 열람 |
| 빠른 리포 점프 | Ctrl+P → 이름 일부 입력 → Enter |
| 코드를 로컬에서 작업 | 리포 → 헤더 "📥 클론" → 경로 선택 → 자동으로 VS Code에서 git 사용 가능 |
| 새 릴리스 | 설정 탭 → "다음 버전 제안" → "자동 노트 생성" → 첨부 파일 추가 → 릴리스 생성 |

---

## 빌드 / 실행

요구사항: **.NET 8 SDK** (Windows)

```bash
cd GitUI
dotnet run
```

또는 Visual Studio 2022에서 `GitUI.sln` 열고 F5.

### 단일 실행파일 (배포용)

```bash
dotnet publish GitUI/GitUI.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -p:DebugSymbols=false ^
  -o publish
```

결과물: `publish/GitUI.exe` (~71 MB, .NET 런타임 + LibGit2Sharp 네이티브 + 모든 의존성 포함). 받은 사람이 .NET 설치 없이 더블클릭으로 실행 가능.

---

## 데이터 위치

`%APPDATA%\GitUI\` 안에 보관 (EXE와 분리 — 업데이트해도 유지):

| 파일 | 용도 | 보호 |
|---|---|---|
| `token.dat` | 인증 토큰 | DPAPI |
| `oauth.dat` | OAuth Client ID/Secret | DPAPI |
| `watches.json` | 자동 감시 설정 (앱 재시작 시 자동 재개) | 평문 |
| `repo-settings.json` | 리포별 폴더/옵션 기억 | 평문 |

---

## 프로젝트 구조

```
GitUI/
├── App.xaml(.cs)                       --minimized 인자 파싱
├── MainWindow.xaml(.cs)                쉘 (사이드바, 드롭존, 트레이, 키바인딩)
├── icon.ico                            앱 아이콘
├── Models/RepoItem.cs
├── Services/
│   ├── TokenStorage              DPAPI 암호화 토큰
│   ├── OAuthConfig               OAuth Client ID/Secret (DPAPI)
│   ├── OAuthWebFlowAuthenticator HttpListener 기반 Web Flow
│   ├── DeviceFlowAuthenticator   Device Flow 폴링
│   ├── GitHubService             Octokit 래퍼 (전부)
│   ├── CloneService              LibGit2Sharp 클론 + 진행상황
│   ├── GitignoreMatcher          .gitignore 패턴 매칭
│   ├── SyncPreview               로컬 vs 원격 SHA diff (삭제 감지 포함)
│   ├── FolderWatcherService      FileSystemWatcher + 디바운스
│   ├── WatchManager              앱 lifecycle 싱글턴 (창 닫아도 동작)
│   ├── WatchConfig               감시 설정 + 영구 저장
│   ├── AutoStart                 Windows Run 키 토글
│   ├── RepoSettings              리포별 폴더/옵션 영구 기억
│   └── ProjectTemplates          언어별 스타터 파일 (12개)
├── ViewModels/
│   ├── MainViewModel             전체 상태 + 명령 팔레트 + 다중선택 + 트레이
│   ├── LoginViewModel            3가지 로그인 방식
│   ├── CreateRepoViewModel       새 리포 + 스타터 + .gitignore + 라이선스
│   ├── RepoDetailViewModel       탭 컨테이너 + Star/Fork/Clone/Back
│   ├── SearchViewModel           외부 리포 검색
│   ├── CommandPaletteViewModel   Ctrl+P 검색
│   └── Tabs/
│       ├── UploadTabViewModel    드롭존 + 동기화 + dry-run + 클립보드
│       ├── WatchTabViewModel     자동 감시 (WatchManager 위임)
│       ├── FilesTabViewModel     파일 트리 + 미리보기 + 다운로드
│       ├── ReadmeTabViewModel    README 인라인 에디터
│       ├── HistoryTabViewModel   커밋 히스토리 30개
│       ├── IssuesTabViewModel    Issues/PRs 목록
│       └── SettingsTabViewModel  visibility / archive / pages / release
└── Views/
    ├── LoginView                 3-state 로그인 (Choose/Pending/Configure)
    ├── CreateRepoView            새 리포 폼
    ├── RepoDetailView            7-탭 컨트롤 (외부 리포는 4-탭만)
    ├── SearchView                외부 검색 결과 카드
    ├── CommandPalette            Ctrl+P 오버레이
    ├── Tabs/                     탭별 UserControl
    └── Dialogs/
        ├── SyncPreviewDialog          Dry-run 확인 (추가/수정/삭제 카운트)
        ├── CreateRepoFromFolderDialog 드롭→새 리포
        └── CloneDialog                클론 진행률
```

---

## 알려진 제한

- 큰 파일(>100MB)은 GitHub Contents API 한계로 업로드 불가 — dry-run에서 자동으로 Skipped로 표시 (Git LFS 필요)
- Pages 활성화는 default branch + `/` 경로 기준만 지원
- README 편집 시 동시 편집 충돌 감지 없음 (마지막 저장이 이김)
- GitHub Search API는 비로그인 10 req/min, 로그인 30 req/min 제한
- Mirror 동기화 시 `targetPrefix`가 비어있고 로컬 폴더가 비어있으면 모든 원격 파일이 삭제 후보가 됨 → dry-run에서 반드시 확인하고 진행
- Hardcodet.NotifyIcon.Wpf 트레이 아이콘은 가끔 Windows Explorer 재시작 시 안 보일 수 있음

---

## 라이선스

본인 코드와 함께 자유롭게 사용. 의존 라이브러리는 각자 라이선스 따름:
- Octokit (MIT)
- LibGit2Sharp (MIT, libgit2 GPLv2 with linking exception)
- ModernWpfUI (MIT)
- CommunityToolkit.Mvvm (MIT)
- Hardcodet.NotifyIcon.Wpf (CPOL)
