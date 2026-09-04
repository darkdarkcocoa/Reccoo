using System.Globalization;
using System.Windows;

namespace CocoaRecorder;

public enum AppLanguage { Korean, English }

/// <summary>
/// 초경량 로컬라이제이션. XAML은 {DynamicResource L_키}, 코드는 L10n.T("키")로 읽는다.
/// 언어 전환 시 Application.Resources의 문자열을 통째로 갈아끼우는 방식 —
/// ApplyTheme의 Recolor와 같은 패턴이라 별도 프레임워크가 필요 없다.
/// </summary>
public static class L10n
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Korean;
    public static bool IsKorean => Current == AppLanguage.Korean;
    public static event Action? LanguageChanged;

    /// <summary>
    /// 시작 언어는 언제나 영어다. 앱을 처음 보는 사람이 누구든 같은 화면에서 출발하고,
    /// 한국어는 타이틀바 토글로 바꾼다 — 그 선택은 설정에 저장되어 다음 실행에도 유지된다.
    /// </summary>
    public static void Init()
    {
        Current = AppLanguage.English;
        Apply();
    }

    public static void Set(AppLanguage lang)
    {
        if (Current == lang) return;
        Current = lang;
        Apply();
        LanguageChanged?.Invoke();
    }

    public static string T(string key) => IsKorean ? Table[key].Ko : Table[key].En;

    public static string FormatMetaDate(DateTime dt) =>
        IsKorean ? dt.ToString("M월 d일 HH:mm")
                 : dt.ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);

    public static string[] StartMessages => IsKorean ? StartKo : StartEn;
    public static string[] StopMessages  => IsKorean ? StopKo  : StopEn;
    public static string[] IdleMessages  => IsKorean ? IdleKo  : IdleEn;

    private static void Apply()
    {
        foreach (var (key, text) in Table)
            Application.Current.Resources["L_" + key] = IsKorean ? text.Ko : text.En;
    }

    private static readonly Dictionary<string, (string Ko, string En)> Table = new()
    {
        // 타이틀바 탭 — 레일의 SOURCE / FORMAT 같은 라벨은 디자인대로 영문 고정이다.
        ["NavRecord"]       = ("녹음", "record"),
        ["NavLibrary"]      = ("보관함", "library"),
        ["NavSettings"]     = ("설정", "settings"),
        ["NavHelp"]         = ("도움말", "help"),

        // 상태 표시 — 점은 별도 요소라 글머리 기호를 붙이지 않는다.
        ["StatusIdle"]      = ("대기 중", "STANDING BY"),
        ["StatusRecording"] = ("녹음 중", "RECORDING"),
        ["StatusPaused"]    = ("일시정지", "PAUSED"),
        ["StatusCountdown"] = ("곧 시작", "STARTING IN"),
        ["SystemSound"]     = ("시스템 사운드", "system audio"),

        // 트랜스포트
        ["RecordStart"]     = ("녹음", "RECORD"),
        ["PauseBtn"]        = ("일시정지", "PAUSE"),
        ["ResumeBtn"]       = ("재개", "RESUME"),
        ["StopBtn"]         = ("정지", "STOP"),
        ["CancelBtn"]       = ("취소", "CANCEL"),
        ["CountdownUnit"]   = ("{0}초", "{0}s"),
        ["CountdownZero"]   = ("바로", "off"),
        ["CountdownReady"]  = ("준비하세요 — ESC 로 취소", "GET READY — ESC to cancel"),
        ["CountdownHint"]   = ("녹음이 시작되기까지 기다릴 시간이에요. 0으로 두면 누르는 즉시 녹음해요",
                               "How long to wait before recording starts — at 0 it records the moment you press Record"),
        ["SoundOnHint"]     = ("카운트다운 숫자마다 코코아가 냥 하고 세어 줘요 — 눌러서 끄기",
                               "Cocoa meows the countdown — click to mute"),
        ["SoundOffHint"]    = ("카운트다운이 조용해요 — 눌러서 냥 소리 켜기",
                               "The countdown is silent — click to let Cocoa meow"),
        ["SoundNoneHint"]   = ("카운트다운이 0이면 냥 소리를 낼 자리가 없어요",
                               "No countdown, no meow — set a countdown first"),
        ["ShortcutHint"]    = ("SPACE 시작/정지 · P 일시정지 · CTRL+O 폴더",
                               "SPACE start/stop · P pause · CTRL+O folder"),

        // 단축키 목록 (settings 탭)
        ["Key1"]            = ("녹음 시작 · 정지", "start or stop recording"),
        ["Key2"]            = ("일시정지 · 다시 시작", "pause and resume"),
        ["Key3"]            = ("저장 폴더 열기", "open the save folder"),
        ["Key4"]            = ("코코아 다시 부르기", "bring cocoa back here"),

        // 도움말 — 코코아가 직접 설명하는 네 단계
        ["HelpTitle"]       = ("이렇게 쓰면 돼냥", "HOW THIS WORKS"),
        ["HelpSub"]         = ("네 가지만 알면 끝이다냥. 아무 단계나 톡 눌러보라냥.",
                               "Four things and you're done. Tap any step to hear it again."),
        ["CocoaSays"]       = ("코코아가 말한다냥", "COCOA SAYS"),
        ["CheatTitle"]      = ("코코아 치트시트", "COCOA'S CHEAT SHEET"),
        ["HelpBack"]        = ("뒤로", "BACK"),
        ["HelpNext"]        = ("다음", "NEXT"),
        ["HelpDone"]        = ("알았다냥 — 녹음하자!", "GOT IT — LET'S RECORD"),
        ["HelpFoot"]        = ("✦ 코코아 › 도움말 이나 F1 로 다시 부를 수 있다냥",
                               "reopen anytime from ✦ cocoa › help · F1"),
        ["Step1Title"]      = ("1. 소리가 어디서 나는지 알려주라냥", "1. tell me where the sound is"),
        ["Step1Body"]       = ("스피커에서 나오는 소리를 내가 그대로 듣고 있다냥. 마이크는 필요 없어냥 — 출력 장치만 골라주면 내가 쫑긋 듣고 있을게냥.",
                               "I listen to whatever your speakers are already playing. No microphone needed — just pick the output device and I'll hear it."),
        ["Step2Title"]      = ("2. 분홍색 버튼을 눌러주라냥", "2. press the pink button"),
        ["Step2Body"]       = ("SPACE 를 눌러도 된다냥. 카운트다운을 켜두면 3 · 2 · 1 세고 시작하니까 탭으로 돌아갈 시간이 생긴다냥. 숫자마다 내가 냥 하고 세어줄게냥 — 조용히 필요하면 내 얼굴 버튼을 눌러달라냥.",
                               "SPACE works too. If you set a countdown I'll wait 3 · 2 · 1 first, so you have time to get back to your tab. I'll meow each count — tap my face button if you need me quiet."),
        ["Step3Title"]      = ("3. 언제든 멈춰도 괜찮다냥", "3. pause whenever you like"),
        ["Step3Body"]       = ("P 로 멈추고 P 로 다시 시작한다냥. 그동안 나는 몸을 말고 낮잠 자면서 시간을 그대로 지켜줄게냥.",
                               "P pauses, P resumes. I curl up and keep the timer exactly where you left it, so nothing gets lost."),
        ["Step4Title"]      = ("4. 녹음한 건 아래층에 있다냥", "4. your tapes live downstairs"),
        ["Step4Body"]       = ("테이프마다 버튼이 붙어 있다냥 — ▶ 로 듣고, ✕ 로 버린다냥. 이름은 두 번 눌러서 바꾸면 된다냥!",
                               "Every tape carries its own buttons — ▶ to listen, ✕ to bin it. Double-click the name to rename. That's everything!"),

        // 마스코트 인사
        ["Hello"]           = ("안녕! 오늘은\n뭘 녹음할까?", "Hi! What are we\nrecording today?"),

        // 보관함
        ["CountFmt"]        = ("{0}개", "{0} RECORDINGS"),
        ["Feedback"]        = ("건의하기", "Feedback"),
        ["FeedbackHint"]    = ("GitHub 이슈로 연결돼요 — 바라는 점이나 버그를 남겨 주세요",
                               "Opens a new GitHub issue — tell us what you want or what broke"),
        ["Update"]          = ("업데이트", "Updates"),
        ["UpdateCurrent"]   = ("지금 쓰고 계신 버전은 v{0} 이에요", "You are on v{0}"),
        ["UpdateReady"]     = ("새 버전 {0} 이 나왔어요 — 눌러서 뭐가 바뀌었는지 보세요",
                               "{0} is out — click to see what changed"),
        ["GithubHint"]      = ("이 앱이 만들어진 곳을 브라우저에서 열어요",
                               "Opens the repository this app is built in"),
        ["RecentMore"]      = ("그 밖에 {0}개 — 보관함 탭에 전부 있어요", "{0} more — the library tab has them all"),
        ["CardTooltip"]     = ("더블클릭으로 이름 변경 · 끌어서 내보내기", "double-click to rename · drag out to export"),
        ["EmptyTitle"]      = ("아직 테이프가 없어요", "no tapes yet"),
        ["EmptyHint"]       = ("녹음을 누르면 코코아가 듣기 시작해요", "hit RECORD and cocoa will start listening"),

        // 다이얼로그 / 카운트다운
        ["FolderDialogTitle"] = ("저장 폴더 선택", "Choose a save folder"),
        ["MsgSoundOn"]      = ("냥냥! 내가 세어 줄게 ♪", "Meow! I'll count for you ♪"),
        ["MsgSoundOff"]     = ("알았어, 조용히 셀게... zZ", "Okay, I'll count quietly... zZ"),
        ["Count3"]          = ("셋!", "Three!"),
        ["Count2"]          = ("둘!", "Two!"),
        ["Count1"]          = ("하나!", "One!"),
        ["CountWait"]       = ("준비하고 있어~", "Getting ready~"),

        // 마스코트 안내/오류 대사
        ["MsgClosing"]      = ("마무리 중...\n잠깐만!", "Wrapping up...\njust a sec!"),
        ["MsgSaving"]       = ("저장 중...\n잠깐만!", "Saving...\njust a sec!"),
        ["MsgCancelled"]    = ("취소했어!\n다시 누르면 시작~", "Cancelled!\nPress again to go~"),
        ["MsgPaused"]       = ("잠깐 멈췄어!\n준비되면 재개~", "Taking a breather!\nResume when ready~"),
        ["MsgResumed"]      = ("♪ 다시 들어볼게!", "♪ Back to listening!"),
        ["MsgPickDevice"]   = ("장치를 먼저\n선택해줘!", "Pick a device\nfirst!"),
        ["MsgPlaying"]      = ("재생 중!\n♪~", "Now playing!\n♪~"),
        ["MsgNameExists"]   = ("같은 이름이\n이미 있어ㅠ", "That name is\nalready taken :("),
        ["MsgDeviceLoadFail"] = ("장치 로드 실패ㅠ", "Couldn't load devices :("),
        ["MsgFolderOpenFail"] = ("폴더 열기 실패ㅠ", "Couldn't open folder :("),
        ["MsgStartFail"]    = ("시작 실패ㅠ", "Couldn't start :("),
        ["MsgError"]        = ("오류ㅠ", "Oops, error :("),
        ["MsgRenameFail"]   = ("이름 바꾸기 실패", "Rename failed"),
        ["MsgPlayFail"]     = ("재생 실패ㅠ", "Playback failed :("),
        ["MsgFeedback"]     = ("의견 고마워!\n귀 기울이고 있을게 ♡", "Thanks for telling me!\nI'm listening ♡"),
        ["MsgGithub"]       = ("내가 만들어진 곳이야!\n구경하고 와 ♡", "That is where I was made!\nGo have a look ♡"),
        ["MsgUpdate"]       = ("뭐가 바뀌었는지\n보러 가자! ♡", "Let's go see\nwhat changed! ♡"),
        ["MsgLinkFail"]     = ("링크를 열지 못했어ㅠ", "Couldn't open the link :("),
        ["MsgDeleteFail"]   = ("삭제 실패ㅠ", "Delete failed :("),
    };

    // ===== 마스코트 대사 풀 — 언어별로 같은 결의 목소리를 유지한다 =====

    private static readonly string[] StartKo =
    {
        "♪ 잘 들리고 있어!\n좋은 소리야~",
        "오 이거 진짜\n좋은데?!",
        "녹음 시작!\n맘에 들 거야 ♡",
        "쉿... 다 듣고\n있어~",
        "어디 누가 떠드는\n거야 ㅋㅋ",
        "♪ 흥얼흥얼...",
        "이거 명곡각!\n잘 잡자",
        "딱 좋은 타이밍!",
        "음원 수집 중...",
        "오케이 OK!\n잘 들어가는 중",
    };

    private static readonly string[] StartEn =
    {
        "♪ Hearing it loud\nand clear~",
        "Ooh, this one's\nactually good?!",
        "Recording!\nYou'll love this ♡",
        "Shh... I'm getting\nall of it~",
        "Who's making all\nthat noise? hehe",
        "♪ humming along...",
        "Banger alert!\nLet's catch it",
        "Perfect timing!",
        "Collecting sounds...",
        "Okay OK!\nComing in nicely",
    };

    private static readonly string[] StopKo =
    {
        "잘 저장됐어!\n또 녹음할까?",
        "굿굿! 들어보자~",
        "이거 보관해두자!\n♡",
        "오 좋은 소리\n잡았어 ♪",
        "완벽! 명작!",
        "또 듣고 싶으면\n클릭해줘",
        "녹음 끝!\n잘 했어 ♡",
        "와 길게 했네!\n수고했어~",
        "이것도 추가!\n보관함이 풍성~",
        "딱 좋은 길이!",
    };

    private static readonly string[] StopEn =
    {
        "Saved!\nRecord another?",
        "Nice! Let's\nhear it~",
        "This one's a\nkeeper! ♡",
        "Ooh, caught a\ngood one ♪",
        "Perfect!\nA masterpiece!",
        "Click me if you\nwant a replay",
        "All done!\nGreat job ♡",
        "Wow, a long one!\nNice work~",
        "Added!\nThe shelf grows~",
        "Just the right\nlength!",
    };

    private static readonly string[] IdleKo =
    {
        "안녕! 오늘은\n뭘 녹음할까?",
        "또 만났네 ♡",
        "Space 누르면\n시작!",
        "♪ 음악 듣자~",
        "오늘 컨디션\n좋다!",
        "뭐 재미난 거\n있어?",
    };

    private static readonly string[] IdleEn =
    {
        "Hi! What are we\nrecording today?",
        "We meet again ♡",
        "Press Space\nto start!",
        "♪ Let's listen to\nsome music~",
        "Feeling great\ntoday!",
        "Got anything\nfun?",
    };
}
