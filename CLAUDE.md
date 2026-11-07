# Cozy Orbit — 작업 지침

- 설계·밸런스의 기준은 `docs/design.md`. 수치를 바꾸면 `Game/Balance.cs`·`Game/Upgrades.cs` 와 문서를 함께 고친다.
  밸런스를 바꿀 때는 발견·항해 시점 시뮬레이션(문서 §6·§11 표)을 다시 돌려 표를 갱신한다.
- 목적지·천체 목록은 `Game/Destinations.*.cs`. id 는 세이브(FoundAt)에 남으므로 바꾸지 않는다. 순서를 바꾸면 `GameState.CurrentVersion` 을 올리고
  `SaveStore` 에 옮겨 적기를 넣는다. 실제 천체의 Fact 는 웹에서 확인한 값만, 기관마다 다르면 "약" 으로.
- 사용량 계층(`src/CozyOrbit.Core/Usage/`)은 `../poke-token-bar-window` 의 것을 옮겨 왔다 (Cursor 제외).
  범용 경로에 프로바이더별 분기를 넣지 않는다 — 새 도구는 `IUsageProvider` 구현 + `UsageScanner.CreateDefaultProviders()` 등록.
  원본 쪽 프로바이더 수정을 가져올 때는 네임스페이스만 바꿔 그대로 옮긴다.
- 외부 로그·DB 는 항상 읽기 전용. 읽기 실패는 "0 사용량"이 아니라 "이번엔 건너뜀" (별빛 장부는 단조라 빼앗지 않는다).
- 스킨은 `Game/Skins.cs` 목록 + 스프라이트 규칙(design.md §9): 복장 `outfit_*` 20×22·원점 (2,6), 탐사선 `ship_*`/`ship_*_boost`,
  테마 `Art/SceneThemes.Catalog.cs`. 햄찌 프레임 번호(0 가만히·1 깜빡·2/3 걸음)는 렌더러와의 약속 — 바꾸지 않는다.
  새 색은 `Palette.<영역>.cs` partial 에. 별사탕 규칙은 `Game/CandyLedger.cs` (켜 둔 구간 안의 사용량만).
- 소품은 `Game/Decor.cs` (기념품은 발견 id 로 연결), 목표·도전과제는 `Game/DailyGoals.cs`·`Achievements.cs` — 진행값은 `GameStats` 누계에서만 읽는다.
- 게임 규칙(`Game/`)은 시계·난수 없이 결정적으로. 시간은 호출자가 넘긴다 (테스트 가능하게).
- 스프라이트는 `Art/` 의 문자 격자 + `Palette` 색만 쓴다. 외부 이미지 파일을 넣지 않는다. 화면에는 정수 배율 + 최근접 보간.
- UI 문자열은 한국어·영어 둘 다: 코드는 `L.T("한국어", "English")`, XAML 은 `{l:T Ko='…', En='…'}`.
- 글꼴 갈무리(Galmuri)는 SIL OFL 1.1 — `Assets/Fonts/OFL.txt` 를 지우지 않는다.
- 빌드/테스트: `dotnet build CozyOrbit.slnx`, `dotnet test tests/CozyOrbit.Core.Tests`. 실행: `dotnet run --project src/CozyOrbit.App`.
  개발 중 세이브를 격리하려면 `COZY_STATE_DIR` 환경 변수.
- 커밋 메시지에 Claude 서명 줄을 넣지 않는다.
- **업데이트 서명**: 공개 키는 `scripts/update-signing.pub.pem` 과 `UpdateManifest.PublicKeyPem` 두 곳 (테스트가 같은지 본다). 개인 키는 GitHub Environment
  `ENV_RELEASE` 의 시크릿 `UPDATE_SIGNING_KEY` 에만 — 출력·커밋하지 않는다. 키를 잃으면 이미 깔린 앱은 새 버전을 받을 수 없다.
- **앞으로의 모든 앱 버전은 `--finish-update` 를 알아들어야 한다** (`Program.Main` 맨 앞의 `UpdateInstaller.RunFinishUpdate`) — 옛 버전이 새 버전을 이 인자로 띄워 설치를 마친다.
- 릴리스 태그는 `release/vX.Y.Z` (csproj `<Version>` 과 같아야 함). 태그 만들기·푸시·삭제와 초안 릴리스 공개는 **무조건 사용자 확인 후에**.
- 앱 실행 중엔 `src/CozyOrbit.App/bin` 이 잠긴다 — 확인 빌드는 `-p:BaseOutputPath=$TEMP/cozy-out/` 처럼 다른 폴더로.
