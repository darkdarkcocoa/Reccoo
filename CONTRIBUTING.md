# Reccoo에 기여하기 / Contributing to Reccoo

레코에 관심을 가져 주셔서 감사합니다! 🎀 버그 신고, 기능 제안, Pull Request 모두 환영합니다.
Thanks for your interest in Reccoo! Bug reports, feature ideas, and PRs are all welcome.

---

## 🐛 버그 신고 & 기능 제안 / Issues

- [Issues](https://github.com/darkdarkcocoa/Reccoo/issues)에 자유롭게 올려 주세요. 한국어와 영어 모두 괜찮습니다.
- 버그라면 **재현 방법**과 **Windows 버전**을 함께 적어 주시면 큰 도움이 됩니다.
- Feel free to file issues in Korean or English. For bugs, please include repro steps and your Windows version.

## 🔀 Pull Request 보내기 / Pull Requests

1. 저장소를 포크하고 브랜치를 만들어 주세요 (`feature/짧은-설명`).
2. 개발에는 [.NET 9 SDK](https://dotnet.microsoft.com/download)와 Windows 10/11이 필요합니다.

   ```powershell
   dotnet run                 # 소스에서 바로 실행
   dotnet build -c Release    # 릴리스 빌드
   ```

3. PR을 열면 CI가 `dotnet build -c Release /warnaserror`로 검사합니다. **경고도 에러로 취급**되므로 로컬에서 먼저 확인해 주세요.
4. PR은 **작은 단위**로 부탁드립니다. 기능 하나, 수정 하나씩이 리뷰하기 좋습니다.
5. 큰 변경(새 화면, 구조 개편 등)은 PR을 열기 전에 이슈로 먼저 상의해 주시면 서로 시간이 절약됩니다.

Fork → branch → make sure `dotnet build -c Release /warnaserror` passes → open a small, focused PR. For large changes, please open an issue first.

## 📐 프로젝트 규칙 / Project Conventions

이 프로젝트는 의도적으로 단순하게 유지하고 있습니다. PR도 이 방향을 따라 주세요.

- **아키텍처**: 소스 파일 몇 개가 전부입니다. 뷰모델, DI, 서비스 레이어 같은 구조를 **도입하지 말아 주세요** — 이 규모에는 과합니다.
- **의존성**: NuGet 패키지는 `NAudio`와 `NAudio.Lame` 두 개뿐입니다. 새 패키지 추가는 꼭 필요한 경우에만, 이슈에서 먼저 상의해 주세요.
- **디자인**: 색상·브러시·컨트롤 스타일은 전부 `App.xaml`의 픽셀 아트 디자인 시스템(크림/코랄/민트/라일락 팔레트, 4px 오프셋 그림자)에 정의되어 있습니다. 새 UI도 반드시 이 토큰을 재사용해 주세요. 하드코딩 색상은 받지 않습니다.
- **UI 문구**: 앱 UI와 마스코트 대사는 **한국어/영어 두 벌**로 `Localization.cs`의 `L10n` 테이블에서 관리됩니다. 문구를 추가하거나 수정할 때는 **두 언어를 모두** 채워 주세요. 톤은 딱딱한 격식체가 아니라 **친근하고 다정한 목소리**입니다 — 두 언어 모두요.
- **코드 스타일**: `Nullable`과 `ImplicitUsings`가 켜져 있습니다. nullable 어노테이션을 지키고, 불필요한 `using` 나열은 피해 주세요.
- **마스코트**: 레코는 이미지 파일이 아니라 `Mascot.cs`에서 픽셀 단위로 그려집니다. 표정 추가는 `MascotMood` enum을 확장하는 방식으로 해 주세요.

Key rules: keep the flat no-framework architecture, no new NuGet deps without discussion, reuse the `App.xaml` design tokens (no hard-coded colors), and keep the friendly, warm voice in all UI strings. UI strings live in both Korean and English in `Localization.cs` — fill in both when adding or changing text.

## 🌏 번역 / Localization

앱 UI는 한국어와 영어를 지원합니다 (타이틀바 KOR | EN 토글). 번역을 다듬거나 새 언어를 제안하고 싶다면 이슈로 알려 주세요 — 언제나 환영입니다!
The app UI supports Korean and English (KOR | EN toggle in the title bar). Want to polish a translation or propose a new language? Open an issue — very welcome!

## 📜 라이선스 / License

PR을 보내시면 해당 기여분도 [MIT 라이선스](LICENSE)로 배포되는 것에 동의하는 것으로 간주됩니다.
By submitting a PR, you agree that your contribution will be licensed under the [MIT License](LICENSE).
