# streams-unity

STREAMS 의 Unity 클라이언트. **WebGL 먼저** 만들어 사이트(my-site)의 `/games/streams-unity/` 에 올린다.
덱·AI·기록은 전부 서버(Matail/my-site 의 `worker/streams/`)에 있고, 이 앱은 화면과 입력만 맡는다.

- 전체 계획: my-site `docs/streams-unity-plan.md`
- API 계약: [`docs/streams-api.md`](docs/streams-api.md) (my-site 에서 복사 — 원본은 my-site)

## 구조

```text
Assets/
├── Scripts/
│   ├── Core/           순수 C# (UnityEngine 없음) — 규칙·점수·보드. 서버 game.ts 를 옮긴 것
│   ├── Net/            서버 통신
│   │   ├── IGameSession.cs   화면 ↔ 서버 이음새. AI 대전·멀티(5단계)가 같은 인터페이스
│   │   ├── AiSession.cs      /api/streams/start, /move
│   │   ├── ApiClient.cs      UnityWebRequest + Awaitable, 재시도, x-streams-client 헤더
│   │   ├── ApiModels.cs      요청·응답 모양
│   │   └── ClientInfo.cs     클라이언트 종류/버전, 서버 주소, 익명 플레이어 id
│   └── Presentation/   화면. 1단계는 씬·프리팹 없이 코드가 UGUI 를 만든다 (GameBootstrap)
├── Editor/StreamsBuild.cs    메뉴 STREAMS → Build WebGL
└── Tests/EditMode/           규칙 테스트 (Data/rules.json = 서버 정답지)
Tools/CoreCheck/              Unity 없이 규칙 검사 (.NET 8, CI)
```

## 처음 열기

1. Unity 6 (6000.x) 설치 — WebGL Build Support 모듈 포함.
2. Unity Hub → Add → Add project from disk → 이 폴더. Hub 가 에디터 버전을 물으면 설치된 Unity 6 을 고른다.
   처음 열 때 `ProjectSettings/` 가 만들어진다 — **`ProjectSettings/` 와 모든 `.meta` 파일을 커밋한다.**
3. Input System 을 켤지 물으면 Yes (재시작). 안 켜도 옛 입력 모듈로 돈다.
4. 메뉴 **STREAMS → Apply WebGL Settings** — 빈 씬 `Assets/Scenes/Main.unity` 를 만들고 WebGL 설정을 맞춘다.
5. Play. 화면은 코드가 만든다.

### 서버 주소

| 어디서 | 주소 |
|---|---|
| WebGL (사이트에 올린 것) | 게임을 띄운 페이지와 같은 도메인 — 설정 필요 없음 |
| 에디터 | 기본 `http://localhost:8787` (my-site 에서 `npx wrangler dev`) |

에디터에서 배포된 서버를 쓰려면 한 번만:

```csharp
Streams.Net.ClientInfo.BaseUrl = "https://<사이트 도메인>";
```

에디터에서 둔 판은 `x-streams-client: unity-editor/…` 로 기록돼 분석에서 빠진다.

## 테스트

- Unity: Window → General → Test Runner → EditMode → Run All
- Unity 없이: `dotnet run -c Release --project Tools/CoreCheck` (GitHub Actions `core` 가 푸시마다 돌림)

규칙을 바꾸면 my-site 에서 `node worker/streams/vectors.ts` 로 정답지를 다시 뽑아
`Assets/Tests/EditMode/Data/rules.json` 에 복사한다.

## WebGL 빌드 → 사이트에 올리기

1. 메뉴 **STREAMS → Build WebGL** → `Builds/WebGL/`
   - 압축 끔(Cloudflare 가 전송 압축), 파일명 해시, 스트리핑 Medium
   - 25 MiB 넘는 파일이 있으면 경고 (사이트 정적 파일 한도 — 넘으면 R2 로)
2. `Builds/WebGL/` 내용을 my-site 의 `public/games/streams-unity/` 로 복사해 배포.
   (자동화는 이후 단계: CI 빌드 → my-site 로 PR)

배치 모드:

```sh
Unity -batchmode -quit -projectPath . -executeMethod StreamsBuild.BuildWebGL -logFile -
```

## 다음

- [ ] 에디터에서 5단계 난이도 끝까지 플레이 확인, `.meta`·`ProjectSettings` 커밋
- [ ] WebGL 빌드를 my-site `/games/streams-unity/` 에 올려 실제 서버와 대전
- [ ] 판 이어 두기 (토큰·보드를 PlayerPrefs 에 저장)
- [ ] 카드·보드 연출, 아트
