# Cozy Orbit (코지 오빗)

햄찌 연구소의 도토리호가 우주를 천천히 항해하는 힐링 방치형 데스크톱 게임 (Windows · macOS).
항해는 알아서 진행되고, 이 컴퓨터에서 쓴 **AI 토큰**이 **별빛**이 되어 도토리호를 빠르게 해 준다.

- 기본 항해: 켜 두기만 하면 전진하고, 꺼 둬도 최대 8시간(연구로 늘어남)까지 이어진다.
- 별빛: Claude Code · Codex · Gemini · OpenCode · omp 같은 AI 도구의 로컬 기록을 **읽기만** 해서 가중 토큰을 센다.
  하루 단위 로그 곡선이라 많이 쓸수록 조금씩 덜 쌓인다. 배터리에서 넘친 별빛은 별가루가 된다.
- 별가루로 연구(엔진·배터리·태양 돛·부스터·망원경·크루·침낭)를 하고, 30개 천체 도감을 채운다.
- 별사탕 상점: **게임을 켜 둔 동안** 쓴 AI 토큰은 별사탕(가중 토큰 5,000개당 1개)도 된다.
  별사탕으로 선장 햄찌 복장·털색, 탐사선 모양, 배경 테마 스킨을 산다. 햄찌들은 연구소 바닥을 좌우로 돌아다닌다.
- 트레이 상주, 한국어/영어.
- 플로팅 창: 장면의 "플로팅" 버튼(또는 트레이 메뉴)으로 우주선·햄찌 장면만 담긴 작은 창을 항상 위에 띄운다.
  끌어서 옮기고, 오른쪽 아래 손잡이·오른쪽 클릭 메뉴(작게/보통/크게)로 크기를 바꾸며, 두 번 누르면 큰 창으로 돌아간다.
  위치·크기를 기억하고, 플로팅으로 둔 채 끝내면 다음 실행도 플로팅으로 시작한다.

설계와 밸런스: [docs/design.md](docs/design.md)

## 실행

.NET 10 SDK 가 필요하다.

```sh
dotnet run --project src/CozyOrbit.App
dotnet test tests/CozyOrbit.Core.Tests
```

세이브: Windows `%APPDATA%\CozyOrbit\save.json`, macOS `~/Library/Application Support/CozyOrbit/save.json`.
`COZY_STATE_DIR` 로 바꿀 수 있다.

## 라이선스 참고

- 글꼴: [갈무리 (Galmuri)](https://github.com/quiple/galmuri) — SIL Open Font License 1.1 (`src/CozyOrbit.App/Assets/Fonts/OFL.txt`).
- 사용량 읽기 코드는 같은 작성자의 poke-token-bar-window 에서 옮겨 왔다.
