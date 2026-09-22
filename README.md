# **Xapper**

## **목차**

<b>

- [개요](#xapper-개요)
- [지원 환경](#지원-환경)
- [기술 및 도구](#기술-및-도구)
- [라이브러리](#라이브러리)
- [프로젝트 구조](#프로젝트-구조)
- [기능 구현](#기능-구현)
  - [프로세스 탐색 및 인젝션](#1-프로세스-탐색-및-인젝션)
  - [UI 트리 스냅샷](#2-ui-트리-스냅샷)
  - [요소 검색](#3-요소-검색)
  - [UI 자동 조작](#4-ui-자동-조작)
  - [화면 기록](#5-화면-기록)
  - [진단 및 검증](#6-진단-및-검증)
- [설치 및 설정](#설치-및-설정)
  - [사전 요구사항](#사전-요구사항)
  - [빌드](#빌드)
  - [MCP 서버 연결 (Claude Desktop)](#mcp-서버-연결-claude-desktop)
  - [MCP 서버 연결 (Claude Code)](#mcp-서버-연결-claude-code)
  - [에이전트 스킬 설치 (권장)](#에이전트-스킬-설치-권장)
  - [사용 예시](#사용-예시)

</b>

## **Xapper 개요**

> **프로젝트 목적 :** AI 에이전트가 실행 중인 WPF 애플리케이션의 UI를 직접 조작하고 검증할 수 있는 MCP 기반 테스트 자동화 플랫폼
>
> **기획 및 제작 :** 이전석
>
> **주요 기능 :** 프로세스 인젝션을 통한 WPF UI 트리 접근, 자연어 기반 UI 테스트 자동화, 실시간 스냅샷/클릭/입력/검증
>
> **개발 환경 :** Windows 11, Visual Studio 2022 Community, .NET 9.0
>
> **문의 :** malbox5034@naver.com

<br/>

## **지원 환경**

### **운영체제**

| OS | 지원 |
|:---|:---:|
| Windows 10 (x64) | O |
| Windows 11 (x64, ARM64) | O |
| macOS / Linux | X (WPF 전용) |

### **대상 WPF 앱 .NET 버전**

Xapper가 인젝션할 수 있는 대상 WPF 애플리케이션의 .NET 런타임 버전입니다.

| 런타임 | 지원 |
|:---|:---:|
| .NET 9.0 | O |
| .NET 8.0 | O |
| .NET 7.0 | O |
| .NET 6.0 | O |
| .NET Framework 4.x | X |

### **빌드 요구사항**

| 항목 | 버전 |
|:---|:---|
| .NET SDK | 9.0 이상 |
| Windows SDK | 10.0.17763.0 이상 (WPF 빌드용) |

<br/>

## **기술 및 도구**
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET_9-512BD4?style=for-the-badge&logo=.net&logoColor=white)
![WPF](https://img.shields.io/badge/WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white)
![MCP](https://img.shields.io/badge/MCP-FF6B35?style=for-the-badge&logoColor=white)
![VS](https://img.shields.io/badge/Visual_Studio-5C2D91?style=for-the-badge&logo=visual%20studio&logoColor=white)
![GitHub](https://img.shields.io/badge/GitHub-100000?style=for-the-badge&logo=github&logoColor=white)

<br/>

## **라이브러리**

|라이브러리|버전|비고|
|:---|---:|:---:|
|ModelContextProtocol|1.2.0|MCP 서버 SDK (stdio transport)|
|Microsoft.Extensions.Hosting|10.0.7|호스팅/DI 프레임워크|
|Snoop GenericInjector|6.1.0|WPF 프로세스 DLL 인젝션 (네이티브)|
|xUnit|2.9.3|단위 테스트|

<br/>

## **프로젝트 구조**

```
src/
├── Xapper.Protocol        IPC 메시지 계약 (net6.0~9.0-windows, WPF 무의존)
├── Xapper.Injector        네이티브 인젝터 (P/Invoke로 DLL 주입)
├── Xapper.Inspector       인젝션 라이브러리 (net6.0~9.0-windows 멀티타겟)
└── Xapper.McpServer       MCP 서버 (stdio transport, 29개 도구)
tests/
├── Xapper.TestApp         샘플 WPF 로그인 폼
└── Xapper.Tests           단위 테스트 (xUnit)
external/
└── snoop-bin/             Snoop GenericInjector 네이티브 DLL
skills/
└── xapper/                에이전트 작업 절차 스킬 (Claude Code 쪽에 복사해 사용)
```

Inspector와 GenericInjector DLL은 McpServer에 **임베디드 리소스로 내장**되어 있어 빌드된 실행 파일만으로 동작합니다. 별도의 경로 설정이나 환경변수 없이 바로 사용 가능합니다.

<br/>

## **기능 구현**

### **1. 프로세스 탐색 및 인젝션**
- `xapper_list_processes` : 실행 중인 WPF 프로세스 목록 조회
- `xapper_attach` : P/Invoke 네이티브 인젝션으로 대상 프로세스에 Inspector DLL 주입
- `xapper_launch` : 앱을 직접 띄우면서 Inspector 를 함께 태운다. .NET 런타임의 startup hook(`DOTNET_STARTUP_HOOKS`)이 앱의 진입점보다 먼저 돌므로 주입 절차도, 그 뒤의 고정 2초 대기도 없어 attach 보다 빠르고(TestApp 실측 launch 0.9초 vs attach 2.1~2.8초), 주 창보다 먼저 뜨는 스플래시·로그인 창까지 사정권에 들어온다. 띄운 앱이 활성 세션이 되며 `xapper_detach` 는 연결만 끊는다(앱은 계속 실행). framework-dependent .NET 앱만 지원하므로 .NET Framework 앱은 직접 띄운 뒤 `xapper_attach` 를 사용한다. 두 가지 제약이 있다. 첫째, 환경변수는 대상 앱이 띄우는 자식 프로세스에도 상속되기 때문에, 앱보다 낮은 .NET 버전에서 도는 보조 실행 파일이 있다면 그 프로세스가 기동에 실패한다. 이런 앱에는 launch 대신 attach 를 사용한다. 둘째, 트리밍된 self-contained 빌드는 런타임이 startup hook 기능 자체를 제외하므로 훅이 조용히 로드되지 않고 제한 시간까지 기다린 뒤 실패한다
- `xapper_detach` : Named Pipe 연결 해제 및 정리
- Named Pipe 기반 IPC (4바이트 LE 길이 접두사 + UTF-8 JSON)
- 응답을 끝까지 읽지 못한 연결은 이후 메시지 경계가 어긋나므로 폐기된다. 다음 호출은 재부착을 요구하는 오류를 낸다

### **2. UI 트리 스냅샷**
- `xapper_snapshot` : Visual Tree를 계층적 텍스트로 반환
- 각 요소에 ref 번호 부여 (이후 조작에 사용)
- maxDepth 파라미터로 탐색 깊이 제어
- 다이얼로그는 물론 Popup·ContextMenu·드롭다운까지 열린 최상위 창을 모두 탐색
- 순회할 수 없는 노드는 그 가지만 건너뛰고 순회를 계속하며, 건너뛴 개수와 사유별 요약을 헤더에 한 줄로 표시 (낱낱의 위치는 `format="json"`)
- `addressableOnly` 를 켜면 id·name·text 중 아무것도 없는 레이아웃 컨테이너(Grid·Border·ContentPresenter 등)를 접고 그 아래의 지목 가능한 요소를 끌어올린다. 셀렉터로 지목할 수 있는 것만 남으므로 출력이 크게 짧아진다(TestApp 실측 86줄 → 51줄, 컨트롤이 많은 화면일수록 차이가 커진다). 이때는 `maxDepth` 를 평소보다 크게 준다 — 깊이 제한이 필터보다 먼저 걸리기 때문이다. 접힌 개수는 헤더에 한 줄로 알린다. 컨트롤 템플릿 내부(`PART_ContentHost` 같이 이름이 붙은 부품)는 이름이 있으므로 남는다
- `rootRef`로 특정 요소부터 부분 탐색. 깊이를 올리면 응답이 지수로 커지므로 전체 깊이를 올리는 대신 부분 탐색을 쓴다
- 스냅샷은 이전에 발급한 ref를 모두 무효화한다 (검색은 무효화하지 않음)
- 최상위 창이 둘 이상이면 루트가 가상 `Application` 노드가 된다. 팝업·메뉴·툴팁도 각자 최상위 창이라 무엇이 떠 있느냐에 따라 루트 모양이 바뀐다

### **3. 요소 검색**
- `xapper_find` : Name, AutomationId, Type, Text 기반 요소 검색
- 트리 필터링으로 대규모 UI에서도 빠른 탐색
- 다이얼로그 윈도우 내부 요소도 검색 가능
- 자식 슬롯이 비어 있는 컨트롤(yFiles, DevExpress 등)을 만나도 검색이 중단되지 않는다. 그 가지만 건너뛰고 찾은 것을 모두 반환하며, 건너뛴 노드의 부모 타입·이름·깊이를 함께 보고
- `type`은 클래스명 완전 일치, 나머지는 부분 일치, 여러 인자는 AND
- `xapper_element_at` : 화면 좌표에 그려진 요소와 그 조상을 깊은 것부터 ref와 함께 반환. 이름도 AutomationId도 없는 컨트롤을 스크린샷에서 본 위치로 지목해 ref를 얻는 길이며, 히트테스트라 가려진 것도 정확히 판별하고 커서·포커스를 건드리지 않는다
- 검색·스냅샷은 열린 최상위 창을 모두 순회하므로 **Popup·ContextMenu·드롭다운 안의 항목도 보인다**
- **`target` selector**: 요소를 받는 도구(click/doubleclick/rightclick/wheel/type/key/select/toggle/expand/scroll/get_property/assert)는 `ref` 대신 `target="id=LoginButton"` · `"name=txtUser,type=TextBox"` · `"text=Log In"` 을 받는다(find 와 같은 매칭, 콤마 AND). 정확히 하나가 맞을 때만 실행하고 0개·여러 개면 후보를 담은 오류 — find 를 따로 부를 필요가 없다

### **4. UI 자동 조작**
- 공통: 조작이 모달 대화상자(MessageBox 등)를 열면 그 창이 닫힐 때까지 기다리지 않는다 — `timeout` 뒤 "전달됐지만 앱이 아직 처리 중" 으로 응답하고, 대화상자가 떠 있는 동안에도 조회 도구는 동작한다(Win32 대화상자는 스냅샷에 안 보이므로 `xapper_screenshot mode="screen"` 으로 확인)
- 공통: 후킹 경로 마우스 제스처는 앱이 메시지 하나를 250 ms 안에 처리하지 못하면(느린 핸들러·모달) 스푸프를 즉시 끈다 — Xapper 가 조작하는 동안 사람의 실제 마우스가 그 앱에서 한참 안 먹던 문제를 이 시간으로 묶는다. 응답은 그와 별개로 핸들러가 끝나기를 `timeout` 까지 기다리므로 수백 ms 걸리는 핸들러는 정상 완료로 돌아온다(드래그는 이 경우 일찍 끝났을 수 있다는 경고를 붙인다). OLE 드래그앤드롭(`DragDrop.DoDragDrop`)은 실제 커서를 따르므로 무입력으로는 완성되지 않는다
- `xapper_click` : 버튼/요소 클릭. 좌표를 주지 않으면 AutomationPeer → RaiseEvent 폴백(빠르지만 히트테스트를 건너뛰므로 가려진 요소도 눌림), 좌표를 주면 그 지점을 실제 히트테스트를 거쳐 클릭한다. 기본은 대상 프로세스 안에서 몰아(user32 후킹 + WM 메시지) 실제 커서·포커스를 건드리지 않으므로 작업 중에도 쓸 수 있고, 그 경로를 설치할 수 없거나 같은 앱의 팝업·대화상자가 그 지점을 덮고 있을 때만 실제 마우스 입력으로 폴백한다(사용자가 앞에 띄운 다른 프로그램 창은 덮고 있어도 관통한다). 어느 경로였는지(synthetic / real mouse input)가 응답에 표시되며, 접근성 경로는 큐가 비워질 때까지 기다렸다가 반환한다
  - 후킹 경로가 안 될 때의 폴백만 SendInput으로 위치 기반 클릭 (듀얼 모니터/DPI 대응) — 이 경우에만 물리 커서를 옮기고 대상 창에 포커스를 넘긴다
  - 클릭/더블클릭/드래그/우클릭/휠 모두 `modifiers` 로 Ctrl/Shift/Alt 를 걸 수 있다(후킹 경로에서는 스푸프, 실제입력 폴백에서는 실제 키를 눌렀다 뗀다)
- `xapper_doubleclick` : 좌표 지점 더블클릭. 더블클릭은 특정 위치에서 일어나는 제스처라 x/y 필수(그리드 행/셀을 더블클릭해 편집기 열기 등). 클릭과 같은 후킹 경로(커서 미이동)를 쓰고, 두 번의 누름을 시스템 더블클릭 시간 안에 붙여 보내 한 번의 더블클릭으로 인식시킨다 — xapper_click 을 두 번 부르는 것으로는 보장되지 않는다
- `xapper_rightclick` : 좌표 지점 우클릭(컨텍스트 메뉴). 항상 좌표 제스처, 기본은 요소 중앙. 후킹 경로(커서 미이동)이고 폴백만 실제 입력
- `xapper_wheel` : 좌표 지점에서 마우스 휠. `xapper_scroll` 이 ScrollViewer 오프셋을 직접 바꾸는 것과 달리 실제 휠 제스처라 Ctrl+휠 줌·커스텀 MouseWheel 핸들러를 건드린다. `notches` 양수=위, 음수=아래(±100)
- `xapper_type` : 텍스트 입력. 접근성 Value 패턴이나 `TextBox.Text` 로 값을 넣고, 둘 다 없는 편집기(RichTextBox·그리드 셀 편집기·DevExpress 편집기)에는 포커스를 준 뒤 실제 키 입력으로 타이핑한다. `ref` 를 생략하면 현재 키보드 포커스 요소에 키 입력으로 타이핑한다(F2·더블클릭으로 연 인라인 편집기처럼 스냅샷에 아직 없는 편집기). 글자 그대로 들어가며, 여러 글자는 항상 이 도구로(글자마다 `xapper_key` 를 부르지 않는다)
- `xapper_key` : 대상 프로세스 안에서 키 입력(F2/Enter/Escape/Tab/화살표/한 글자, 또는 `"qwerty"` 같은 문자열을 한 호출에 글자별 키 입력으로). 전경 창과 무관하게 대상 앱의 키보드 포커스 요소로 라우팅하므로, 사용자가 앞 창에서 딴 일을 해도 키가 새지 않는다. ref 를 주면 먼저 그 요소에 포커스. 수식키(`modifiers`: Ctrl/Shift/Alt, `Ctrl+Shift` 조합)를 지원 — 대상 프로세스 안에서 GetKeyState 를 스푸프해 WPF 가 눌린 것으로 보게 하며(Ctrl+Z 등), 후크를 걸 수 없으면 오류로 안내한다. 편집기가 키(F2)로만 열리는 컨트롤에 쓴다
- `xapper_notice_show` / `xapper_notice_hide` : 대상 앱 창 위쪽에 "Xapper 조작중" 알림을 올리고 내린다. 실제 마우스 입력(폴백)이 예상되는 구간 앞에 올려 책상 앞 사람이 마우스가 저절로 움직이는 것을 보고 놀라지 않게 한다. 알림 창은 MCP 서버 프로세스가 띄우며 포커스를 가져가지 않고 클릭이 통과한다(스냅샷·검색에도 안 잡힘). 알림이 내려간 채 조작이 실제 마우스로 떨어지면 서버가 자동으로 올리고 응답에 알린다. detach 하면 내려간다. 한계: `xapper_screenshot mode="screen"` 에는 알림이 찍힐 수 있다
- `xapper_run` : 여러 조작을 담은 JavaScript 를 한 번에 실행한다. 반복·조건·대기·단언을 서버 안에서 처리해 도구를 하나씩 부를 때의 왕복을 없앤다. 전역 `xapper` 로 find/one/click/doubleClick/rightClick/wheel/drag/type/key/select/toggle/expand/scroll/get/assert/waitUntil/screenshot/snapshot/notice 를, `log`/`fail`/`sleep` 도 쓴다. 대상은 셀렉터 문자열(`id=`/`name=`/`text=`/`type=`, 콤마 AND)·ref 숫자·find 결과 요소. 실패한 조작은 예외로 스크립트를 멈추고 응답에 실패 줄·짧은 추적·스크린샷 경로를 준다. 성공은 요약과 return 값만. 3개 이상 연속 조작이나 반복이 있으면 이걸 쓴다(예: `xapper.type("id=User","t"); xapper.click("text=로그인"); xapper.waitUntil("id=Main"); return xapper.get("id=Status","Text");`)
- `xapper_select` : ComboBox/ListBox 항목 선택
- `xapper_toggle` : CheckBox/ToggleButton 토글
- `xapper_expand` : TreeViewItem/Expander 펼치기/접기
- `xapper_scroll` : ScrollViewer 스크롤
- `xapper_drag` : 드래그 (요소→요소 / 요소+픽셀 오프셋 / 절대 화면 좌표). **시작 지점**이 스플리터·슬라이더·스크롤바 썸이면 그 컨트롤의 드래그 이벤트로 처리해 커서도 포커스도 건드리지 않는다 (목록에 스크롤바가 있다고 항목 드래그가 스크롤로 바뀌지 않는다). 그 외(항목 드롭·캔버스 요소 이동)도 기본은 대상 프로세스 안에서 몰아 커서·포커스를 건드리지 않으며, 그 경로를 설치할 수 없거나 같은 앱의 창이 시작 지점을 덮고 있을 때만 실제 마우스 입력으로 폴백한다(응답에 어느 경로였는지 표시)

### **5. 화면 기록**
- `xapper_screen_recall` / `xapper_screen_learn` / `xapper_screen_forget` : 한 번 파악한 화면을 다시 파악하지 않게 한다. 화면에 들어서면 먼저 `recall` 하고, 아는 화면이면 셀렉터와 기준점 목록을 받아 **스크린샷 없이** 조작한다. 모르는 화면이면 그때만 `annotate` 스크린샷으로 한 번 보고 `learn` 해 둔다
- 화면을 식별하는 지문은 **주 창의 시각 트리에서 뽑은 (타입, Name, AutomationId) 세 값의 중복 없는 집합**이다. 세 가지 선택이 각각 실측으로 정해졌다. **텍스트를 넣지 않아서** 그리드 데이터나 선택 상태가 바뀌어도 같은 화면으로 인식하고, **개수가 아니라 집합으로 세서** 가상화 목록을 스크롤해도 흔들리지 않으며(노드가 105개에서 149개로 늘어도 고유 요소는 46개 그대로), **주 창으로 한정해서** 팝업과 메뉴를 배제한다. 마지막 것이 특히 중요한데, WPF 는 한 번 연 컨텍스트 메뉴를 닫아도 시각 트리에서 없애지 않아 전체 트리로 재면 같은 화면이 여러 지문으로 갈라진다(실측: 전체 트리 4종류 → 주 창만 2종류)
- `learn` 은 **이름과 메모만 받는다.** 영역 목록은 서버가 직접 뽑는다. 영역이 어디에 몇 개 있는지는 기계가 정확히 알고, 이 화면이 무슨 화면인지와 주의할 점은 모델만 판단할 수 있기 때문이다
- 기록하는 영역은 **id·name 으로 잡히는 것과 기준점이 붙은 것**뿐이다. 텍스트로만 잡히는 것(데이터 행)과 컨트롤 템플릿이 만든 부품은 뺀다. 전자는 데이터가 바뀌면 가리키는 것이 없어지고 필요하면 `xapper_find` 로 즉시 찾을 수 있으며, 후자는 스크롤바 내부 같은 것이라 기록해 둘 값어치가 없다
- 저장소는 `%LOCALAPPDATA%\Xapper\screens.db` 의 SQLite 파일 하나다. JSON 대신 DB 를 쓰는 이유는 MCP 클라이언트가 서버 프로세스를 수시로 다시 띄우고 배포 때마다 죽였다 살리기 때문이다. 파일 하나를 두 프로세스가 동시에 쓰면 깨지고, 쓰는 도중에 죽으면 절반만 적힌 파일이 남는다
- **날짜에 따른 자동 삭제는 없다.** 여기서는 오래된 것과 틀린 것이 같지 않다. 앱이 바뀌면 지문도 바뀌므로 낡은 기록은 틀린 답을 주는 대신 조회에 걸리지 않고 묻힌다. 같은 지문으로 다시 배우면 덮어쓰고, 조회할 때마다 사용 횟수가 쌓이며, 앱을 새로 빌드해 기록이 쓸모없어졌을 때는 `forget` 으로 비운다. 앱당 200개 상한은 안전망일 뿐이다

### **6. 진단 및 검증**
- `xapper_get_property` : 임의 DependencyProperty 값 읽기
- `xapper_get_bindings` : 데이터 바인딩 상태 및 오류 조회
- `xapper_screenshot` : 윈도우/요소 캡처. 그림을 MCP 이미지 콘텐츠로 그대로 반환하므로 호출자가 화면을 직접 볼 수 있다
- `annotate` 를 켜면 히트테스트로 닿을 수 있는 요소에 번호 상자를 그리고, 번호마다 ref 를 적은 목록을 그림과 함께 돌려준다. 이름도 id 도 텍스트도 없는 컨트롤(DevExpress 그리드 셀 등)을 지목할 때 스크린샷과 `xapper_element_at` 을 번갈아 부르던 왕복이 한 번으로 줄어든다. 상자를 받는 것은 다른 후보를 품지 않은 가장 안쪽 요소뿐이며(컨테이너와 컨트롤 템플릿 부품은 제외), 번호 라벨은 요소 바깥 왼쪽에 얹어 화면의 글자를 가리지 않는다. 셀렉터로 지목할 수 없는 요소에는 기준점을 함께 적는다(`in id=GridControl at 0.45,0.2` 형태). ref 는 이번 세션에서만 유효하지만 기준점은 창 크기가 바뀌어도, 세션을 넘겨도 유효하므로 기록해 둘 값은 이쪽이다. 기준점은 유일한 id·name 을 가진 가장 가까운 조상으로 잡으며, 그런 조상이 없으면 붙지 않는다. 상자가 상한을 넘으면 면적이 작은 것을 남기고 잘라낸 개수를 알린다. `mode="render"` 에서만 동작하는데, 상자를 캡처 대상의 좌표계에 겹쳐 그리는 방식이라 화면 픽셀을 읽는 경로에서는 정확도를 보장할 수 없고, 그쪽이 필요한 팝업·메뉴는 각자 최상위 창이라 `xapper_find` 가 이미 보여 주기 때문이다
- `maxWidth`로 비율을 유지한 채 축소, `savePath`로 PNG를 파일에 함께 저장 (저장 실패는 경고로 알리고 그림은 그대로 반환)
- 보고되는 크기는 실제 인코딩된 픽셀 수
- `mode`로 캡처 출처를 고른다. `render`(기본)는 앱의 시각 트리를 다시 그려 창이 가려져 있어도 찍히지만 별도 창·팝업·컨텍스트 메뉴·드롭다운은 담기지 않는다. `screen`은 데스크톱에 합성된 픽셀을 읽어 사람이 보는 그대로 담기지만 위를 덮은 창도 함께 찍힌다
- 담지 못한 것이 있으면 응답이 그 사실을 알린다 — `render`는 열려 있는 다른 창 개수를, `screen`은 앱의 창이 앞에 없다는 사실을
- `xapper_assert` : 속성 값 단언 (PASS/FAIL 반환)
- `xapper_batch` : 여러 단계를 한 호출에 순서대로 실행. 각 단계는 `{"tool": "click", ...그 도구의 인자}` 로 적고(click·doubleclick·rightclick·wheel·type·key·select·toggle·expand·scroll·find·get_property·assert), 첫 실패에서 중단하며 단계별 결과를 번호를 붙여 단일 도구와 같은 형식으로 돌려준다

<br/>

## **설치 및 설정**

### **사전 요구사항**

- **Windows 10/11** (x64 또는 ARM64)
- **.NET 9.0 SDK** ([다운로드](https://dotnet.microsoft.com/download/dotnet/9.0))
- **MCP 클라이언트** (Claude Desktop, Claude Code 등)

### **빌드**

```bash
git clone https://github.com/Conosuke/Xapper.git
cd Xapper
dotnet build
```

빌드가 완료되면 실행 파일이 생성됩니다:

```
src/Xapper.McpServer/bin/Debug/net9.0-windows/Xapper.McpServer.exe
```

> 이 실행 파일 하나로 동작합니다. Inspector DLL과 GenericInjector DLL이 리소스로 내장되어 있어 별도 파일 복사나 환경변수 설정이 필요 없습니다.

### **MCP 서버 연결 (Claude Desktop)**

`%AppData%\Claude\claude_desktop_config.json` 파일에 추가:

```json
{
  "mcpServers": {
    "xapper": {
      "command": "C:\\경로\\Xapper\\src\\Xapper.McpServer\\bin\\Debug\\net9.0-windows\\Xapper.McpServer.exe"
    }
  }
}
```

> `command`에 빌드된 `Xapper.McpServer.exe`의 **절대 경로**를 입력합니다.

### **MCP 서버 연결 (Claude Code)**

프로젝트 루트에 `.mcp.json` 파일 생성:

```json
{
  "mcpServers": {
    "xapper": {
      "command": "C:\\경로\\Xapper\\src\\Xapper.McpServer\\bin\\Debug\\net9.0-windows\\Xapper.McpServer.exe"
    }
  }
}
```

또는 `dotnet run`으로 실행:

```json
{
  "mcpServers": {
    "xapper": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\경로\\Xapper\\src\\Xapper.McpServer"]
    }
  }
}
```

### **환경변수 (선택, 개발자용)**

일반 사용 시 환경변수 설정은 **불필요**합니다. 개발/디버깅 시 DLL 경로를 직접 지정하려면:

| 환경변수 | 설명 |
|:---|:---|
| `XAPPER_INSPECTOR_BASE_DIR` | Inspector DLL 기본 디렉토리 오버라이드 (TFM별 하위 폴더 포함) |
| `XAPPER_GENERIC_INJECTOR_DIR` | GenericInjector DLL 디렉토리 오버라이드 |

### **에이전트 스킬 설치 (권장)**

`skills/xapper` 를 Claude Code 쪽으로 복사하면, 에이전트가 작업을 시작하는 시점에 Xapper 사용 절차를 먼저 읽습니다. 서버 지침과 도구 설명이 닿지 못하는 자리를 메우는 것이라 실제 토큰 사용량 차이가 큽니다. 특히 화면을 한 번 배워 두고 다음 방문에 다시 쓰는 흐름이 여기에 담겨 있습니다.

```bash
# 여러 프로젝트에서 쓸 때
cp -r skills/xapper ~/.claude/skills/

# 한 프로젝트에서만 쓸 때
cp -r skills/xapper <프로젝트>/.claude/skills/
```

자세한 내용은 [`skills/README.md`](skills/README.md) 를 참고하십시오.

### **사용 예시**

MCP 연결 후, AI 에이전트에게 자연어로 요청합니다:

```
"내 WPF 앱에서 로그인 테스트를 해줘.
 UsernameInput에 'admin'을 입력하고 LoginButton을 클릭한 뒤
 StatusText가 'Welcome'을 포함하는지 확인해."
```

AI 에이전트가 자동으로 수행하는 흐름:

```
1. xapper_list_processes       → WPF 프로세스 목록 확인
2. xapper_attach {pid}         → 대상 프로세스에 인젝션
3. xapper_snapshot             → UI 트리 구조 파악
4. xapper_find "UsernameInput" → 입력 필드 ref 획득
5. xapper_type {ref, "admin"}  → 텍스트 입력
6. xapper_click {loginRef}     → 로그인 버튼 클릭
7. xapper_assert {statusRef, "Text", "contains", "Welcome"}
                               → 결과 검증 (PASS/FAIL)
```

### **MCP 도구 전체 목록**

| 도구 | 설명 | 주요 파라미터 |
|------|------|---------------|
| `xapper_list_processes` | WPF 프로세스 목록 | - |
| `xapper_attach` | 프로세스 인젝션 | `pid` |
| `xapper_detach` | 연결 해제 | - |
| `xapper_snapshot` | UI 트리 스냅샷 | `maxDepth` |
| `xapper_find` | 요소 검색 | `name`, `automationId`, `type`, `text` |
| `xapper_element_at` | 좌표의 요소 조회 | `x`, `y`, `maxAncestors` (선택) |
| `xapper_click` | 클릭 | `ref`, `x`, `y` (모두 선택), `modifiers`(선택) |
| `xapper_doubleclick` | 더블클릭 | `ref`, `x`, `y` (x/y 필수), `modifiers`(선택) |
| `xapper_rightclick` | 우클릭 | `ref`, `x`, `y`, `modifiers` (x/y/modifiers 선택) |
| `xapper_wheel` | 마우스 휠 | `ref`, `notches`, `x`, `y`, `modifiers` (x/y/modifiers 선택) |
| `xapper_type` | 텍스트 입력 | `text`, `ref`(선택, 없으면 포커스 요소), `clear`(선택) |
| `xapper_key` | 키 입력 | `key`, `modifiers`(선택), `ref`(선택) |
| `xapper_select` | 항목 선택 | `ref`, `item` |
| `xapper_toggle` | 토글 | `ref` |
| `xapper_expand` | 펼치기/접기 | `ref`, `expand` |
| `xapper_scroll` | 스크롤 | `ref`, `direction`, `amount` |
| `xapper_drag` | 드래그 | `sourceRef`, `targetRef`, `offsetX`/`offsetY`, 화면 좌표 (모두 선택), `modifiers`(선택) |
| `xapper_get_property` | 속성 읽기 | `ref`, `propertyName` |
| `xapper_get_bindings` | 바인딩 조회 | `ref` |
| `xapper_screenshot` | 스크린샷 (이미지 반환) | `ref`, `maxWidth`, `savePath`, `mode` (모두 선택) |
| `xapper_assert` | 값 단언 | `ref`, `propertyName`, `operator`, `expected` |
| `xapper_batch` | 여러 단계를 한 호출에 (첫 실패에서 중단, 단계별 결과) | `steps` (`{"tool", ...}` 배열, 최대 50) |

<br/>


## **동작 원리**

```
┌─────────────────┐     stdio (JSON-RPC)     ┌──────────────────┐
│  AI Agent       │ ◄──────────────────────► │  Xapper.McpServer │
│  (Claude, etc.) │                           └────────┬─────────┘
└─────────────────┘                                    │
                                              P/Invoke │ Native Injection
                                                       ▼
                                              ┌──────────────────┐
                                              │  Target WPF App  │
                                              │  ┌─────────────┐ │
                                              │  │  Inspector  │ │ ← Named Pipe IPC
                                              │  │  (injected) │ │
                                              │  └─────────────┘ │
                                              └──────────────────┘
```

1. AI 에이전트가 MCP 프로토콜로 도구 호출
2. McpServer가 임베디드 리소스에서 DLL을 추출 (최초 1회, `%TEMP%\Xapper\`)
3. NativeInjector가 P/Invoke(CreateRemoteThread + LoadLibraryW)로 GenericInjector DLL을 대상 프로세스에 로드
4. GenericInjector가 Inspector DLL의 EntryPoint.Initialize를 호출하여 인젝션 완료
5. Inspector가 Named Pipe 서버를 열고 McpServer와 IPC 연결
6. McpServer가 Inspector에 명령 전달 → Inspector가 Dispatcher를 통해 UI 스레드에서 실행
7. 결과를 MCP 응답으로 반환

<br/>
<br/>
