# 🚀 LOREWAY TOOLKIT (마비노기 제작 최적화 도구) - AI 인수인계 문서

이 문서는 **새로운 컴퓨터 환경이나 새로운 AI 세션에서 지금까지의 개발 맥락을 100% 즉시 이어받기 위해 작성된 전체 히스토리 및 기술 사양서**입니다.

---

## 1. 📌 프로젝트 개요 및 핵심 정체성
- **프로젝트 공식 명칭**: **`LorewayToolkit`** (어셈블리, 실행 파일명, 프로젝트 파일명 일치)
- **개발 목적**: 2026 넥슨 넥토리얼 포트폴리오 제출용 데스크탑 애플리케이션
- **핵심 목표**: 단순한 경매장 검색기가 아닌, 마비노기 다단계 장비 제작 시 **[경매장 최저가 vs NPC 상점/구슬 교환]** 경로를 재귀적으로 분석하여 **최소 비용 제작 루트와 예상 절감액**을 제시하는 의사결정 지원 도구
- **개발 기간**: 4일 완성형 MVP (실제 동작 가능한 높은 완성도 중심)

---

## 2. 🛠 기술 스택 및 아키텍처
- **Language**: C# 12.0 (.NET 8.0 Windows WPF)
- **Architecture**: 순수 MVVM 패턴 (외부 무거운 프레임워크 배제, `INotifyPropertyChanged`, `ICommand` 직접 구현)
- **External API**: NEXON Open API (Mabinogi Auction API)
- **Local Storage**: JSON (`System.Text.Json` 기반 대소문자 무시 파싱 및 자동 복사)
- **Distribution**: .NET 미설치 PC에서도 즉시 단독 실행되는 **Self-Contained x64 Single-File** 배포

```text
LorewayToolkit (프로젝트 구조)
│
├── Models/
│   ├── Material.cs             # 재료 DTO (이름, 필요수량)
│   ├── Recipe.cs               # 아이템 레시피 DTO
│   ├── AcquisitionMethod.cs    # 수급 경로 (상점/구슬 교환/경매장 등) DTO
│   ├── AuctionItem.cs          # 넥슨 경매장 API 응답 매핑 DTO
│   └── MaterialCostPlan.cs     # 최종 계산된 단일 재료 최적 조달 계획
│
├── ViewModels/
│   ├── ViewModelBase.cs        # INotifyPropertyChanged 구현체
│   ├── RelayCommand.cs         # ICommand 구현체 (버튼 바인딩)
│   └── MainViewModel.cs        # 전체 화면 상태 관리 및 비동기 계산 제어
│
├── Services/
│   ├── RecipeService.cs        # default/user 레시피 로컬 JSON 입출력
│   ├── AcquisitionService.cs   # 상점/구슬 등 고정 수급처 JSON 관리
│   ├── NexonApiService.cs      # 넥슨 API 비동기 호출 (10분 캐시 내장)
│   ├── AuctionCacheService.cs  # 경매장 2-Tier 캐싱 (메모리 + Data/auction-cache.json)
│   └── OptimizationService.cs  # 다단계 재귀 최적화 계산 엔진
│
├── Views/
│   ├── MainWindow.xaml         # 메인 다크 테마 UI
│   ├── MainWindow.xaml.cs      # 순수 MVVM (DataContext = new MainViewModel())
│   └── RecipeEditorWindow.xaml # 커스텀 레시피 등록 팝업
│
├── Data/
│   ├── default-recipes.json    # 기본 등록 마비노기 무기/방어구 레시피
│   ├── acquisition-methods.json# 모험가의 인장, 상점 등 대체 수급처
│   ├── auction-cache.json      # API Key 없을 때 동작하는 오프라인 캐시
│   └── user-recipes.json       # 사용자가 추가한 커스텀 레시피
│
├── LorewayToolkit.csproj       # .NET 8 WPF 프로젝트 설정 (AssemblyName: LorewayToolkit)
├── .env.example                # 넥슨 API Key 템플릿 (보안상 .env는 gitignore 처리)
└── LorewayToolkit/             # 최종 완성된 무설치 배포 폴더
    └── LorewayToolkit.exe      # 단일 실행 파일 (Native DLL 임베딩 완료)
```

