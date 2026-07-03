# Reccoo (레코)

> 🎀 **귀여운 픽셀 아트 사운드 레코더 for Windows**
> A cute pixel-art audio recorder built on .NET 9 + WPF

레코는 **지금 PC에서 나오는 소리를 그대로 담아주는** 작은 레코더야. 스트리밍 음악, 게임 사운드, 라이브 방송 — 스피커로 나오는 소리라면 뭐든 버튼 하나로 녹음해서 WAV나 MP3로 저장해줘. 마이크를 거치지 않으니까 주변 잡음 없이 깨끗하게 담기고, 녹음하는 동안엔 픽셀 마스코트 "레코"가 옆에서 말을 걸어와. 🐤

![Reccoo screenshot](assets/screenshot.png)

![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square)
![WPF](https://img.shields.io/badge/UI-WPF-1f6feb?style=flat-square)
![Windows](https://img.shields.io/badge/OS-Windows-0078D4?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-success?style=flat-square)
[![Latest release](https://img.shields.io/github/v/release/darkdarkcocoa/Reccoo?style=flat-square&color=f5a598)](https://github.com/darkdarkcocoa/Reccoo/releases/latest)

---

## ⬇️ 다운로드

받아서 더블클릭하면 끝이야. 설치 과정도, .NET 런타임 설치도 필요 없어.

> **[📦 Reccoo.exe 받기 (Windows x64, ~76 MB)](https://github.com/darkdarkcocoa/Reccoo/releases/latest/download/Reccoo.exe)** — 이 링크는 항상 **최신 릴리스**를 가리켜
> ([![최신 버전](https://img.shields.io/github/v/release/darkdarkcocoa/Reccoo?style=flat-square&label=%EC%B5%9C%EC%8B%A0%20%EB%B2%84%EC%A0%84&color=b9e4c9)](https://github.com/darkdarkcocoa/Reccoo/releases/latest) · 릴리스 시점: [![릴리스 날짜](https://img.shields.io/github/release-date/darkdarkcocoa/Reccoo?style=flat-square&label=&color=ffd97a)](https://github.com/darkdarkcocoa/Reccoo/releases/latest))
>
> .NET 런타임까지 통째로 들어있는 single-file 실행파일이야. 처음 실행할 때 SmartScreen이 "알 수 없는 앱"이라고 경고할 수 있는데, 아직 서명 인증서가 없어서 그래 — **"추가 정보" → "실행"**을 누르면 정상적으로 열려.

무엇이 바뀌었는지는 [Releases 페이지](https://github.com/darkdarkcocoa/Reccoo/releases)에서 볼 수 있어.

---

## ✨ 이런 걸 할 수 있어 / Features

**🎙️ 녹음하기**

- **시스템 사운드를 그대로 캡처** — 스피커로 나오는 소리를 손실 없이 담아 (WASAPI loopback 방식)
- **3초 카운트다운** — 시작 버튼을 누르면 셋을 세어주니까, 타이밍 맞출 여유가 생겨
- **일시정지 / 재개** — 광고가 나오면 잠깐 멈췄다가 이어서 녹음하면 돼
- **WAV / MP3 선택** — 원본 그대로 남기려면 WAV, 가볍게 보관하려면 MP3 (LO / MED / HI 품질 선택)

**👀 보는 재미, 듣는 재미**

- **라이브 파형** — 지금 나는 소리가 56개의 픽셀 바로 출렁여 (mint → gold → coral 그라데이션)
- **입력 레벨 미터** — 18칸 픽셀 미터로 소리 크기를 한눈에
- **마스코트 "레코"** — 소리에 맞춰 들썩이고, 녹음 상태 따라 표정이 바뀌고, 가끔 혼잣말도 해
- **다크 테마** — 타이틀바의 토글 하나로 낮/밤 전환

**📼 녹음이 끝나면**

- **보관함** — 녹음본이 카세트 테이프 카드로 차곡차곡 쌓여. 길이 표시, 인라인 이름 바꾸기까지
- **미니 플레이어** — 앱 안에서 바로 재생하고 진행바로 탐색해
- **드래그로 내보내기** — 카드를 잡아서 폴더나 메신저에 끌어다 놓으면 끝
- **휴지통 삭제** — 지워도 휴지통으로 가니까 실수해도 복구할 수 있어
- **키보드 단축키** — 마우스 없이도 녹음을 시작하고 멈출 수 있어

---

## 🛠️ 직접 빌드하고 싶다면

소스에서 바로 돌려볼 수도 있어. 필요한 건 [.NET 9 SDK](https://dotnet.microsoft.com/download)와 Windows 10/11, 이게 전부야.

```powershell
git clone https://github.com/darkdarkcocoa/Reccoo.git
cd Reccoo
dotnet run
```

릴리스 빌드는 이렇게:

```powershell
dotnet build -c Release
./bin/Release/net9.0-windows/Reccoo.exe
```

녹음은 기본 출력 장치에서 캡처되고, 파일은 `Documents\Music\Reccoo\` 폴더에 `Reccoo_yyyyMMdd_HHmmss.{wav|mp3}` 이름으로 저장돼.

---

## 📁 프로젝트 구조

구조는 일부러 단순하게 유지하고 있어 — 프레임워크 없이 소스 파일 몇 개가 전부야.

```
Reccoo/
├── App.xaml                  # 픽셀 테마 ResourceDictionary (팔레트 + 컨트롤 스타일)
├── App.xaml.cs
├── MainWindow.xaml           # 1216×736 메인 레이아웃
├── MainWindow.xaml.cs        # 트랜스포트 / 파형 / 보관함 로직
├── Mascot.cs                 # 레코 스프라이트 — 이미지가 아니라 픽셀 단위로 직접 그려
├── AudioRecorder.cs          # NAudio 캡처 + WAV/MP3 인코딩
├── AssemblyInfo.cs
├── Reccoo.csproj
├── Fonts/
│   ├── PixelifySans.ttf      # OFL 1.1
│   ├── DotGothic16-Regular.ttf  # OFL 1.1 (한글 픽셀)
│   └── *-OFL.txt             # 폰트 라이선스
└── design/                   # Claude Design 시안 (gitignored)
```

---

## 🎵 임베드 폰트

| 폰트 | 용도 | 라이선스 |
|------|------|----------|
| [Pixelify Sans](https://fonts.google.com/specimen/Pixelify+Sans) | 영문/숫자 픽셀 글꼴 | OFL 1.1 |
| [DotGothic16](https://fonts.google.com/specimen/DotGothic16) | 한글/CJK 픽셀 글꼴 | OFL 1.1 |

WPF의 글리프 폴백 덕분에 `Pixelify Sans → DotGothic16 → Consolas` 순으로 알아서 매칭돼 — 한글도 별도 처리 없이 픽셀체로 나와.

---

## 🎨 디자인 토큰

레코의 팔레트야. UI를 만질 일이 있다면 새 색을 만들지 말고 이 안에서 골라줘.

```
ink-dark   #3A2F4A      cream      #F6ECD6
ink        #4A3C5C      cream-deep #ECDCB8
ink-soft   #7A6A8A      paper      #FFF8E7
                        coral      #F5A598
mint       #B9E4C9      coral-deep #E87F6E
mint-deep  #87CAA4      lilac      #C9B8E8
gold       #FFD97A      lilac-deep #A48DD0
```

---

## 🤝 기여하기

버그 신고, 아이디어, PR 모두 환영이야! 시작하기 전에 [CONTRIBUTING.md](CONTRIBUTING.md)를 한 번 읽어줘 — 프로젝트가 지키는 결이 정리돼 있어.

---

## 📜 License

[MIT](LICENSE) © darkcocoa — 저작권 표시만 남겨주면 마음껏 가져다 써도 돼.

폰트는 각각 [SIL Open Font License 1.1](Fonts/PixelifySans-OFL.txt)을 따라.

---

<sub>🤖 시안 디자인 + WPF 구현은 [Claude Code](https://claude.com/claude-code)와 함께</sub>
