# Cozy Orbit (코지 오빗)

햄찌 연구소의 도토리호가 우주를 천천히 항해하는 힐링 방치형 데스크톱 게임 (Windows · macOS).
항해는 알아서 진행되고, 이 컴퓨터에서 쓴 **AI 토큰**이 **별빛**이 되어 도토리호를 빠르게 해 준다.

- 기본 항해: 켜 두기만 하면 전진하고, 꺼 둬도 최대 8시간(연구로 늘어남)까지 이어진다.
- 별빛: Claude Code · Codex · Gemini · OpenCode · omp 같은 AI 도구의 로컬 기록을 **읽기만** 해서 가중 토큰을 센다.
  하루 단위 로그 곡선이라 많이 쓸수록 조금씩 덜 쌓인다. 배터리에서 넘친 별빛은 별가루가 된다.
- 별가루로 연구(엔진·배터리·태양 돛·부스터·망원경·크루·침낭)를 하고, 도감을 채운다:
  우리 은하에서 안드로메다·마젤란 은하·처녀자리 은하단·먼 우주·우주 거대 구조까지 목적지 6곳, 천체 120종
  (실제 천체는 실제 천문 사실 한 줄과 함께).
- 새 항해 (환생): 목적지 도감을 다 채우면 다음 목적지로 떠난다 — 진행은 처음부터, 속도·별가루 ×1.6 이 영구로 쌓인다.
- 별사탕 상점: **게임을 켜 둔 동안** 쓴 AI 토큰은 별사탕(가중 토큰 5,000개당 1개)도 된다.
  선장 햄찌 복장·털색, 탐사선 모양, 배경 테마 스킨과 연구소 소품을 산다. 발견하면 얻는 기념품도 있다.
- 오늘의 목표 3개 · 도전과제 23개 · 날짜별 사용량 그래프.
- 햄찌를 누르면 쓰다듬기, 가끔 별똥별(누르면 별가루)·유성우·우주 고래.
- 트레이 상주, 로그인 시 자동 실행, 한국어/영어, 서명된 자동 업데이트.
- 소리: 직접 합성한 로파이 배경음과 효과음. 기본은 꺼져 있고 설정 → 소리에서 켠다 (배경음·효과음 따로, 음량).
- 플로팅 창: 장면의 "플로팅" 버튼(또는 트레이 메뉴)으로 우주선·햄찌 장면만 담긴 작은 창을 항상 위에 띄운다.
  끌어서 옮기고, 오른쪽 아래 손잡이·오른쪽 클릭 메뉴(작게/보통/크게)로 크기를 바꾸며, 두 번 누르면 큰 창으로 돌아간다.
  위치·크기를 기억하고, 플로팅으로 둔 채 끝내면 다음 실행도 플로팅으로 시작한다. 알림은 햄찌 말풍선으로.

설계와 밸런스: [docs/design.md](docs/design.md)

## 설치

[Releases](https://github.com/Jaeguk-Ahn/cozy-orbit/releases) 에서 받는다 — Windows `CozyOrbit-<버전>-win-x64.zip` (exe 하나),
macOS `CozyOrbit-<버전>-osx-arm64.zip`(Apple 실리콘)·`osx-x64.zip`(인텔). 이후 버전은 앱이 알아서 확인·설치한다 (설정 → 업데이트).
macOS 는 서명·공증을 하지 않은 앱이라 처음 한 번 Finder 에서 오른쪽 클릭 → 열기.

## 개발

.NET 10 SDK 가 필요하다.

```sh
dotnet run --project src/CozyOrbit.App
dotnet test tests/CozyOrbit.Core.Tests
```

세이브: Windows `%APPDATA%\CozyOrbit\save.json`, macOS `~/Library/Application Support/CozyOrbit/save.json`.
`COZY_STATE_DIR` 로 바꿀 수 있다.

## 라이선스 참고

- 글꼴: [갈무리 (Galmuri)](https://github.com/quiple/galmuri) — SIL Open Font License 1.1 (`src/CozyOrbit.App/Assets/Fonts/OFL.txt`).
- 소리 출력: [OpenAL Soft](https://github.com/kcat/openal-soft) — LGPL, 수정 없이 동적 로드 (`src/CozyOrbit.App/Assets/Licenses/OpenAL-Soft.txt`).
- 사용량 읽기 코드는 같은 작성자의 poke-token-bar-window 에서 옮겨 왔다.