---

## 3. 🎯 핵심 개발 및 기술적 결정 히스토리 (중요!)

### (1) 이름 및 명칭 통일
- 프로젝트 초기의 임시 명칭(`MabinogiCraftOptimizer`)에서 최종 공식 명칭 **`LorewayToolkit`**으로 전체 통일 완료.
  - 프로젝트 파일: `LorewayToolkit.csproj`
  - 어셈블리 및 실행 파일: `LorewayToolkit.exe`
  - 배포 폴더: `LorewayToolkit/`
  - 윈도우 창 제목 및 상단 브랜드: `LOREWAY TOOLKIT`

### (2) UI 및 해상도 최적화
- **창 크기 고정**: `Width="1120"`, `Height="780"`, `ResizeMode="CanMinimize"`
- **레이아웃 안정성**: 메인 제작 아이템 콤보박스 및 주요 컨트롤의 가로 폭을 `760px`로 안정감 있게 제한하여 와이드 화면에서도 UI가 늘어지지 않도록 최적화.
- **다크 테마**: 장시간 이용하는 게이머를 위해 눈이 편안한 딥 차콜/다크 슬레이트 팔레트 적용.

### (3) 경매장 2-Tier 캐싱 및 오프라인 폴백
- **API 호출 절약 및 성능**: 1차 Memory Cache(TTL 10분) + 2차 Local JSON Cache(`Data/auction-cache.json`).
- **무설정 체험 보장**: 평가관이 `.env`에 넥슨 API Key를 입력하지 않아도 로컬 캐시 데이터를 통해 즉시 모든 제작 최적화 계산을 100% 체험 가능하도록 방어 로직 구현.

### (4) Self-Contained 단일 실행 파일 빌드
- 면접관 PC에 .NET 8 Runtime이 설치되어 있지 않아도 더블 클릭 한 번으로 실행되도록 설정:
  - `PublishSingleFile=true`, `SelfContained=true`, `RuntimeIdentifier=win-x64`
  - C++ 런타임 DLL 종속성 분리 오류를 방지하기 위해 `IncludeNativeLibrariesForSelfExtract=true` 적용.

### (5) 보안 및 포트폴리오 기준
- 실제 발급받은 NEXON API Key가 담긴 `.env` 파일은 `.gitignore`에 등록되어 GitHub 유출을 원천 방지함. 배포용으로는 `.env.example`만 제공.

---

## 4. 💻 새 컴퓨터에서 작업 재개하는 방법

### 1단계: 프로젝트 폴더 이동
- 새 컴퓨터로 `LorewayToolkit` (또는 기존 작업 폴더)을 복사합니다.

### 2단계: IDE에서 프로젝트 열기
- Visual Studio 또는 Antigravity IDE에서 `LorewayToolkit.csproj` (또는 프로젝트 폴더)를 엽니다.

### 3단계: 새 AI 세션에 프롬프트 입력
새 채팅창에 아래 문구를 그대로 입력하면 AI가 모든 맥락을 즉시 파악합니다:

```text
@AI_CONTEXT.md 파일을 읽고 지금까지의 LorewayToolkit 개발 현황과 아키텍처 결정을 모두 파악해줘. 
C# .NET 8 WPF 환경이며, 이제 다음 작업으로 이어갈 준비가 되었어.
```

---

## 5. 🔮 향후 고려 가능한 작업 로드맵 (Next Steps)
1. **레시피 데이터 확장**: 마비노기 인기 종결 무기(나이트브링어, 페러시우스 등) 공식 레시피 JSON 보강
2. **다단계 하위 재료 분해 시각화**: 트리뷰(TreeView)를 통한 재료 단계별 최적화 경로 도식화
3. **웹/모바일 확장 검토**: Blazor 또는 Avalonia 기반 크로스 플랫폼 포팅
