# Reccoo에 기여하기 / Contributing to Reccoo

레코에 관심 가져줘서 고마워! 🎀 버그 신고, 기능 제안, PR 모두 환영이야.
Thanks for your interest in Reccoo! Bug reports, feature ideas, and PRs are all welcome.

---

## 🐛 버그 신고 & 기능 제안 / Issues

- [Issues](https://github.com/darkdarkcocoa/Reccoo/issues)에 자유롭게 올려줘. 한국어/영어 둘 다 OK.
- 버그라면 **재현 방법**과 **Windows 버전**을 같이 적어주면 큰 도움이 돼.
- Feel free to file issues in Korean or English. For bugs, please include repro steps and your Windows version.

## 🔀 PR 보내는 법 / Pull Requests

1. 저장소를 포크하고 브랜치를 만들어줘 (`feature/짧은-설명`).
2. 개발에는 [.NET 9 SDK](https://dotnet.microsoft.com/download)와 Windows 10/11이 필요해.

   ```powershell
   dotnet run                 # 소스에서 바로 실행
   dotnet build -c Release    # 릴리스 빌드
   ```

3. PR을 열면 CI가 `dotnet build -c Release /warnaserror`로 검사해 — **경고 하나도 에러로 취급**되니까 로컬에서 먼저 확인해줘.
4. PR은 **작은 단위**로 부탁해. 기능 하나, 수정 하나씩.
5. 큰 변경(새 화면, 구조 개편 등)은 PR 전에 이슈로 먼저 상의해주면 서로 시간이 절약돼.

Fork → branch → make sure `dotnet build -c Release /warnaserror` passes → open a small, focused PR. For large changes, please open an issue first.

## 📐 프로젝트 규칙 / Project Conventions

이 프로젝트는 의도적으로 단순하게 유지하고 있어. PR도 이 결을 따라줘:

- **아키텍처**: 소스 파일 몇 개가 전부야. 뷰모델, DI, 서비스 레이어 같은 걸 **도입하지 말아줘** — 이 규모에선 과해.
- **의존성**: NuGet 패키지는 `NAudio` + `NAudio.Lame` 딱 둘이야. 새 패키지 추가는 정말 강한 이유가 있을 때만, 이슈에서 먼저 상의.
- **디자인**: 색·브러시·컨트롤 스타일은 전부 `App.xaml`의 픽셀 아트 디자인 시스템(크림/코랄/민트/라일락 팔레트, 4px 오프셋 그림자)에 정의돼 있어. 새 UI도 반드시 이 토큰을 재사용해줘. 하드코딩 색상 금지!
- **UI 문구**: 현재 앱 UI와 마스코트 대사는 **한국어 전용**이고, 딱딱한 격식체가 아니라 **친근한 반말 톤**이야 (예: "아직 녹음이 없어요" ✕ → "아직 녹음이 없어! 위 버튼을 눌러 시작해" 스타일). 문구를 추가하거나 고칠 때 이 목소리를 유지해줘.
- **코드 스타일**: `Nullable`과 `ImplicitUsings`가 켜져 있어. nullable 어노테이션을 지키고, 불필요한 `using` 나열은 하지 말아줘.
- **마스코트**: 레코는 이미지 파일이 아니라 `Mascot.cs`에서 픽셀 단위로 그려져. 표정 추가는 `MascotMood` enum 확장으로.

Key rules: keep the flat no-framework architecture, no new NuGet deps without discussion, reuse the `App.xaml` design tokens (no hard-coded colors), and match the friendly casual Korean voice in all UI strings (the UI is currently Korean-only).

## 🌏 번역 / Localization

영어 UI 지원은 아직 없어 — 관심 있으면 이슈에서 손들어줘, 환영이야!
There is no English UI yet. If you'd like to work on localization, raise your hand in an issue — very welcome!

## 📜 라이선스 / License

PR을 보내면 그 기여분도 [MIT 라이선스](LICENSE)로 배포되는 것에 동의하는 거야.
By submitting a PR, you agree that your contribution will be licensed under the [MIT License](LICENSE).
